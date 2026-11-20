using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class KerrSchildTests
    {
        // Round-trip and identity checks are exact algebra, so only double rounding is allowed.
        const double AlgebraTolerance = 1e-12;
        // Central difference with h = 1e-6 has O(h^2) truncation (~1e-12) and ~1e-10 rounding,
        // so 1e-6 relative catches any wrong analytic term without flaking.
        const double FiniteDifferenceStep = 1e-6;
        const double GradientRelativeTolerance = 1e-6;

        static readonly Vector4d SampleVector = new Vector4d(0.3, -1.2, 0.7, 2.1);

        // Ergosphere, spin axis, far field, plus two generic points.
        static readonly object[] Points =
        {
            new object[] { 1.9, 0.0, 0.01, 0.9 },
            new object[] { 0.0, 0.0, 4.0, 0.6 },
            new object[] { 30.0, -12.0, 8.0, 0.6 },
            new object[] { 5.0, 0.0, 0.0, 0.6 },
            new object[] { 3.0, 2.0, -1.5, 0.999 },
        };

        [Test]
        public void Radius_OnSpinAxis_EqualsAbsZ()
        {
            Assert.AreEqual(7.0, KerrSchild.Radius(new Vector3d(0, 0, 7), 0.6), AlgebraTolerance);
            Assert.AreEqual(7.0, KerrSchild.Radius(new Vector3d(0, 0, -7), 0.6), AlgebraTolerance);
        }

        [Test]
        public void Radius_InEquatorialPlane_IsSqrtRhoSquaredMinusASquared()
        {
            Assert.AreEqual(Math.Sqrt(25.0 - 0.36), KerrSchild.Radius(new Vector3d(5, 0, 0), 0.6), AlgebraTolerance);
        }

        [Test]
        public void HorizonRadius_KnownValues()
        {
            Assert.AreEqual(2.0, KerrSchild.HorizonRadius(0.0), AlgebraTolerance);
            Assert.AreEqual(1.8, KerrSchild.HorizonRadius(0.6), AlgebraTolerance);
        }

        [TestCaseSource(nameof(Points))]
        public void Raise_InvertsLower(double x, double y, double z, double a)
        {
            var pos = new Vector3d(x, y, z);
            var back = KerrSchild.Raise(KerrSchild.Lower(SampleVector, pos, a), pos, a);
            AssertClose(SampleVector, back, AlgebraTolerance);
        }

        [TestCaseSource(nameof(Points))]
        public void Dot_EqualsLoweredContraction(double x, double y, double z, double a)
        {
            var pos = new Vector3d(x, y, z);
            var u = new Vector4d(1.1, 0.2, -0.4, 0.3);
            var lowered = KerrSchild.Lower(u, pos, a);
            double expected = lowered.T * SampleVector.T + lowered.X * SampleVector.X
                            + lowered.Y * SampleVector.Y + lowered.Z * SampleVector.Z;
            Assert.AreEqual(expected, KerrSchild.Dot(u, SampleVector, pos, a), AlgebraTolerance);
        }

        [TestCaseSource(nameof(Points))]
        public void Hamiltonian_IsHalfContractionWithRaised(double x, double y, double z, double a)
        {
            var pos = new Vector3d(x, y, z);
            var raised = KerrSchild.Raise(SampleVector, pos, a);
            double expected = 0.5 * (raised.T * SampleVector.T + raised.X * SampleVector.X
                                   + raised.Y * SampleVector.Y + raised.Z * SampleVector.Z);
            Assert.AreEqual(expected, KerrSchild.Hamiltonian(pos, SampleVector, a), AlgebraTolerance);
        }

        [TestCaseSource(nameof(Points))]
        public void HamiltonianGradient_MatchesCentralFiniteDifference(double x, double y, double z, double a)
        {
            var pos = new Vector3d(x, y, z);
            KerrSchild.HamiltonianGradient(pos, SampleVector, a, out var analytic);

            var numeric = new Vector3d(
                CentralDifference(pos, new Vector3d(FiniteDifferenceStep, 0, 0), a),
                CentralDifference(pos, new Vector3d(0, FiniteDifferenceStep, 0), a),
                CentralDifference(pos, new Vector3d(0, 0, FiniteDifferenceStep), a));

            double relativeError = (numeric - analytic).Length / analytic.Length;
            Assert.Less(relativeError, GradientRelativeTolerance, $"analytic {analytic}, numeric {numeric}");
        }

        static double CentralDifference(Vector3d pos, Vector3d step, double a) =>
            (KerrSchild.Hamiltonian(pos + step, SampleVector, a) - KerrSchild.Hamiltonian(pos - step, SampleVector, a))
            / (2 * step.Length);

        static void AssertClose(Vector4d expected, Vector4d actual, double tolerance)
        {
            Assert.AreEqual(expected.T, actual.T, tolerance, "t");
            Assert.AreEqual(expected.X, actual.X, tolerance, "x");
            Assert.AreEqual(expected.Y, actual.Y, tolerance, "y");
            Assert.AreEqual(expected.Z, actual.Z, tolerance, "z");
        }
    }
}
