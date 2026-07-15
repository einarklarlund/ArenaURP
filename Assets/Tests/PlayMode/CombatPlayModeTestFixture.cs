using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Base fixture for PlayMode projectile/ability integration tests that pit two scripted
    /// "combatant" pawns against each other on a live FishNet host.
    ///
    /// It loads the minimal <c>CombatArena</c> scene (floor + a bare NetworkPlayerManager ??? no
    /// MatchMediator orchestration) and exposes a clean, typed API over <c>CombatTestRig</c>
    /// (Assembly-CSharp, unreferenceable from this asmdef). All reflection into the rig is
    /// contained in <see cref="Invoke"/>; test classes call the typed protected methods below and
    /// never see reflection ??? e.g.:
    ///
    /// <code>
    /// var pair = SpawnFacingPair(8f, health: 50);
    /// var shooter = pair[0]; var target = pair[1];
    /// BeginCapture(target);
    /// AimAt(shooter, Position(target));
    /// SetFiring(shooter, true);
    /// yield return WaitUntil(() => Health(target) < 50);
    /// Assert.Less(Health(target), 50);
    /// Assert.AreNotEqual(0, DamageEventCount(target));
    /// </code>
    ///
    /// Combatants are full-stack Pawn Root variants driven by <c>ScriptedPawnInput</c> (no BotBrain) ???
    /// so movement, weapon, knockback, color and abilities all run exactly as in-game, but the test
    /// chooses every action deterministically. See the rig for the gotchas (knockback impulses clear
    /// each frame; sample BodyColor within a frame or two of a hit).
    /// </summary>
    public abstract class CombatPlayModeTestFixture : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/CombatArena.unity";

        // ---- spawn / arrange ---------------------------------------------------------------

        /// <summary>Spawn one scripted combatant facing <paramref name="facing"/>, linked to a
        /// registered NetworkPlayer. Returns the pawn GameObject.</summary>
        protected GameObject SpawnCombatant(Vector3 position, Vector3 facing, bool isBot = true, int health = -1)
            => (GameObject)Invoke("SpawnCombatant", position, facing, isBot, health);

        /// <summary>Two combatants <paramref name="distance"/> apart on Z, facing each other.
        /// Returns [a (at -Z), b (at +Z)].</summary>
        protected GameObject[] SpawnFacingPair(float distance, int health = -1, float y = 1f)
            => (GameObject[])Invoke("SpawnFacingPair", distance, health, y);

        protected GameObject PlayerObject(GameObject pawn) => (GameObject)Invoke("PlayerObject", pawn);

        // ---- commands ----------------------------------------------------------------------

        protected void AimAt(GameObject pawn, Vector3 worldPos) => Invoke("AimAt", pawn, worldPos);
        protected void SetMove(GameObject pawn, Vector2 move) => Invoke("SetMove", pawn, move);
        protected void SetLook(GameObject pawn, Vector2 look) => Invoke("SetLook", pawn, look);
        protected void SetFiring(GameObject pawn, bool fire) => Invoke("SetFiring", pawn, fire);
        protected void Stop(GameObject pawn) => Invoke("Stop", pawn);
        protected void SetWeapon(GameObject pawn, string weaponDataPath) => Invoke("SetWeapon", pawn, weaponDataPath);
        protected void SetKnockbackResistance(GameObject pawn, float multiplier) => Invoke("SetKnockbackResistance", pawn, multiplier);
        protected void SetHitstunResistance(GameObject pawn, float multiplier) => Invoke("SetHitstunResistance", pawn, multiplier);

        protected void GivePassive(GameObject pawn, string abilityPath) => Invoke("GivePassive", pawn, abilityPath);
        protected void AttachStealHandler(GameObject pawn, bool enabled = true) => Invoke("AttachStealHandler", pawn, enabled);
        protected void GiveActive(GameObject pawn, string abilityPath) => Invoke("GiveActive", pawn, abilityPath);
        protected void UseSelectedAbility(GameObject pawn) => Invoke("UseSelectedAbility", pawn);
        protected void CycleAbility(GameObject pawn, int dir) => Invoke("CycleAbility", pawn, dir);

        // ---- readbacks ---------------------------------------------------------------------

        protected int Health(GameObject pawn) => (int)Invoke("Health", pawn);
        protected int MaxHealth(GameObject pawn) => (int)Invoke("MaxHealth", pawn);
        protected Vector3 Position(GameObject pawn) => (Vector3)Invoke("Position", pawn);
        protected Vector3 Velocity(GameObject pawn) => (Vector3)Invoke("Velocity", pawn);
        protected float Speed(GameObject pawn) => (float)Invoke("Speed", pawn);
        protected int MovementModifierCount(GameObject pawn) => (int)Invoke("MovementModifierCount", pawn);
        protected int WeaponModifierCount(GameObject pawn) => (int)Invoke("WeaponModifierCount", pawn);
        protected int CountWeaponModifiersWithFireRate(GameObject pawn, float fireRateMultiplier) => (int)Invoke("CountWeaponModifiersWithFireRate", pawn, fireRateMultiplier);
        protected Color BodyColor(GameObject pawn) => (Color)Invoke("BodyColor", pawn);

        protected int ActivesCount(GameObject pawn) => (int)Invoke("ActivesCount", pawn);
        protected int PassivesCount(GameObject pawn) => (int)Invoke("PassivesCount", pawn);
        protected int SelectedIndex(GameObject pawn) => (int)Invoke("SelectedIndex", pawn);
        protected int AbilityUses(GameObject pawn, int index) => (int)Invoke("AbilityUses", pawn, index);

        /// <summary>Count live server-spawned NetworkObjects carrying a component named
        /// <paramref name="componentTypeName"/> (e.g. "NetworkedMotion" for projectiles).</summary>
        protected int CountSpawnedWith(string componentTypeName) => (int)Invoke("CountSpawnedWith", componentTypeName);

        // ---- event capture -----------------------------------------------------------------

        protected void BeginCapture(GameObject pawn) => Invoke("BeginCapture", pawn);
        protected int DamageEventCount(GameObject pawn) => (int)Invoke("DamageEventCount", pawn);
        protected int DeathEventCount(GameObject pawn) => (int)Invoke("DeathEventCount", pawn);
        protected int LastDamageAmount(GameObject pawn) => (int)Invoke("LastDamageAmount", pawn);
        protected bool IsPawnOwner(GameObject pawn) => (bool)Invoke("IsPawnOwner", pawn);
        protected bool IsStunned(GameObject pawn) => (bool)Invoke("IsStunned", pawn);
        protected int StripHitstunDealersFromLiveProjectiles() => (int)Invoke("StripHitstunDealersFromLiveProjectiles");

        // ---- coroutine polling helpers -----------------------------------------------------

        protected static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>Pump frames until <paramref name="condition"/> is true or the timeout elapses.
        /// Does NOT assert ??? the caller asserts the post-condition so failures read clearly.</summary>
        protected static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds = 5f)
        {
            float t = 0f;
            while (!condition() && t < timeoutSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        protected IEnumerator WaitUntilHealthBelow(GameObject pawn, int health, float timeoutSeconds = 5f)
            => WaitUntil(() => Health(pawn) < health, timeoutSeconds);

        // ---- rig reflection bridge (contained here) ----------------------------------------

        static readonly Type RigType = Type.GetType("CombatTestRig, Assembly-CSharp");

        static object Invoke(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "CombatTestRig not found in Assembly-CSharp");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"CombatTestRig.{method} not found");
            return mi.Invoke(null, args);
        }

        [UnityTearDown]
        public IEnumerator CombatTearDown()
        {
            // Clear the rig's static damage/death latches before the host stops (base teardown).
            if (RigType != null)
                RigType.GetMethod("EndCaptures", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            yield return null;
        }
    }
}
