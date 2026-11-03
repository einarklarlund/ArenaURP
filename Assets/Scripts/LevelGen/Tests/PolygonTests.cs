using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PolygonTests
    {
        private const float Epsilon = 1e-3f;

        private static List<Vector2> Square(float x0, float z0, float size) => new()
        {
            new(x0, z0), new(x0 + size, z0), new(x0 + size, z0 + size), new(x0, z0 + size),
        };

        [Test]
        public void Subtract_IgnoresAHolesNearDuplicateCorner()
        {
            // A slope piece with two corners 2.4e-6 apart, under a float's spacing at x = 45: the edge between them
            // doubles back. Taken as a half-plane it put the whole triangle outside the hole, which was then kept.
            var hole = new List<Vector2> { new(45.1679268f, 32.1464424f), new(45.03158f, 32.23089f), new(44.99901f, 32.1783f), new(45.1679268f, 32.14644f) };
            var inside = new List<Vector2> { new(45.1679268f, 32.14644f), new(45.0679054f, 32.20839f), new(45.07605f, 32.16377f) };
            float left = 0f;
            foreach (var piece in Polygon.Subtract(inside, hole)) left += Polygon.Area(piece);
            Assert.Less(left, 1e-5f);
        }

        [Test]
        public void Area_IsPositiveForCounterClockwise()
        {
            var square = Square(0f, 0f, 10f);
            Assert.AreEqual(100f, Polygon.SignedArea(square), Epsilon);
            square.Reverse();
            Assert.AreEqual(-100f, Polygon.SignedArea(square), Epsilon);
            Assert.AreEqual(100f, Polygon.Area(square), Epsilon);
        }

        [Test]
        public void Centroid_OfTriangle_IsVertexMean()
        {
            var triangle = new List<Vector2> { new(0f, 0f), new(6f, 0f), new(0f, 3f) };
            var c = Polygon.Centroid(triangle);
            Assert.AreEqual(2f, c.x, Epsilon);
            Assert.AreEqual(1f, c.y, Epsilon);
        }

        [Test]
        public void Contains_InsideAndOutside()
        {
            var square = Square(0f, 0f, 10f);
            Assert.IsTrue(Polygon.Contains(square, 5f, 5f));
            Assert.IsFalse(Polygon.Contains(square, 11f, 5f));
            Assert.IsFalse(Polygon.Contains(square, 5f, -0.1f));
        }

        [Test]
        public void ClipHalfPlane_KeepsTheInnerSide()
        {
            var clipped = Polygon.ClipHalfPlane(Square(0f, 0f, 10f), new Vector2(1f, 0f), 4f);
            Assert.AreEqual(40f, Polygon.Area(clipped), Epsilon);
            Assert.IsTrue(Polygon.IsConvex(clipped));
            Assert.AreEqual(0, Polygon.ClipHalfPlane(Square(0f, 0f, 10f), new Vector2(1f, 0f), -1f).Count);
        }

        [Test]
        public void ClipConvex_GivesTheIntersection()
        {
            var overlap = Polygon.ClipConvex(Square(0f, 0f, 10f), Square(6f, 7f, 10f));
            Assert.AreEqual(4f * 3f, Polygon.Area(overlap), Epsilon);
        }

        [Test]
        public void Inset_MovesEveryEdgeInward()
        {
            Assert.AreEqual(64f, Polygon.Area(Polygon.Inset(Square(0f, 0f, 10f), 1f)), Epsilon);
            Assert.AreEqual(0, Polygon.Inset(Square(0f, 0f, 10f), 5.5f).Count);

            // An angled triangle (3-4-5) has inradius 1: an inset of 0.5 leaves a similar triangle at half scale.
            var triangle = new List<Vector2> { new(0f, 0f), new(4f, 0f), new(0f, 3f) };
            Rotate(triangle, 33f);
            var inset = Polygon.Inset(triangle, 0.5f);
            Assert.AreEqual(6f * 0.25f, Polygon.Area(inset), Epsilon);
            Assert.IsTrue(Polygon.IsConvex(inset));
        }

        [Test]
        public void SplitByChord_GivesTwoConvexPiecesThatAddUp([Values(0f, 17f, 45f, 100f, 163f)] float degrees)
        {
            var square = Square(0f, 0f, 10f);
            Rotate(square, 12f);
            float r = degrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            var point = Polygon.Centroid(square) + new Vector2(0.7f, -1.1f);
            Polygon.SplitByChord(square, point, direction, out var left, out var right);

            Assert.AreEqual(100f, Polygon.Area(left) + Polygon.Area(right), Epsilon);
            Assert.IsTrue(Polygon.IsConvex(left));
            Assert.IsTrue(Polygon.IsConvex(right));
            var normalLeft = new Vector2(-direction.y, direction.x);
            Assert.Greater(Vector2.Dot(Polygon.Centroid(left) - point, normalLeft), 0f);
            Assert.Less(Vector2.Dot(Polygon.Centroid(right) - point, normalLeft), 0f);
        }

        [Test]
        public void SplitByChord_ThatMisses_LeavesOneSideEmpty()
        {
            Polygon.SplitByChord(Square(0f, 0f, 10f), new Vector2(0f, 20f), Vector2.right, out var left, out var right);
            Assert.AreEqual(0, left.Count);
            Assert.AreEqual(100f, Polygon.Area(right), Epsilon);
        }

        [Test]
        public void IsConvex_RejectsReflexAndClockwise()
        {
            var l = new List<Vector2> { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(5f, 5f), new(5f, 10f), new(0f, 10f) };
            Assert.IsFalse(Polygon.IsConvex(l));
            var square = Square(0f, 0f, 10f);
            Assert.IsTrue(Polygon.IsConvex(square));
            square.Reverse();
            Assert.IsFalse(Polygon.IsConvex(square));
        }

        internal static void Rotate(List<Vector2> polygon, float degrees)
        {
            var c = Polygon.Centroid(polygon);
            float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            for (int i = 0; i < polygon.Count; i++)
            {
                var d = polygon[i] - c;
                polygon[i] = c + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }
        }
    }
}
