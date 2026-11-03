using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class RoomGraphTests
    {
        /// <summary>Two plots side by side, the right one 3 higher, meeting along x = 25.</summary>
        private static LevelDescription TwoPlots(TransitionType type)
        {
            var f = new Fixture().Plot(0, 0f, 0f, 25f, 50f).Plot(1, 25f, 0f, 50f, 50f, 3f);
            f.Level.Transitions.Add(new TransitionDescription
            {
                Id = 0, Type = type, PlotA = 0, PlotB = 1, From = new Vector2(25f, 0f), To = new Vector2(25f, 50f),
            });
            f.Level.RoomLinks.AddRange(SiteStage.Links(f.Level));
            return f.Level;
        }

        [Test]
        public void RoomAt_FindsThePlotUnderAPosition()
        {
            var rooms = new RoomGraph(TwoPlots(TransitionType.Flat));
            Assert.AreEqual(0, rooms.RoomAt(new Vector3(10f, 5f, 10f)));
            Assert.AreEqual(1, rooms.RoomAt(new Vector3(40f, -2f, 45f)), "height is ignored");
            int seam = rooms.RoomAt(new Vector3(25f, 0f, 20f));
            Assert.IsTrue(seam == 0 || seam == 1, "a point on the seam is in one of the two");
            Assert.AreEqual(0, rooms.RoomAt(new Vector3(-0.5f, 0f, 10f)), "just outside counts as the nearest plot");
            Assert.AreEqual(-1, rooms.RoomAt(new Vector3(-5f, 0f, 10f)), "well outside is no room");
        }

        [Test]
        public void TwoWayTransition_IsAdjacentBothWays()
        {
            var rooms = new RoomGraph(TwoPlots(TransitionType.Ramp));
            CollectionAssert.AreEqual(new[] { 1 }, rooms.Adjacent(0));
            CollectionAssert.AreEqual(new[] { 0 }, rooms.Adjacent(1));
        }

        [Test]
        public void Cliff_IsAdjacentOnlyDownward()
        {
            var rooms = new RoomGraph(TwoPlots(TransitionType.Cliff));
            CollectionAssert.IsEmpty(rooms.Adjacent(0), "can't climb the cliff");
            CollectionAssert.AreEqual(new[] { 0 }, rooms.Adjacent(1), "can drop off it");
            CollectionAssert.AreEqual(new[] { 1 }, rooms.Linked(0));
        }

        [Test]
        public void Blocked_IsLinkedButNotAdjacent()
        {
            var rooms = new RoomGraph(TwoPlots(TransitionType.Blocked));
            CollectionAssert.IsEmpty(rooms.Adjacent(0));
            CollectionAssert.IsEmpty(rooms.Adjacent(1));
            CollectionAssert.AreEqual(new[] { 0 }, rooms.Linked(1));
        }

        [Test]
        public void UnknownRoom_HasNoNeighboursOrSites()
        {
            var rooms = new RoomGraph(TwoPlots(TransitionType.Flat));
            CollectionAssert.IsEmpty(rooms.Adjacent(-1));
            CollectionAssert.IsEmpty(rooms.ChestSitesIn(7));
            CollectionAssert.IsEmpty(rooms.PickupLinesIn(-1));
        }
    }
}
