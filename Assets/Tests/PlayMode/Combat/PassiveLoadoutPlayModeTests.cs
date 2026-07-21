using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// The two shared-registry passive properties that ownership-on-the-player buys (ADR 0009): PERSISTENCE — the
    /// loadout lives on the NetworkPlayer, so it survives the pawn's death and re-applies to a fresh pawn at spawn —
    /// and FOREIGN READ — a peer that does not own a player can resolve that player's owned KIND references to their
    /// PassiveInfo identity (the enemy-loadout HUD path). Networked change → PlayMode test.
    /// </summary>
    public sealed class PassiveLoadoutPlayModeTests : CombatPlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Loadout_survives_pawn_death_and_re_applies_to_the_respawned_pawn()
        {
            var pawn1 = SpawnCombatant(new Vector3(0f, 1f, 0f), Vector3.forward);
            yield return WaitFrames(2);
            var player = PlayerObject(pawn1);

            GivePassive(pawn1, "TestFireRate"); // WeaponPassive(FireRateMultiplier=0.5), applied to the current pawn
            yield return WaitUntil(() => CountWeaponModifiersWithFireRate(pawn1, 0.5f) == 1);
            Assert.AreEqual(1, PassivesCount(pawn1), "arrange: the player owns one passive KIND");
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(pawn1, 0.5f), "arrange: the passive folded onto the first pawn");

            // Death, then respawn: despawn the pawn (the player and its loadout live on), spawn a fresh pawn for it.
            DespawnPawn(pawn1);
            yield return WaitFrames(2);
            var pawn2 = SpawnPawnFor(player, new Vector3(2f, 1f, 0f));
            yield return WaitUntil(() => CountWeaponModifiersWithFireRate(pawn2, 0.5f) == 1);

            Assert.AreEqual(1, PassivesCount(pawn2), "the loadout survived death — ownership is on the persistent player");
            Assert.AreEqual(1, CountWeaponModifiersWithFireRate(pawn2, 0.5f),
                "the persistent loadout re-applies to the fresh pawn at spawn (per-game persistence)");
        }

        [UnityTest]
        public IEnumerator A_foreign_reader_resolves_a_players_owned_kind_to_its_identity()
        {
            var pawn = SpawnCombatant(new Vector3(0f, 1f, 0f), Vector3.forward);
            yield return WaitFrames(2);
            var player = PlayerObject(pawn);

            GivePassive(pawn, "TestFireRate"); // its KIND's PassiveInfo.DisplayName is "Test Fire Rate"
            yield return WaitUntil(() => PassivesCount(pawn) == 1);
            Assert.AreEqual(1, PassivesCount(pawn), "arrange: the player owns one passive KIND");

            // Reading the player's loadout from here (not the owning player) resolves the shared registry-child
            // reference to its identity — exactly what an enemy-loadout HUD does.
            Assert.AreEqual("Test Fire Rate", ForeignReadPassiveName(player),
                "a foreign reader resolves the owned KIND reference to its PassiveInfo display name");
        }
    }
}
