using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class ObserverFrameTests
    {
        const double Spin = 0.9;
        // Normalisation and orthogonality are algebra on O(1) numbers: double rounding only.
        const double AlgebraTolerance = 1e-12;
        // At r = 1e6 the metric differs from flat by f ~ 2e-6, which bounds the angle error.
        const double FarFieldAngleTolerance = 1e-5;

        static readonly Vector3d VelocityDirection = new Vector3d(0.3, 0.5, -0.2);

        // Generic, ergosphere (r₊ = 1.436 < r < 2 at the equator), and on the spin axis.
        static readonly object[] Positions =
        {
            new object[] { 12.0, 3.0, 2.0 },
            new object[] { 1.9, 0.0, 0.05 },
            new object[] { 0.0, 0.0, 6.0 },
        };

        static readonly object[] PositionsAndSpeeds =
        {
            new object[] { 12.0, 3.0, 2.0, 0.0 }, new object[] { 12.0, 3.0, 2.0, 0.5 },
            new object[] { 1.9, 0.0, 0.05, 0.0 }, new object[] { 1.9, 0.0, 0.05, 0.5 },
            new object[] { 0.0, 0.0, 6.0, 0.0 }, new object[] { 0.0, 0.0, 6.0, 0.5 },
        };

        [TestCaseSource(nameof(Positions))]
        public void Zamo_IsUnitTimelike_WithZeroAngularMomentum(double x, double y, double z)
        {
            var pos = new Vector3d(x, y, z);
            var u = ObserverFrame.Zamo(pos, Spin);
            Assert.AreEqual(-1.0, KerrSchild.Dot(u, u, pos, Spin), AlgebraTolerance);
            Assert.Greater(u.T, 0.0, "future-directed");

            if (x * x + y * y > 0)
            {
                var rotation = new Vector4d(0, -y, x, 0);
                Assert.AreEqual(0.0, KerrSchild.Dot(u, rotation, pos, Spin), AlgebraTolerance);
            }
        }

        [TestCaseSource(nameof(PositionsAndSpeeds))]
        public void Build_TetradIsOrthonormal(double x, double y, double z, double beta)
        {
            var pos = new Vector3d(x, y, z);
            bool onAxis = x * x + y * y == 0;
            var forward = (-pos).Normalized;
            var up = onAxis ? new Vector3d(1, 0, 0) : new Vector3d(0, 0, 1);
            var right = new Vector3d(0, 1, 0);

            var f = ObserverFrame.Build(pos, Spin, forward, up, right, VelocityDirection, beta);

            var basis = new[] { f.U, f.Right, f.Up, f.Forward };
            for (int i = 0; i < 4; i++)
            for (int j = i; j < 4; j++)
            {
                double expected = i != j ? 0 : i == 0 ? -1 : 1;
                Assert.AreEqual(expected, KerrSchild.Dot(basis[i], basis[j], pos, Spin), AlgebraTolerance, $"pair {i},{j}");
            }
        }

        [Test]
        public void Aberration_FarFromHole_MatchesSpecialRelativity()
        {
            const double a = 0.6, beta = 0.5;
            var pos = new Vector3d(1e6, 0, 0);
            var forward = new Vector3d(0, 1, 0);
            var f = ObserverFrame.Build(pos, a, forward, new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), forward, beta);

            // tanHalfFovY = √3, ndc (1, 0): the pixel looks 60° off forward in the moving frame.
            // cos θ' = (cos θ + β)/(1 + β cos θ) with θ' = 60°, β = 0.5 gives θ = 90° in the rest frame.
            var result = RayTracer.TracePixel(f, 1, 0, Math.Sqrt(3), 1, TraceSettings.Reference(a));

            double angle = Math.Acos(Vector3d.Dot(result.SkyDirection, forward));
            Assert.AreEqual(Math.PI / 2, angle, FarFieldAngleTolerance);
        }

        [Test]
        public void Zamo_InsideHorizon_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ObserverFrame.Zamo(new Vector3d(1.5, 0, 0), 0.6));
        }
    }
}
