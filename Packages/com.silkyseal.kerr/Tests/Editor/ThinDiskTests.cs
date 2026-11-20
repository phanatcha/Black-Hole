using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class CircularOrbitTests
    {
        [TestCase(0.0, 6.0, 1e-12)]
        [TestCase(0.6, 3.8290, 1e-4)]
        [TestCase(0.998, 1.2369, 1e-4)] // Thorne's limiting spin
        public void IscoRadius_MatchesBardeen(double a, double expected, double tolerance) =>
            Assert.AreEqual(expected, CircularOrbit.IscoRadius(a), tolerance);

        [Test]
        public void IscoRadius_IsTheEnergyMinimum([Values(0.0, 0.6, 0.998)] double a)
        {
            double r = CircularOrbit.IscoRadius(a), e = CircularOrbit.SpecificEnergy(r, a);
            Assert.Greater(CircularOrbit.SpecificEnergy(r * 1.001, a), e);
            Assert.Greater(CircularOrbit.SpecificEnergy(r * 0.999, a), e);
        }

        [Test]
        public void Velocity_IsUnitTimelike_AndCarriesTheOrbitEnergy([Values(0.0, 0.6, 0.998)] double a,
                                                                    [Values(0.0, 1.0, 10.0)] double beyondIsco)
        {
            double r = CircularOrbit.IscoRadius(a) + beyondIsco;
            var x = EquatorialPoint(r, a, 0.7);
            var u = CircularOrbit.Velocity(x, a);
            Assert.AreEqual(-1, KerrSchild.Dot(u, u, x, a), 1e-12);
            Assert.AreEqual(CircularOrbit.SpecificEnergy(r, a), -KerrSchild.Lower(u, x, a).T, 1e-12);
        }

        [Test]
        public void Shift_IsTimeDilation_ForPhotonsWithoutAngularMomentum([Values(0.0, 0.6, 0.998)] double a)
        {
            // k_φ = 0 and unit energy at infinity leave only gravitational plus transverse Doppler: g = 1/u^t.
            double r = CircularOrbit.IscoRadius(a) + 3, sr = Math.Sqrt(r);
            double ut = (r * sr + a) / (Math.Pow(r, 0.75) * Math.Sqrt(r * sr - 3 * sr + 2 * a));
            Assert.AreEqual(1 / ut, CircularOrbit.Shift(EquatorialPoint(r, a, 2.0), 1, 0, a), 1e-12);
            if (a == 0) Assert.AreEqual(Math.Sqrt(1 - 3 / r), CircularOrbit.Shift(EquatorialPoint(r, a, 2.0), 1, 0, a), 1e-12);
        }

        // Kerr-Schild point on z = 0 at Boyer–Lindquist radius r and azimuth φ̃: x + iy = (r + ia) e^{iφ̃}.
        internal static Vector3d EquatorialPoint(double r, double a, double phi) =>
            new Vector3d(r * Math.Cos(phi) - a * Math.Sin(phi), r * Math.Sin(phi) + a * Math.Cos(phi), 0);
    }

    public class ThinDiskTests
    {
        [Test]
        public void FluxShape_VanishesAtIsco_AndIsPositiveOutside([Values(0.0, 0.6, 0.998)] double a)
        {
            double isco = CircularOrbit.IscoRadius(a);
            Assert.AreEqual(0, ThinDisk.FluxShape(isco, a), 1e-12);
            Assert.AreEqual(0, ThinDisk.FluxShape(isco * 0.9, a));
            Assert.Greater(ThinDisk.FluxShape(isco * 1.01, a), 0);
        }

        [TestCase(7.0)]
        [TestCase(10.0)]
        [TestCase(30.0)]
        public void FluxShape_MatchesSchwarzschildClosedForm(double r)
        {
            double x = Math.Sqrt(r), s3 = Math.Sqrt(3), s6 = Math.Sqrt(6);
            double bracket = x - s6 + s3 / 2 * Math.Log((x + s3) * (s6 - s3) / ((x - s3) * (s6 + s3)));
            Assert.AreEqual(bracket / (x * x * x * x * x * (x * x - 3)), ThinDisk.FluxShape(r, 0), 1e-14);
        }

        [Test]
        public void FluxShape_TendsToNewtonian_FarOut([Values(0.0, 0.9)] double a)
        {
            const double r = 1e8; // inner-edge correction is √(r_isco/r) ≈ 2e-4 here
            Assert.AreEqual(1, ThinDisk.FluxShape(r, a) * r * r * r, 1e-3);
        }

        [Test]
        public void FromAccretion_FarField_MatchesNewtonianDisk()
        {
            const double massSolar = 1e8, ratio = 0.1, r = 1e5;
            const double gm = 1.3271244e20 * massSolar, c = 2.99792458e8, sigma = 5.670374419e-8;
            var disk = ThinDisk.FromAccretion(0.6, 2 * r, massSolar, ratio);
            double rg = gm / (c * c), metres = r * rg;
            double newtonian = Math.Pow(3 * gm * disk.AccretionRate / (8 * Math.PI * sigma * metres * metres * metres), 0.25);
            Assert.AreEqual(newtonian, disk.Temperature(r), 0.01 * newtonian);
        }

        [Test]
        public void FromAccretion_UsesEddingtonLuminanceAndIscoEfficiency()
        {
            const double massSolar = 10, ratio = 0.5, c = 2.99792458e8;
            var disk = ThinDisk.FromAccretion(0, 100, massSolar, ratio);
            double eddington = 1.2572e31 * massSolar; // W, 4πGMm_p c/σ_T
            double efficiency = 1 - Math.Sqrt(8.0 / 9.0);
            Assert.AreEqual(ratio * eddington, efficiency * disk.AccretionRate * c * c, 1e-3 * ratio * eddington);
        }

        [Test]
        public void Temperature_ScalesAsMassAndRateToTheQuarter()
        {
            const double r = 10;
            double t = ThinDisk.FromAccretion(0.6, 50, 1e8, 0.1).Temperature(r);
            Assert.AreEqual(t / 2, ThinDisk.FromAccretion(0.6, 50, 16e8, 0.1).Temperature(r), 1e-9 * t);
            Assert.AreEqual(t * 2, ThinDisk.FromAccretion(0.6, 50, 1e8, 1.6).Temperature(r), 1e-9 * t);
        }

        [Test]
        public void FromPeakTemperature_PeaksAtTheGivenValue_InsideTheDisk([Values(0.0, 0.6, 0.998)] double a)
        {
            var disk = ThinDisk.FromPeakTemperature(a, 20, 4500);
            double max = 0;
            for (int i = 0; i <= 100000; i++)
                max = Math.Max(max, disk.Temperature(disk.InnerRadius + (disk.OuterRadius - disk.InnerRadius) * i / 100000.0));
            Assert.AreEqual(4500, max, 4500 * 1e-6);
            Assert.AreEqual(0, disk.Temperature(disk.InnerRadius * 0.99));
            Assert.AreEqual(0, disk.Temperature(disk.OuterRadius * 1.01));
        }
    }
}
