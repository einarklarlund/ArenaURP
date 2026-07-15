using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Evidence spike for the component-handler migration (DESIGN.md thread): passive-steal-ON-HIT built as an
    /// owner-contributed <c>IHitHandler</c>. Exercises the two things the shape must prove — the OWNER-HARVEST
    /// path (a handler authored on a passive instance, discovered at the hit site with no serialized list) and
    /// the CHANNEL FILTER (supported ∩ authored-enabled ∩ current channel). Networked change → PlayMode test.
    /// </summary>
    public class PassiveStealOnHitTests : CombatPlayModeTestFixture
    {
        private const string CarrierPassive = "Assets/Tests/PlayMode/Prefabs/TestScaleBuffPassive.asset";
        private const string LootPassive = "Assets/Tests/PlayMode/Prefabs/TestFireRateBuffPassive.asset";

        [UnityTest]
        public IEnumerator Player_hit_steals_a_passive_kind_from_the_victim()
        {
            var pair = SpawnFacingPair(6f, health: 100);
            var attacker = pair[0];
            var victim = pair[1];
            yield return WaitFrames(2);

            GivePassive(attacker, CarrierPassive); // hosts the steal handler
            GivePassive(victim, LootPassive);      // the loot: its 0.5 fire-rate row identifies it in the fold
            yield return WaitUntil(() => PassivesCount(attacker) == 1 && PassivesCount(victim) == 1);
            Assert.AreEqual(1, PassivesCount(attacker), "arrange: attacker holds the carrier passive");
            Assert.AreEqual(1, PassivesCount(victim), "arrange: victim holds the loot passive");
            // Assert on the loot's IDENTIFIABLE row, never the raw count — other systems (temperature reacting
            // to the hits) contribute their own rows to the same fold.
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(victim, 0.5f), "arrange: the loot's fire-rate buff is folded on the victim");
            AttachStealHandler(attacker);

            AimAt(attacker, Position(victim));
            SetFiring(attacker, true);
            yield return WaitUntil(() => PassivesCount(attacker) == 2, 5f);
            SetFiring(attacker, false);

            Assert.AreEqual(2, PassivesCount(attacker), "the stolen KIND was granted to the attacker");
            // The victim's instance despawns asynchronously with the grant — poll it too.
            yield return WaitUntil(() => PassivesCount(victim) == 0, 2f);
            Assert.AreEqual(0, PassivesCount(victim), "the victim's passive instance was removed");

            // The kind moved, and its standing buff moved with it: the fold row swaps sides.
            yield return WaitUntil(() => CountWeaponModifiersWithFireRate(attacker, 0.5f) == 1
                                      && CountWeaponModifiersWithFireRate(victim, 0.5f) == 0, 2f);
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(attacker, 0.5f), "the stolen kind's weapon buff now folds on the attacker");
            Assert.AreEqual(0, CountWeaponModifiersWithFireRate(victim, 0.5f), "the victim's re-derive dropped the stolen kind's buff");
        }

        [UnityTest]
        public IEnumerator Authored_channel_mask_of_none_silences_the_steal()
        {
            var pair = SpawnFacingPair(6f, health: 100);
            var attacker = pair[0];
            var victim = pair[1];
            yield return WaitFrames(2);

            GivePassive(attacker, CarrierPassive);
            GivePassive(victim, LootPassive);
            yield return WaitUntil(() => PassivesCount(attacker) == 1 && PassivesCount(victim) == 1);
            AttachStealHandler(attacker, enabled: false); // authored mask None: dispatch must filter it out

            BeginCapture(victim);
            AimAt(attacker, Position(victim));
            SetFiring(attacker, true);
            yield return WaitUntil(() => DamageEventCount(victim) >= 2, 5f);
            SetFiring(attacker, false);

            Assert.GreaterOrEqual(DamageEventCount(victim), 2, "arrange: player hits landed while the mask was None");
            Assert.AreEqual(1, PassivesCount(attacker), "no steal: the authored channel mask is the off switch");
            Assert.AreEqual(1, PassivesCount(victim), "the victim keeps its passive");
        }
    }
}
