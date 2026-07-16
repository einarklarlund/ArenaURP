using System;
using Arena.Health;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Events;
using HealthCoreState = Arena.Health.HealthState;

/// <summary>
/// The ONE health system for every damageable entity — pawns, bullets, props. Current/Max HP are SyncVars; the
/// arithmetic is the pure <see cref="HealthProcessor"/> (EditMode-tested in Arena.Health.Core). This is the sole
/// <see cref="IDamageable"/>: hit operations and HurtBoxes deal damage here, and everything that cares about a
/// health value reads/subscribes to it (a pawn's <c>Pawn</c> forwards to it so its own consumers don't change).
///
/// Two notification surfaces:
///  - C# events <see cref="OnDamageTaken"/> / <see cref="OnDeath"/> fire IN-PROCESS wherever <see cref="TakeDamage"/>
///    is invoked — for the standard projectile-hit path (ServerCollision → HitResolver) that is the server. They
///    carry the full <see cref="DamageInfo"/> (attacker, amount, hit point) for server-side reactions and kill
///    attribution (PawnManager reads <see cref="OnDeath"/>). CROSS-PEER cosmetic reactions do NOT use these — a hit
///    resolves server-only, so the events never reach a remote client; those reactions ride the
///    <see cref="CurrentHealth"/> SyncVar OnChange, which replication delivers to every peer (see
///    <c>PawnColor</c>, <c>PawnDamageVfx</c>; ADR 0004).
///  - Serialized <see cref="onServerDamage"/> / <see cref="onServerDeath"/> UnityEvents fire SERVER-ONLY, for
///    inspector-wired destructibles.
///
/// The max is AUTHORED ON THE PREFAB (<c>maxHealth</c>); <see cref="OnStartServer"/> re-seeds current/max from it
/// each (pooled) spawn. Set <c>despawnOnDeath = false</c> when something else owns the despawn (a pawn — the
/// PawnManager despawns it; a projectile — its ServerCollision drives despawn off <see cref="IsDead"/>).
/// Projectiles drain durability via <see cref="ServerLose"/> (no events, no despawn).
/// </summary>
public class Health : NetworkBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 1;
    [Tooltip("Despawn this object when it dies. FALSE when something else owns the despawn (pawns: PawnManager; projectiles: ServerCollision).")]
    [SerializeField] private bool despawnOnDeath = true;

    public readonly SyncVar<int> CurrentHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));

    /// <summary>Current HP as a 0..1 fraction of the (buffed) max — the derived surface consumers read
    /// instead of re-deriving <c>CurrentHealth / MaxHealth</c>. Zero max reports 0 (never divides by zero).
    /// Reads the SyncVars, so it is valid on every peer.</summary>
    public float HealthFraction =>
        new HealthCoreState { Hp = CurrentHealth.Value, MaxHp = MaxHealth.Value }.Fraction;

    /// <summary>Fires in-process when damage is applied here — i.e. wherever <see cref="TakeDamage"/> is invoked
    /// (the server, for the standard hit path). Carries the full <see cref="DamageInfo"/>. Cross-peer cosmetics use
    /// <see cref="CurrentHealth"/>.OnChange instead.</summary>
    public event Action<DamageInfo> OnDamageTaken;

    /// <summary>Fires on the authoritative HP&lt;=0 transition (server), carrying the <see cref="DamageInfo"/> —
    /// incl. the attacker, for kill attribution (PawnManager).</summary>
    public event Action<DamageInfo> OnDeath;

    [SerializeField] private DamageInfoEvent onServerDamage = new();
    [SerializeField] private DamageInfoEvent onServerDeath = new();

    public bool IsDead => CurrentHealth.Value <= 0;

    // Per-spawn on-spawn buff layer over the authored max (SpawnBuffReceiver → BuffMaxHealth). Kept SEPARATE
    // from the baseline `maxHealth` field so a pooled reuse never inherits a previous spawn's buff; reset to identity
    // in ResetState. Server-only knob — the SyncVars are ServerOnly, so clients self-heal from the seeded values.
    private float _maxMultiplier = 1f;

    public override void OnStartServer()
    {
        base.OnStartServer();
        SeedFromMax();
    }

    // Effective max = authored baseline × per-spawn buff layer (rounded, floored at 1). Seeds both SyncVars — on
    // spawn current == max — without mutating the authored `maxHealth` field.
    private void SeedFromMax()
    {
        int max = Mathf.Max(1, Mathf.RoundToInt(maxHealth * _maxMultiplier));
        MaxHealth.Value = max;
        CurrentHealth.Value = max;
    }

    /// <summary>
    /// Apply an on-spawn max-health buff as a multiplier over the authored baseline. ORDER-INDEPENDENT (house rule
    /// for shared init across siblings with no guaranteed OnStartNetwork order), mirroring
    /// <see cref="NetworkedMotion.SetSpeedMultiplier"/>: safe to call before <see cref="OnStartServer"/> (the
    /// multiplier is stored and folded in when it seeds) or after (an already-seeded max is recomputed in place).
    ///
    /// Server-only effect: the SyncVars are ServerOnly, so this writes nothing on a client (the buffed values ride
    /// the spawn message and land via SyncVar replication — a client that missed the just-granted passive still gets
    /// the server-authoritative max). Per-spawn buff layer, reset to identity in <see cref="ResetState"/>.
    /// </summary>
    public void SetMaxHealthMultiplier(float multiplier)
    {
        _maxMultiplier = multiplier;
        if (IsServerInitialized)
            SeedFromMax();
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _maxMultiplier = 1f;   // pooled reuse starts unbuffed; OnStartServer re-seeds from the baseline max
    }

    /// <summary>Server: (re)seed max + current HP at runtime (e.g. a buff raising an entity's max).</summary>
    [Server]
    public void ServerInitMax(int max)
    {
        maxHealth = Mathf.Max(1, max);
        MaxHealth.Value = maxHealth;
        CurrentHealth.Value = maxHealth;
    }

    /// <summary>
    /// Apply damage. Raises the in-process <see cref="OnDamageTaken"/> (server-side for the standard hit path),
    /// then — on the server — applies the loss and, on death, raises the authoritative <see cref="OnDeath"/> /
    /// <see cref="onServerDeath"/> and optionally despawns. Cross-peer cosmetic reactions ride
    /// <see cref="CurrentHealth"/>.OnChange, not these events, so they reach every peer.
    /// </summary>
    public void TakeDamage(DamageInfo info)
    {
        if (CurrentHealth.Value <= 0) return;

        // In-process notification for server-side reactions and kill attribution — NOT the cross-peer cosmetic
        // path (that rides the CurrentHealth SyncVar OnChange; see the class doc / ADR 0004).
        OnDamageTaken?.Invoke(info);

        if (!IsServerInitialized)
            return;

        onServerDamage?.Invoke(info);
        ApplyLoss(info.Amount);

        if (CurrentHealth.Value <= 0)
        {
            OnDeath?.Invoke(info);
            onServerDeath?.Invoke(info);
            if (despawnOnDeath)
                Despawn(DespawnType.Pool);
        }
    }

    /// <summary>Server: heal, clamped at <see cref="MaxHealth"/>.</summary>
    [Server]
    public void ServerHeal(int amount)
    {
        var state = new HealthCoreState { Hp = CurrentHealth.Value, MaxHp = MaxHealth.Value };
        CurrentHealth.Value = HealthProcessor.Heal(state, amount).Hp;
    }

    /// <summary>Server: drain HP without the damage-event / despawn path (e.g. projectile self-damage).</summary>
    [Server]
    public void ServerLose(int amount) => ApplyLoss(amount);

    private void ApplyLoss(int amount)
    {
        var state = new HealthCoreState { Hp = CurrentHealth.Value, MaxHp = MaxHealth.Value };
        CurrentHealth.Value = HealthProcessor.ApplyDamage(state, amount).Hp;
    }
}
