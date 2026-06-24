using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// The "Pawn glue" integration test from the integration-testing skill.
    ///
    /// What it covers (what EditMode tests structurally cannot):
    ///   PawnInput -> PawnMovement.Update -> MovementProcessor -> CharacterController.Move
    ///   ...running on a server-spawned NetworkObject, ticking the real engine loop,
    ///   colliding against a real CharacterController + floor.
    ///
    /// Anything testable as pure-data (e.g. modifier folding) belongs in the EditMode
    /// MovementProcessor tests, not here.
    ///
    /// Wall-clock waits used deliberately: PlayMode tests run at very high FPS
    /// (~500), so frame-stepping gives nanoseconds of simulated time. Movement
    /// + grounding genuinely need wall-clock time to settle.
    /// </summary>
    public sealed class PawnMovementGlueTests : NetworkedPlayModeTestFixture
    {
        const string TestPawnPrefabPath = "Assets/Tests/PlayMode/TestPawn.prefab";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestPawnGlue.unity";

        [UnityTest]
        public IEnumerator Strafe_input_moves_pawn_via_CharacterController()
        {
            var pawn = SpawnServerPrefab(TestPawnPrefabPath, new Vector3(0, 2f, 0));

            // Let the network spawn settle and the pawn land + stabilise on the floor.
            yield return new WaitForSeconds(0.5f);
            var startPos = pawn.transform.position;

            // Move.x is the horizontal/strafe axis (Move.y would be forward).
            SetFakePawnInput(pawn, new Vector2(1f, 0f));
            yield return new WaitForSeconds(1.0f);

            var delta = pawn.transform.position - startPos;
            Assert.Greater(
                delta.x, 1.0f,
                $"Strafe input should move the pawn on +X; actually moved {delta}"
            );
        }

        [UnityTest]
        public IEnumerator Forward_input_moves_pawn_via_CharacterController()
        {
            var pawn = SpawnServerPrefab(TestPawnPrefabPath, new Vector3(0, 2f, 0));

            yield return new WaitForSeconds(0.5f);
            var startPos = pawn.transform.position;

            // Move.y is the forward axis (in local space; identity rotation -> +Z world).
            SetFakePawnInput(pawn, new Vector2(0f, 1f));
            yield return new WaitForSeconds(1.0f);

            var delta = pawn.transform.position - startPos;
            Assert.Greater(
                delta.z, 1.0f,
                $"Forward input should move the pawn on +Z; actually moved {delta}"
            );
        }
    }
}
