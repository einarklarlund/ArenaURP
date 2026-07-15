using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Regression tests for fix A: <c>PawnWeapon.ExecuteShot</c> had two spread bugs at one call site.
    ///
    ///  1. The pellet index was a literal <c>0</c>, so every pellet of a multi-projectile shot got the
    ///     leftmost slot's angle — <c>SpreadType.Even</c> fanned nothing (all pellets collinear).
    ///  2. The post-modifier <c>effectiveSpread</c> was computed but discarded (the 4-arg overload re-read
    ///     the raw authored <c>SpreadAngle</c>), so any <c>WeaponModifier.SpreadMultiplier</c> did nothing.
    ///
    /// These fire a real multi-pellet Even shot through the live host and read the spawned projectiles'
    /// yaws. <see cref="Even_pellets_fan_out_to_distinct_symmetric_angles"/> catches bug 1 (pre-fix the
    /// three yaws are identical); <see cref="A_spread_modifier_widens_the_fan"/> catches bug 2 (pre-fix the
    /// modifier is ignored, so the fan stays at the authored width).
    /// </summary>
    public sealed class WeaponSpreadTests : CombatPlayModeTestFixture
    {
        // Fire exactly one shot: the weapon's cooldown (>= 0.1s) far exceeds a couple of editor frames, so a
        // brief hold produces a single pulse — ProjectilesPerShot projectiles, all spawned the same frame.
        IEnumerator FireOneShot(GameObject shooter)
        {
            SetFiring(shooter, true);
            yield return WaitFrames(2);
            SetFiring(shooter, false);
            yield return WaitFrames(1);
        }

        [UnityTest]
        public IEnumerator Even_pellets_fan_out_to_distinct_symmetric_angles()
        {
            var shooter = SpawnCombatant(new Vector3(0f, 1f, 0f), Vector3.forward);
            yield return new WaitForSeconds(0.3f); // settle onto the floor

            OverrideWeaponSpread(shooter, pellets: 3, spreadAngle: 30f, even: true);
            yield return FireOneShot(shooter);

            // Positive control: the shot actually produced three projectiles, so the yaw assertions below are
            // about a real fan and can't pass vacuously.
            float[] yaws = ProjectileYaws(shooter).OrderBy(y => y).ToArray();
            Assert.AreEqual(3, yaws.Length, "an Even 3-pellet shot should spawn exactly three projectiles");

            // Bug 1: distinct angles. Pre-fix every pellet used index 0, so all three yaws were identical.
            Assert.Greater(yaws[1] - yaws[0], 1f, "adjacent pellets must have distinct angles (pellet index used)");
            Assert.Greater(yaws[2] - yaws[1], 1f, "adjacent pellets must have distinct angles (pellet index used)");

            // Symmetric about the aim (0), spanning the full 30 deg: yaws ~ [-15, 0, +15].
            Assert.AreEqual(0f, yaws[1], 1.0f, "the middle pellet fires straight ahead");
            Assert.AreEqual(-yaws[2], yaws[0], 1.0f, "the fan is symmetric about the aim direction");
            Assert.AreEqual(15f, yaws[2], 1.0f, "the outer pellet sits at +spreadAngle/2");
        }

        [UnityTest]
        public IEnumerator A_spread_modifier_widens_the_fan()
        {
            var shooter = SpawnCombatant(new Vector3(0f, 1f, 0f), Vector3.forward);
            yield return new WaitForSeconds(0.3f);

            // Authored 20 deg, doubled by the modifier => effective 40 deg => outer pellets at +/-20.
            OverrideWeaponSpread(shooter, pellets: 3, spreadAngle: 20f, even: true);
            AddSpreadModifier(shooter, spreadMultiplier: 2f);
            yield return FireOneShot(shooter);

            float[] yaws = ProjectileYaws(shooter).OrderBy(y => y).ToArray();
            Assert.AreEqual(3, yaws.Length, "an Even 3-pellet shot should spawn exactly three projectiles");

            float outer = Mathf.Max(Mathf.Abs(yaws[0]), Mathf.Abs(yaws[2]));
            // Pre-fix the modifier was ignored => outer ~10 (raw 20/2). With the fix => ~20 (effective 40/2).
            Assert.Greater(outer, 15f, "the spread modifier must widen the fan (effective spread reaches the geometry)");
        }
    }
}
