#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only typed seam for the PlayMode projectile/ability integration tests ??? the
/// "drive two scripted pawns at each other and assert the effects" rig.
///
/// Why it exists: the <c>Arena.PlayTests</c> asmdef can't reference Assembly-CSharp, where the
/// gameplay types live (<see cref="Pawn"/>, <see cref="PawnMovement"/>, <see cref="PawnWeapon"/>,
/// <see cref="PawnColor"/>, <see cref="PawnActiveAbilities"/>, <see cref="PawnPassiveAbilities"/>,
/// <see cref="NetworkPlayer"/>, <see cref="NetworkPlayerManager"/>). Tests reach this class by reflection
/// (<c>Type.GetType("CombatTestRig, Assembly-CSharp")</c>) and call its static methods ??? the same
/// pattern as <see cref="MatchTestRig"/> / <c>AbilityTestRig</c> / <c>ProjectileSpawnTestRig</c>.
/// Reflection is contained to the test???rig boundary; INSIDE the rig everything is typed.
///
/// It assumes the single-host CombatArena fixture: one NetworkManager (host) and a scene that
/// contains a <see cref="NetworkPlayerManager"/> (so projectile owner-resolution in
/// <c>ServerCollision.ResolvePlayer</c> works) but NOT the full MatchMediator orchestration.
///
/// Design notes:
/// - Combatants are spawned from <c>TestCombatant.prefab</c> (a Pawn Root variant: full combat
///   stack ??? movement/weapon/knockback/color/temperature/abilities ??? driven by
///   <see cref="ScriptedPawnInput"/> instead of BotBrain).
/// - Continuous combat (move/aim/fire) is driven through the input component, exactly like the
///   shipped game. Ability USE/CYCLE go through the server methods directly (ServerUse /
///   ServerCycleSelected), matching AbilityTestRig and avoiding edge-trigger timing fragility.
/// - Knockback's <c>PendingImpulses</c> is consumed+cleared every frame, so it can't be polled
///   after the fact: assert on <see cref="Velocity"/> / position delta, or the captured damage
///   events, instead.
/// Keep this thin ??? it is glue, not logic.
/// </summary>
public static class CombatTestRig
{
    public const string CombatantPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestCombatant.prefab";
    public const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";

    // pawn -> the NetworkPlayer we spawned to own it (for attribution + owner-lookup effects).
    static readonly Dictionary<Pawn, NetworkPlayer> _owners = new();

    // ---- component resolution (combatant components live on named child GOs) ---------------

    static Pawn PawnOf(GameObject go) => go.GetComponent<Pawn>();
    static ScriptedPawnInput Input(GameObject go) => go.GetComponentInChildren<ScriptedPawnInput>(true);
    static PawnMovement Movement(GameObject go) => go.GetComponentInChildren<PawnMovement>(true);
    static PawnWeapon Weapon(GameObject go) => go.GetComponentInChildren<PawnWeapon>(true);
    static PawnColor Color_(GameObject go) => go.GetComponentInChildren<PawnColor>(true);
    static PawnActiveAbilities ActiveAbilities(GameObject go) => go.GetComponentInChildren<PawnActiveAbilities>(true);
    static PawnPassiveAbilities PassiveAbilities(GameObject go) => go.GetComponentInChildren<PawnPassiveAbilities>(true);
    static KnockbackReceiver Knockback(GameObject go) => go.GetComponentInChildren<KnockbackReceiver>(true);
    static HitstunReceiver Hitstun(GameObject go) => go.GetComponentInChildren<HitstunReceiver>(true);

    static NetworkPlayerManager Players() => Object.FindAnyObjectByType<NetworkPlayerManager>();

    public static bool ManagerPresent() => Players() != null;

    // ---- spawn / arrange -------------------------------------------------------------------

    /// <summary>Spawn one scripted combatant facing <paramref name="facing"/>, link it to a freshly
    /// spawned NetworkPlayer (registered so projectiles attribute + resolve owner reactions), and
    /// return the pawn GameObject. <paramref name="health"/> &lt; 0 leaves the prefab/Pawn default.</summary>
    public static GameObject SpawnCombatant(Vector3 position, Vector3 facing, bool isBot = true, int health = -1)
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) throw new System.Exception("CombatTestRig: no NetworkManager (host not started?)");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatantPrefabPath);
        if (prefab == null) throw new System.Exception($"CombatTestRig: missing {CombatantPrefabPath}");
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null) throw new System.Exception($"CombatTestRig: missing {PlayerPrefabPath}");

        Quaternion rot = facing.sqrMagnitude > 1e-5f
            ? Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z).normalized) : Quaternion.identity;
        var pawnGo = Object.Instantiate(prefab, position, rot);
        // Server-spawned (no owning client) — these are server-driven pawns, like bots, so ScriptedPawnInput.IsActive
        // (IsServerInitialized) makes the server simulate their movement and weapon. All the effects we assert run
        // server-authoritative: damage (ServerOnly Health), color (Health.OnChange), knockback (KnockbackReceiver
        // takes its no-owner branch and applies the impulse here), and hitstun (a ServerOnly SyncVar tick).
        nm.ServerManager.Spawn(pawnGo);
        var pawn = pawnGo.GetComponent<Pawn>();

        var playerGo = Object.Instantiate(playerPrefab);
        var np = playerGo.GetComponent<NetworkPlayer>();
        np.IsBot = isBot;
        nm.ServerManager.Spawn(playerGo, nm.ClientManager.Connection);

        // Bidirectional link, exactly as the match flow / ProjectileDamageTestRig wire it.
        pawn.ControllingPlayer.Value = np;
        np.ControlledPawn.Value = pawn;

        if (health > 0)
        {
            pawn.MaxHealth.Value = health;
            pawn.Health.Value = health;
        }

        var pm = Players();
        if (pm != null && !pm.ActivePlayers.Contains(np))
            pm.ActivePlayers.Add(np);

        _owners[pawn] = np;
        return pawnGo;
    }

    /// <summary>Convenience: two combatants at ??distance/2 along Z, facing each other.
    /// Returns [a, b]; a is at -Z looking +Z, b at +Z looking -Z.</summary>
    public static GameObject[] SpawnFacingPair(float distance, int health = -1, float y = 1f)
    {
        float half = distance * 0.5f;
        var a = SpawnCombatant(new Vector3(0f, y, -half), Vector3.forward, true, health);
        var b = SpawnCombatant(new Vector3(0f, y, half), Vector3.back, true, health);
        return new[] { a, b };
    }

    /// <summary>The NetworkPlayer GameObject the rig spawned to own this pawn (for advanced wiring).</summary>
    public static GameObject PlayerObject(GameObject pawnGo)
        => _owners.TryGetValue(PawnOf(pawnGo), out var np) && np != null ? np.gameObject : null;

    public static int PlayerObjectId(GameObject pawnGo)
        => _owners.TryGetValue(PawnOf(pawnGo), out var np) && np != null ? np.ObjectId : -1;

    // ---- commands --------------------------------------------------------------------------

    /// <summary>Point the pawn (and therefore its firePoint, which drives fire direction) at a world
    /// position. Sets rotation directly ??? deterministic and frame-instant, no turn-rate lag.</summary>
    public static void AimAt(GameObject pawnGo, Vector3 worldPos)
    {
        var t = pawnGo.transform;
        Vector3 dir = worldPos - t.position;
        if (dir.sqrMagnitude > 1e-5f)
            t.rotation = Quaternion.LookRotation(dir.normalized);
    }

    public static void SetMove(GameObject pawnGo, Vector2 move) => Input(pawnGo).Move = move;
    public static void SetLook(GameObject pawnGo, Vector2 look) => Input(pawnGo).Look = look;
    public static void SetFiring(GameObject pawnGo, bool fire) => Input(pawnGo).Fire = fire;

    public static void Stop(GameObject pawnGo)
    {
        var input = Input(pawnGo);
        input.Move = Vector2.zero;
        input.Look = Vector2.zero;
        input.Fire = false;
    }

    static readonly FieldInfo WeaponDataField =
        typeof(PawnWeapon).GetField("weaponData", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Swap the equipped weapon (private serialized field on PawnWeapon).</summary>
    public static void SetWeapon(GameObject pawnGo, string weaponDataPath)
    {
        var wd = AssetDatabase.LoadAssetAtPath<WeaponData>(weaponDataPath);
        if (wd == null) throw new System.Exception($"CombatTestRig: missing WeaponData {weaponDataPath}");
        WeaponDataField.SetValue(Weapon(pawnGo), wd);
    }

    /// <summary>Reconfigure the equipped weapon for a multi-pellet fan by swapping in a runtime CLONE of its
    /// WeaponData — so the spread geometry can be exercised without authoring an asset, and the shared SO is never
    /// dirtied. Keeps the original projectile / fire mode; overrides only pellet count, spread angle and type.</summary>
    public static void OverrideWeaponSpread(GameObject pawnGo, int pellets, float spreadAngle, bool even = true)
    {
        var weapon = Weapon(pawnGo);
        var src = (WeaponData)WeaponDataField.GetValue(weapon);
        var clone = Object.Instantiate(src);
        clone.ProjectilesPerShot = pellets;
        clone.SpreadAngle = spreadAngle;
        clone.SpreadType = even ? SpreadType.Even : SpreadType.Random;
        WeaponDataField.SetValue(weapon, clone);
    }

    /// <summary>Contribute a spread modifier to the weapon's fold (effective spread = authored × multiplier).
    /// 1 = unchanged, &gt;1 widens (overheat), &lt;1 tightens (accuracy buff).</summary>
    public static void AddSpreadModifier(GameObject pawnGo, float spreadMultiplier)
        => Weapon(pawnGo).Modifiers.Add(new WeaponModifier { FireRateMultiplier = 1f, SpreadMultiplier = spreadMultiplier });

    /// <summary>Signed yaw (degrees, around world up) of every live projectile relative to the shooter's aim
    /// forward. The Even fan has no Random, so these are deterministic: distinct+symmetric yaws prove each pellet
    /// gets its own slot index (not a shared 0), and the outer magnitude proves the effective (post-modifier)
    /// spread reached the geometry rather than the raw authored angle.</summary>
    public static float[] ProjectileYaws(GameObject shooterGo)
    {
        Vector3 aim = shooterGo.transform.forward;
        var yaws = new List<float>();
        foreach (var kvp in InstanceFinder.ServerManager.Objects.Spawned)
        {
            var nob = kvp.Value;
            if (nob == null || nob.GetComponentInChildren<NetworkedMotion>(true) == null) continue;
            yaws.Add(Vector3.SignedAngle(aim, nob.transform.forward, Vector3.up));
        }
        return yaws.ToArray();
    }

    /// <summary>Contribute a standing knockback-resistance modifier to this pawn's receiver (the receiving end of
    /// the fold). 1 = unchanged, 0 = knockback-immune. How hard a hit shoves is the DEALER's config, authored on
    /// the projectile prefab, so a test tunes the victim's resistance rather than its "knockback strength".</summary>
    public static void SetKnockbackResistance(GameObject pawnGo, float multiplier)
    {
        var kb = Knockback(pawnGo);
        if (kb == null) throw new System.Exception("CombatTestRig: pawn has no KnockbackReceiver");
        kb.Modifiers.Clear();
        kb.Modifiers.Add(new KnockbackModifier { ImpulseMultiplier = multiplier });
    }

    /// <summary>Contribute a standing hitstun-resistance modifier to this pawn's receiver. 1 = unchanged,
    /// 0 = stun-immune.</summary>
    public static void SetHitstunResistance(GameObject pawnGo, float multiplier)
    {
        var hs = Hitstun(pawnGo);
        if (hs == null) throw new System.Exception("CombatTestRig: pawn has no HitstunReceiver");
        hs.Modifiers.Clear();
        hs.Modifiers.Add(new HitstunModifier { DurationMultiplier = multiplier });
    }

    /// <summary>Whether this pawn is currently in hitstun (weapon may not start a new fire event).</summary>
    public static bool IsStunned(GameObject pawnGo)
    {
        var hs = Hitstun(pawnGo);
        return hs != null && hs.IsStunned;
    }

    // ---- abilities (server-authoritative paths) --------------------------------------------

    public static void GivePassive(GameObject pawnGo, string abilityPath)
        => PassiveAbilities(pawnGo).AddPassive(LoadAbility<PassiveAbilityType>(abilityPath));

    public static void GiveActive(GameObject pawnGo, string abilityPath)
        => ActiveAbilities(pawnGo).AddActive(LoadAbility<ActiveAbilityType>(abilityPath));

    /// <summary>Use the selected active — same server method the F-key ServerRpc invokes.</summary>
    public static void UseSelectedAbility(GameObject pawnGo) => ActiveAbilities(pawnGo).ServerUse();

    /// <summary>Cycle the selected active slot (private ServerRpc; runs server logic directly on host).</summary>
    public static void CycleAbility(GameObject pawnGo, int dir)
        => typeof(PawnActiveAbilities).GetMethod("ServerCycleSelected", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(ActiveAbilities(pawnGo), new object[] { dir });

    /// <summary>
    /// Attach a <see cref="PassiveStealOnHit"/> handler to the FIRST passive instance under this pawn — the
    /// spike's stand-in for authoring the component on a passive instance prefab (same serialized surface;
    /// runtime attachment only because the headless run has no editor to author a prefab with). With
    /// <paramref name="enabled"/> false the authored channel mask is set to None, so dispatch must filter the
    /// handler out on every channel.
    /// </summary>
    public static void AttachStealHandler(GameObject pawnGo, bool enabled = true)
    {
        var passives = new List<PassiveAbility>();
        PassiveAbilities(pawnGo).GetPassives(passives);
        if (passives.Count == 0)
            throw new System.Exception("CombatTestRig: pawn has no passive instance to host the steal handler");
        var handler = passives[0].gameObject.AddComponent<PassiveStealOnHit>();
        if (!enabled)
            typeof(PassiveStealOnHit).GetField("enabledChannels", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(handler, HitChannels.None);
    }

    static T LoadAbility<T>(string path) where T : AbilityType
    {
        var a = AssetDatabase.LoadAssetAtPath<AbilityType>(path) as T;
        if (a == null) throw new System.Exception($"CombatTestRig: {path} is not a {typeof(T).Name}");
        return a;
    }

    static readonly List<ActiveAbility> _activesBuf = new();

    public static int ActivesCount(GameObject pawnGo) => ActiveAbilities(pawnGo).ActivesCount;
    public static int PassivesCount(GameObject pawnGo) => PassiveAbilities(pawnGo).PassivesCount;
    public static int SelectedIndex(GameObject pawnGo) => ActiveAbilities(pawnGo).SelectedIndex.Value;

    public static int AbilityUses(GameObject pawnGo, int index)
    {
        ActiveAbilities(pawnGo).GetActives(_activesBuf);
        return index >= 0 && index < _activesBuf.Count ? _activesBuf[index].UsesRemaining.Value : -1;
    }

    // ---- readbacks -------------------------------------------------------------------------

    public static int Health(GameObject pawnGo) => PawnOf(pawnGo).Health.Value;
    public static int MaxHealth(GameObject pawnGo) => PawnOf(pawnGo).MaxHealth.Value;
    public static Vector3 Position(GameObject pawnGo) => pawnGo.transform.position;
    public static Vector3 Velocity(GameObject pawnGo) => Movement(pawnGo).State.Velocity;
    public static float Speed(GameObject pawnGo) => Movement(pawnGo).State.Velocity.magnitude;

    public static int MovementModifierCount(GameObject pawnGo) => Movement(pawnGo).Modifiers.Count;
    public static int WeaponModifierCount(GameObject pawnGo) => Weapon(pawnGo).Modifiers.Count;

    /// <summary>Count entries in the pawn's live weapon modifier list with this exact FireRateMultiplier
    /// (±0.001) — identifies a SPECIFIC contribution (e.g. a passive's authored buff) in a fold that other
    /// systems (PawnTemperature contributes its own row once temperature moves) also write to. Raw counts
    /// are not a valid proxy for one contributor's presence.</summary>
    public static int CountWeaponModifiersWithFireRate(GameObject pawnGo, float fireRateMultiplier)
    {
        int n = 0;
        foreach (var m in Weapon(pawnGo).Modifiers)
            if (Mathf.Abs(m.FireRateMultiplier - fireRateMultiplier) < 0.001f) n++;
        return n;
    }
    public static int PendingImpulseCount(GameObject pawnGo) => Movement(pawnGo).PendingImpulses.Count;

    /// <summary>Currently-applied albedo on the pawn's first mesh renderer (what PawnColor wrote via
    /// "_BaseColor"). Sample within a frame or two of a hit ??? the flash decays back to default.</summary>
    public static Color BodyColor(GameObject pawnGo)
    {
        var renderers = (MeshRenderer[])typeof(PawnColor)
            .GetField("meshRenderers", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(Color_(pawnGo));
        if (renderers == null || renderers.Length == 0 || renderers[0] == null) return Color.clear;
        var mat = renderers[0].material;
        return mat.HasColor("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
    }

    // ---- spawned-object counting (for abilities/weapons that spawn things) ------------------

    /// <summary>Count live server-spawned NetworkObjects carrying a component whose type name matches
    /// <paramref name="componentTypeName"/> (e.g. "NetworkedMotion" for projectiles, or an ability's
    /// spawned-zone component). Lets a test assert "the ability spawned N of X" without referencing X.</summary>
    public static int CountSpawnedWith(string componentTypeName)
    {
        int n = 0;
        foreach (var kvp in InstanceFinder.ServerManager.Objects.Spawned)
        {
            var nob = kvp.Value;
            if (nob == null) continue;
            foreach (var c in nob.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == componentTypeName) { n++; break; }
        }
        return n;
    }

    /// <summary>Count active PawnDamageVfx hit (or death) bursts currently alive in the scene. PawnDamageVfx spawns
    /// pooled clones named "{prefab}(Clone)", each with a ParticleSystem on its root; this counts the active ones by
    /// name so a test can assert one burst per hit (the asServer gate must not double-spawn on a host) without
    /// referencing the VFX prefab directly. Enumerates ParticleSystem (always present — Play() is unconditional)
    /// rather than the optional PooledLifetime.</summary>
    public static int CountActiveVfxClones(GameObject pawnGo, bool death)
    {
        var vfx = pawnGo.GetComponentInChildren<PawnDamageVfx>(true);
        if (vfx == null) return 0;
        var field = typeof(PawnDamageVfx).GetField(death ? "onDeathPrefab" : "onHitPrefab",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var prefab = (GameObject)field.GetValue(vfx);
        if (prefab == null) return 0;
        string cloneName = prefab.name + "(Clone)";
        int n = 0;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.gameObject.activeInHierarchy && ps.gameObject.name == cloneName) n++;
        return n;
    }

    // ---- event capture (damage / death are events; latch them so a test can poll) ----------

    sealed class Capture
    {
        public int DamageEvents;
        public int DeathEvents;
        public DamageInfo Last;
        public System.Action<DamageInfo> Dmg;
        public System.Action<DamageInfo> Death;
        public Pawn Pawn;
    }

    static readonly Dictionary<Pawn, Capture> _caps = new();

    /// <summary>Start latching OnDamageTaken / OnDeath for this pawn. Call before driving the action.</summary>
    public static void BeginCapture(GameObject pawnGo)
    {
        var pawn = PawnOf(pawnGo);
        if (_caps.ContainsKey(pawn)) return;
        var cap = new Capture { Pawn = pawn };
        cap.Dmg = info =>
        {
            cap.DamageEvents++;
            cap.Last = info;
        };
        cap.Death = _ => cap.DeathEvents++;
        pawn.OnDamageTaken += cap.Dmg;
        pawn.OnDeath += cap.Death;
        _caps[pawn] = cap;
    }

    public static int DamageEventCount(GameObject pawnGo)
        => _caps.TryGetValue(PawnOf(pawnGo), out var c) ? c.DamageEvents : 0;

    public static int DeathEventCount(GameObject pawnGo)
        => _caps.TryGetValue(PawnOf(pawnGo), out var c) ? c.DeathEvents : 0;

    public static int LastDamageAmount(GameObject pawnGo)
        => _caps.TryGetValue(PawnOf(pawnGo), out var c) ? c.Last.Amount : 0;

    // NOTE: there is deliberately no "impulse at the damage event" readback. Knockback is no longer a reaction to
    // damage — it is its own exchange, fired from component presence beside the damage exchange (ADR 0002) — so
    // PendingImpulses is still empty when OnDamageTaken fires. Assert on the resulting displacement instead.

    /// <summary>
    /// Destroy the HitstunDealer on every live projectile (composability pin, ADR 0002): with the component gone
    /// the hitstun exchange must fall silent while the damage exchange on the same hit still lands. Idempotent —
    /// call it every frame while bullets are in flight so none lands with its dealer intact. Returns how many
    /// dealers were stripped this call.
    /// </summary>
    public static int StripHitstunDealersFromLiveProjectiles()
    {
        var dealers = UnityEngine.Object.FindObjectsByType<HitstunDealer>(FindObjectsSortMode.None);
        foreach (var d in dealers)
            UnityEngine.Object.Destroy(d);
        return dealers.Length;
    }

    public static bool IsPawnOwner(GameObject pawnGo) => PawnOf(pawnGo).IsOwner;

    /// <summary>Unsubscribe every capture. Call in test teardown so the static latches don't dangle
    /// across the play???edit domain reload.</summary>
    public static void EndCaptures()
    {
        foreach (var c in _caps.Values)
        {
            if (c.Pawn == null) continue;
            c.Pawn.OnDamageTaken -= c.Dmg;
            c.Pawn.OnDeath -= c.Death;
        }
        _caps.Clear();
        _owners.Clear();
    }
}
#endif
