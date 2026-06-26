using NUnit.Framework;
using Arena.Ability;

namespace Arena.Tests.Ability
{
    /// <summary>
    /// EditMode unit tests for the pure active-ability inventory math (cycling, clamping, use/remove).
    /// No scene, no FishNet — these are the branchy edge cases that would otherwise need PlayMode.
    /// </summary>
    [TestFixture]
    public class AbilityInventoryLogicTests
    {
        // ---- CycleIndex ----

        [Test]
        public void CycleIndex_Forward_Advances()
        {
            Assert.AreEqual(1, AbilityInventoryLogic.CycleIndex(0, 5, +1));
        }

        [Test]
        public void CycleIndex_Forward_WrapsPastEnd()
        {
            Assert.AreEqual(0, AbilityInventoryLogic.CycleIndex(4, 5, +1));
        }

        [Test]
        public void CycleIndex_Backward_WrapsPastStart()
        {
            Assert.AreEqual(4, AbilityInventoryLogic.CycleIndex(0, 5, -1));
        }

        [Test]
        public void CycleIndex_SingleSlot_StaysAtZero()
        {
            Assert.AreEqual(0, AbilityInventoryLogic.CycleIndex(0, 1, +1));
            Assert.AreEqual(0, AbilityInventoryLogic.CycleIndex(0, 1, -1));
        }

        [Test]
        public void CycleIndex_Empty_ReturnsZero()
        {
            Assert.AreEqual(0, AbilityInventoryLogic.CycleIndex(0, 0, +1));
            Assert.AreEqual(0, AbilityInventoryLogic.CycleIndex(3, 0, -1));
        }

        // ---- ClampSelection ----

        [Test]
        public void ClampSelection_ClampsToLastWhenCountShrinks()
        {
            // Selected slot 4, but only 3 slots remain after a removal -> clamp to 2.
            Assert.AreEqual(2, AbilityInventoryLogic.ClampSelection(4, 3));
        }

        [Test]
        public void ClampSelection_Empty_ReturnsZero()
        {
            Assert.AreEqual(0, AbilityInventoryLogic.ClampSelection(2, 0));
        }

        [Test]
        public void ClampSelection_InRange_Unchanged()
        {
            Assert.AreEqual(1, AbilityInventoryLogic.ClampSelection(1, 3));
        }

        // ---- UseSlot ----

        [Test]
        public void UseSlot_Decrements_AndKeepsSlotWhenUsesRemain()
        {
            var r = AbilityInventoryLogic.UseSlot(3);
            Assert.AreEqual(2, r.UsesRemaining);
            Assert.IsFalse(r.Removed);
        }

        [Test]
        public void UseSlot_LastUse_RemovesSlot()
        {
            var r = AbilityInventoryLogic.UseSlot(1);
            Assert.AreEqual(0, r.UsesRemaining);
            Assert.IsTrue(r.Removed);
        }
    }
}
