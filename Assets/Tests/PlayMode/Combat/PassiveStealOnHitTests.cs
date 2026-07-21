using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Steal-a-passive-ON-HIT in the shared-registry model (ADR 0009). The attacker owns a STEAL KIND whose
    /// <c>PassiveStealController</c> (<c>IHitMessageHandler</c>) self-clones onto the projectiles it spawns (via
    /// SpawnContributionReceiver), so by hit time it is an ordinary handler on the source and <c>HitMessage.Dispatch</c>
    /// never resolves the ability system. On a player hit it MOVES one passive KIND reference from the victim's
    /// <c>PlayerPassiveLoadout</c> to the attacker's — a pure SyncList transfer, no despawn/spawn. Exercises the two
    /// things the shape must prove: the SPAWN-TIME CONTRIBUTION path (the handler rides the owner's projectile with no
    /// serialized hit-site list) and the CHANNEL FILTER (EnabledHitChannels ∩ current channel). Networked change → PlayMode test.
    /// </summary>
    public class PassiveStealOnHitTests : CombatPlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Player_hit_moves_a_passive_kind_reference_from_the_victim_to_the_attacker()
        {
            var pair = SpawnFacingPair(6f, health: 100);
            var attacker = pair[0];
            var victim = pair[1];
            yield return WaitFrames(2);

            AttachStealHandler(attacker);          // attacker owns the steal KIND (its bullets steal on hit)
            GivePassive(victim, "TestFireRate");   // the loot: its 0.5 fire-rate row identifies the moved kind
            yield return WaitUntil(() => PassivesCount(attacker) == 1 && PassivesCount(victim) == 1);
            Assert.AreEqual(1, PassivesCount(attacker), "arrange: attacker holds the steal KIND");
            Assert.AreEqual(1, PassivesCount(victim), "arrange: victim holds the loot KIND");
            // Assert on the loot's IDENTIFIABLE row, never the raw count — other systems contribute their own rows.
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(victim, 0.5f), "arrange: the loot's fire-rate buff folded on the victim");

            AimAt(attacker, Position(victim));
            SetFiring(attacker, true);
            yield return WaitUntil(() => PassivesCount(attacker) == 2, 5f);
            SetFiring(attacker, false);

            // The authoritative steal is the loadout REFERENCE move: the kind leaves the victim and joins the attacker.
            Assert.AreEqual(2, PassivesCount(attacker), "the stolen KIND ref was moved into the attacker's loadout");
            yield return WaitUntil(() => PassivesCount(victim) == 0, 2f);
            Assert.AreEqual(0, PassivesCount(victim), "the stolen KIND ref left the victim's loadout");

            // The moved kind's standing weapon buff reaches the attacker's pawn (the loadout's mid-life grant applies it).
            yield return WaitUntil(() => CountWeaponModifiersWithFireRate(attacker, 0.5f) == 1, 2f);
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(attacker, 0.5f),
                "the stolen kind's weapon buff now folds on the attacker's pawn");
            // NOTE: the victim's already-folded buff LINGERS until it respawns — removal/teardown cleanup is deferred
            // (ADR 0009), so this test does NOT assert the victim's fold drops; the ref move above IS the steal.
        }

        [UnityTest]
        public IEnumerator Authored_channel_mask_of_none_silences_the_steal()
        {
            var pair = SpawnFacingPair(6f, health: 100);
            var attacker = pair[0];
            var victim = pair[1];
            yield return WaitFrames(2);

            AttachStealHandler(attacker, enabled: false); // grants the "TestStealSilent" KIND: channel mask None
            GivePassive(victim, "TestFireRate");
            yield return WaitUntil(() => PassivesCount(attacker) == 1 && PassivesCount(victim) == 1);

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
