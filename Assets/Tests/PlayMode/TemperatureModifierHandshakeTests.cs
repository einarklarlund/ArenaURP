using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Narrow PlayMode slice of the Temperature -> Movement modifier handshake.
    ///
    /// The PURE CONTRACT (a TemperatureProcessor-surfaced MovementModifier shape that
    /// MovementProcessor consumes) is already pinned by the EditMode test
    /// TemperatureMovementIntegrationTests. This test only covers what EditMode can't:
    /// that the live `PawnTemperature.Update` MonoBehaviour actually mutates the live
    /// `PawnMovement.Modifiers` list at runtime via its serialized reference.
    ///
    /// Risk caught: someone unwires `PawnTemperature.movement` in the inspector, or
    /// rewrites the Update path so it stops calling `.Add/.Remove`, or the Remove-Add
    /// idiom drifts in a way that leaks modifiers across ticks. Each of those breaks
    /// the in-game temperature effect silently, since the processors themselves still
    /// pass their own EditMode tests.
    /// </summary>
    public sealed class TemperatureModifierHandshakeTests : NetworkedPlayModeTestFixture
    {
        const string TestPawnTemperaturePrefabPath
            = "Assets/Tests/PlayMode/TestPawnTemperature.prefab";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestPawnGlue.unity";

        [UnityTest]
        public IEnumerator PawnTemperature_pushes_modifier_into_PawnMovement_Modifiers_each_tick()
        {
            var pawn = SpawnServerPrefab(TestPawnTemperaturePrefabPath, new Vector3(0, 2f, 0));
            yield return new WaitForSeconds(0.2f);

            var modifiers = GetMovementModifiersList(pawn);

            Assert.Greater(
                modifiers.Count, 0,
                "After a few ticks PawnTemperature.Update should have pushed at least one " +
                "modifier into PawnMovement.Modifiers via the serialized reference. " +
                "If empty, either the reference is unwired or Update isn't being called."
            );

            // Bounded-growth check: run for many more ticks and confirm the list stays
            // small. The production code uses a Remove-then-Add idiom; if Remove ever
            // misses, this asserts the leak. Cap is forgiving — knockback/etc. could
            // legitimately push a modifier or two — so we're only catching unbounded
            // growth, not exact counts.
            yield return new WaitForSeconds(1.5f);

            Assert.LessOrEqual(
                modifiers.Count, 4,
                $"PawnMovement.Modifiers grew to {modifiers.Count} entries after sustained " +
                "ticking. PawnTemperature's Remove-then-Add idiom should keep it bounded. " +
                "An unbounded list is both a memory leak and a behavioural compounding bug."
            );
        }

        /// <summary>
        /// Reflection access to <c>PawnMovement.Modifiers</c> on the Movement child.
        /// The test asmdef can't reference PawnMovement directly (it lives in
        /// Assembly-CSharp), so we resolve by name and field.
        /// </summary>
        static System.Collections.IList GetMovementModifiersList(GameObject pawn)
        {
            var movement = pawn.transform.Find("Movement").GetComponent("PawnMovement");
            Assert.IsNotNull(movement, "TestPawnTemperature should have a Movement child with PawnMovement");
            var field = movement.GetType().GetField(
                "Modifiers", BindingFlags.Public | BindingFlags.Instance);
            return (System.Collections.IList)field.GetValue(movement);
        }
    }
}
