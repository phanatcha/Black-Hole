using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class FootprintTests
    {
        static readonly Vector3d Centre = new Vector3d(0, 0, 1);

        [Test]
        public void SmallerOneSidedStep_IsTaken()
        {
            // To the right the neighbour's ray crossed a lensing fold and looks far away; the left step is the honest one.
            var left = new Vector3d(-0.01, 0, 1).Normalized;
            var right = new Vector3d(0.5, 0, 1).Normalized;
            var down = new Vector3d(0, -0.02, 1).Normalized;
            var up = new Vector3d(0, 0.02, 1).Normalized;

            Footprint.Estimate(Centre, left, right, down, up, 0.001, out var jx, out var jy);

            Assert.AreEqual(0.01, jx.Length, 1e-4);
            Assert.AreEqual(0, Vector3d.Dot(jx, Centre), 1e-12);
            Assert.AreEqual(0.02, jy.Length, 2e-4);
        }

        [Test]
        public void NoEscapedNeighbour_FallsBackToThePixelAngle()
        {
            var down = new Vector3d(0, -0.02, 1).Normalized;
            Footprint.Estimate(Centre, default, default, down, default, 0.001, out var jx, out var jy);

            Assert.AreEqual(0.001, jx.Length, 1e-9);
            Assert.AreEqual(0, Vector3d.Dot(jx, jy), 1e-12);
            Assert.AreEqual(0, Vector3d.Dot(jx, Centre), 1e-12);

            Footprint.Estimate(Centre, default, default, default, default, 0.001, out jx, out jy);

            Assert.AreEqual(0.001, jx.Length, 1e-9);
            Assert.AreEqual(0.001, jy.Length, 1e-9);
            Assert.AreEqual(0, Vector3d.Dot(jx, jy), 1e-12);
            Assert.AreEqual(0, Vector3d.Dot(jy, Centre), 1e-12);
        }
    }
}
