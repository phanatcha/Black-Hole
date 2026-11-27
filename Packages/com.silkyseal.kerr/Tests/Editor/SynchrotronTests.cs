using System;
using NUnit.Framework;
using static SilkySeal.Kerr.Synchrotron;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// The emissivity fits checked against what they approximate: the single-electron synchrotron spectrum integrated
    /// over the electron distribution, per steradian, √3 e³ B sinθ F(ν/ν_c) / (4π m c²) per electron.
    /// </summary>
    public class SynchrotronTests
    {
        const double Gauss = 20, SinTheta = 0.8, Density = 1e6;

        [Test]
        public void F_MatchesItsLimitsAndTabulatedValue()
        {
            // Small x: 4π / (√3 Γ(1/3)) (x/2)^(1/3); large x: √(πx/2) e^−x (1 + 55 / 72x), next term ~4e-4 at x = 50.
            Assert.AreEqual(2.14953 * Math.Cbrt(1e-6), F(1e-6), 1e-3 * F(1e-6));
            Assert.AreEqual(0.65142, F(1), 1e-4);
            double x = 50;
            Assert.AreEqual(Math.Sqrt(Math.PI * x / 2) * Math.Exp(-x) * (1 + 55 / (72 * x)), F(x), 1e-3 * F(x));
        }

        [Test]
        public void G_MatchesItsLimits()
        {
            // x K_2/3(x): 2^(−1/3) Γ(2/3) x^(1/3) for small x, √(πx/2) e^−x (1 + 7 / 72x) for large x.
            Assert.AreEqual(Math.Pow(2, -1.0 / 3) * 1.3541179394264 * Math.Cbrt(1e-6), G(1e-6), 1e-6 * G(1e-6));
            double x = 50;
            Assert.AreEqual(Math.Sqrt(Math.PI * x / 2) * Math.Exp(-x) * (1 + 7 / (72 * x)), G(x), 1e-3 * G(x));
        }

        // Dexter (2016) quotes the fits within 1 % of their integrals; 2 % leaves room for this quadrature.
        [TestCase(1.0)]
        [TestCase(10.0)]
        [TestCase(100.0)]
        [TestCase(1000.0)]
        public void Thermal_MatchesMaxwellJuttnerIntegral(double x)
        {
            const double thetaE = 10;
            double nuC = 1.5 * GyroFrequency(Gauss) * SinTheta * thetaE * thetaE, nu = x * nuC;
            Thermal(Density, thetaE, Gauss, SinTheta, nu, out double jI, out double jQ);

            // Ultra-relativistic Maxwell–Jüttner, n γ² e^(−γ/θ) / (2θ³): the K₂(1/θ) → 2θ² limit the fits assume.
            Func<double, double> electrons = g => Density * g * g * Math.Exp(-g / thetaE) / (2 * thetaE * thetaE * thetaE);
            double expectedI = OverElectrons(electrons, 1, 80 * thetaE, nu, F);
            double expectedQ = OverElectrons(electrons, 1, 80 * thetaE, nu, G);
            Assert.AreEqual(expectedI, jI, 0.02 * expectedI);
            Assert.AreEqual(expectedQ, jQ, 0.02 * expectedQ);
        }

        // x1 = ν / (γ_min² ν_p): below 1 the cutoff at γ_min shapes the spectrum, above it the power law does.
        [TestCase(0.1)]
        [TestCase(1.0)]
        [TestCase(10.0)]
        [TestCase(1000.0)]
        public void PowerLaw_MatchesDirectIntegral(double x1)
        {
            double nuP = 1.5 * GyroFrequency(Gauss) * SinTheta, nu = x1 * MinLorentzFactor * MinLorentzFactor * nuP;
            PowerLaw(Density, Gauss, SinTheta, nu, out double jI, out double jQ);

            double p = PowerLawIndex, g1 = MinLorentzFactor;
            Func<double, double> electrons = g => Density * (p - 1) * Math.Pow(g1, p - 1) * Math.Pow(g, -p);
            double expectedI = OverElectrons(electrons, g1, 1e4 * g1, nu, F);
            double expectedQ = OverElectrons(electrons, g1, 1e4 * g1, nu, G);
            Assert.AreEqual(expectedI, jI, 0.01 * expectedI);
            Assert.AreEqual(expectedQ, jQ, 0.01 * expectedQ);
        }

        [TestCase(0.0, 0.5)]
        [TestCase(20.0, 0.0)]
        public void BothKinds_AreDarkWithoutAFieldAcrossTheRay(double gauss, double sinTheta)
        {
            // Near the spin axis the thermal density, and with it the field, underflows to 0 first.
            Synchrotron.PowerLaw(1e3, gauss, sinTheta, 230e9, out double pI, out double pQ);
            Synchrotron.Thermal(1e6, 10, gauss, sinTheta, 230e9, out double tI, out double tQ);
            Assert.AreEqual(0, pI);
            Assert.AreEqual(0, pQ);
            Assert.AreEqual(0, tI);
            Assert.AreEqual(0, tQ);
        }

        [Test]
        public void PowerLaw_AboveTheCutoffMatchesTheGammaFunctionForm()
        {
            // Dexter (2016) A38 and A39: the textbook power-law emissivity, valid for ν ≫ γ_min² ν_p.
            double p = PowerLawIndex, g1 = MinLorentzFactor;
            double nuP = 1.5 * GyroFrequency(Gauss) * SinTheta, nu = 1e5 * g1 * g1 * nuP;
            PowerLaw(Density, Gauss, SinTheta, nu, out double jI, out double jQ);

            double expectedI = Density * ElectronCharge * ElectronCharge * (p - 1) * nuP
                             / (2 * Math.Sqrt(3) * LightSpeed * Math.Pow(g1, 1 - p))
                             * Math.Pow(nu / nuP, -(p - 1) / 2) * Math.Pow(2, (p - 3) / 2) * (p + 7.0 / 3) / (p + 1)
                             * Gamma(p / 4 + 7.0 / 12) * Gamma(p / 4 - 1.0 / 12);
            Assert.AreEqual(expectedI, jI, 1e-3 * expectedI);
            Assert.AreEqual((p + 1) / (p + 7.0 / 3), jQ / jI, 1e-3);
        }

        // ∫ N(γ) √3 e³ B sinθ S(ν/ν_c(γ)) / (4π m c²) dγ with ν_c = 1.5 ν_B sinθ γ², by trapezoids in ln γ.
        static double OverElectrons(Func<double, double> electrons, double gMin, double gMax, double nu, Func<double, double> spectrum)
        {
            const int Steps = 4000;
            double nuP = 1.5 * GyroFrequency(Gauss) * SinTheta, span = Math.Log(gMax / gMin), sum = 0;
            for (int i = 0; i <= Steps; i++)
            {
                double g = gMin * Math.Exp(span * i / Steps);
                double f = electrons(g) * g * spectrum(nu / (nuP * g * g));
                sum += i == 0 || i == Steps ? 0.5 * f : f;
            }
            double perElectron = Math.Sqrt(3) * Math.Pow(ElectronCharge, 3) * Gauss * SinTheta
                               / (4 * Math.PI * ElectronMass * LightSpeed * LightSpeed);
            return perElectron * sum * span / Steps;
        }

        // Lanczos (g = 7, n = 9), ~1e-15 relative for x > 0.5.
        static double Gamma(double x)
        {
            double[] c =
            {
                0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
                12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
            };
            x -= 1;
            double a = c[0], t = x + 7.5;
            for (int i = 1; i < c.Length; i++) a += c[i] / (x + i);
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
        }
    }
}
