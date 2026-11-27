using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class SceneBvhTests
    {
        // A segment straight through a triangle's plane at a known point.
        [Test]
        public void Segment_HitsATriangle_WhereItCrosses()
        {
            var bvh = new SceneBvh(new[] { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0) }, new[] { 0, 1, 2 });

            Assert.IsTrue(bvh.Intersect(new Vector3d(1, 2, 5), new Vector3d(1, 2, -5), out var hit));
            Assert.AreEqual(0, hit.Triangle);
            Assert.AreEqual(0.5, hit.T, 1e-12);
            Assert.AreEqual(0.25, hit.U, 1e-12, "weight of the second vertex");
            Assert.AreEqual(0.5, hit.V, 1e-12, "weight of the third vertex");
        }

        [Test]
        public void Segment_EndingShortOfATriangle_Misses()
        {
            var bvh = new SceneBvh(new[] { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0) }, new[] { 0, 1, 2 });

            Assert.IsFalse(bvh.Intersect(new Vector3d(1, 1, 5), new Vector3d(1, 1, 0.1), out _));
            Assert.IsFalse(bvh.Intersect(new Vector3d(3, 3, 5), new Vector3d(3, 3, -5), out _), "beside the triangle");
        }

        // The tree must find the same nearest triangle as checking every one of them.
        [Test]
        public void Intersect_MatchesBruteForce()
        {
            var random = new Random(7);
            const int count = 600;
            var vertices = new Vector3d[3 * count];
            var indices = new int[3 * count];
            for (int i = 0; i < count; i++)
            {
                var centre = RandomPoint(random, 20);
                for (int k = 0; k < 3; k++)
                {
                    vertices[3 * i + k] = centre + RandomPoint(random, 1.5);
                    indices[3 * i + k] = 3 * i + k;
                }
            }
            var bvh = new SceneBvh(vertices, indices);

            int hits = 0;
            for (int s = 0; s < 4000; s++)
            {
                var from = RandomPoint(random, 30);
                var to = RandomPoint(random, 30);
                bool found = bvh.Intersect(from, to, out var hit);
                bool expected = BruteForce(vertices, indices, from, to, out double t, out int triangle);
                Assert.AreEqual(expected, found, $"segment {s}");
                if (!found) continue;
                hits++;
                Assert.AreEqual(t, hit.T, 1e-12, $"segment {s}");
                Assert.AreEqual(triangle, hit.Triangle, $"segment {s}");
            }
            Assert.Greater(hits, 400, "enough segments hit something to test the order");
        }

        static bool BruteForce(Vector3d[] vertices, int[] indices, Vector3d from, Vector3d to, out double nearest, out int triangle)
        {
            nearest = 1;
            triangle = -1;
            for (int i = 0; i < indices.Length / 3; i++)
            {
                var one = new SceneBvh(new[] { vertices[indices[3 * i]], vertices[indices[3 * i + 1]], vertices[indices[3 * i + 2]] },
                                       new[] { 0, 1, 2 });
                if (one.Intersect(from, to, out var hit) && hit.T < nearest)
                {
                    nearest = hit.T;
                    triangle = i;
                }
            }
            return triangle >= 0;
        }

        static Vector3d RandomPoint(Random random, double halfSize) =>
            new Vector3d(halfSize * (2 * random.NextDouble() - 1), halfSize * (2 * random.NextDouble() - 1), halfSize * (2 * random.NextDouble() - 1));
    }
}
