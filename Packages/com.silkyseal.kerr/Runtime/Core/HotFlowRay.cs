using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Gathers a <see cref="HotFlow"/>'s light along one backward-traced ray: specific intensity at 230 GHz with its
    /// linear polarization, and at the three <see cref="VisibleBand"/> frequencies. Emission and absorption are taken in
    /// the gas's frame and carried to the camera through the invariants I_ν/ν³ and ν α_ν; the polarization angle at
    /// the camera comes from the Walker–Penrose constant. Left out: Faraday rotation and conversion, circular
    /// polarization, absorption by the power-law electrons and the polarized absorption terms. Intensities are cgs
    /// (erg s⁻¹ cm⁻² Hz⁻¹ sr⁻¹) at the camera's frequencies.
    /// </summary>
    public struct HotFlowRay : IRayObserver
    {
        /// <summary>The EHT's observing frequency, Hz.</summary>
        public const double RadioFrequency = 230e9;
        /// <summary>
        /// A step moves the ray at most this fraction of r inside the flow, so the gas is sampled about 23 times per
        /// decade in radius; the trapezoid rule converges at 1 % here.
        /// </summary>
        public const double StepFraction = 0.1;

        /// <summary>Stokes I, Q, U at <see cref="RadioFrequency"/>; Q and U on the camera's (up, left) axes.</summary>
        public double Radio, StokesQ, StokesU;
        /// <summary>I at <see cref="VisibleBand.Frequencies"/>.</summary>
        public Vector3d Visible;
        /// <summary>How much of the light from behind the flow still reaches the camera, at 230 GHz and in the visible.</summary>
        public double RadioTransmittance;
        public Vector3d VisibleTransmittance;

        readonly HotFlow _flow;
        readonly WalkerPenrose.Screen _screen;
        readonly double _centimetres, _innerRadius;
        Sample _last;

        public HotFlowRay(in HotFlow flow, in WalkerPenrose.Screen screen)
        {
            _flow = flow;
            _screen = screen;
            _centimetres = flow.LengthScale;
            _innerRadius = KerrSchild.HorizonRadius(flow.Spin) + RayTracer.HorizonMargin;
            _last = default;
            Radio = StokesQ = StokesU = 0;
            Visible = default;
            RadioTransmittance = 1;
            VisibleTransmittance = new Vector3d(1, 1, 1);
        }

        public bool OnEquatorCrossing(Vector3d x, double kt, double kPhi) => true;

        public bool OnStep(Vector3d x, Vector4d p, double h, ref double nextStep)
        {
            double a = _flow.Spin, r = KerrSchild.Radius(x, a);
            var k = KerrSchild.Raise(p, x, a);
            // Outside the flow a step may only reach its edge; inside, a fixed fraction of r.
            nextStep = Math.Min(nextStep, Math.Max(StepFraction * r, r - HotFlow.OuterRadius) / k.Spatial.Length);

            var sample = Emit(x, k, r);
            if (h > 0) Accumulate(_last, sample, h * _centimetres);
            _last = sample;
            return true;
        }

        // Emission and absorption at one point, already in the invariant form the camera's frequencies need.
        struct Sample
        {
            public double Radio, Q, U, RadioAbsorption;
            public double Blue, Green, Red, BlueAbsorption, GreenAbsorption, RedAbsorption;
        }

        Sample Emit(Vector3d x, Vector4d k, double r)
        {
            var s = default(Sample);
            if (r >= HotFlow.OuterRadius || r <= _innerRadius) return s;
            double thermal = _flow.ThermalDensity(x, r), nonthermal = _flow.NonthermalDensity(x, r);
            if (thermal <= 0 && nonthermal <= 0) return s;

            double a = _flow.Spin;
            var u = _flow.Velocity(x);
            var b = _flow.FieldDirection(x, u);
            double shift = KerrSchild.Dot(k, u, x, a); // ν in the gas over ν at the camera, 1/g
            double cos = KerrSchild.Dot(k, b, x, a) / shift, sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
            double kelvin = _flow.ElectronTemperature(r), gauss = _flow.FieldStrength(thermal, r);

            var e = new Electrons(thermal, nonthermal, kelvin, gauss, sin, shift);
            e.Emit(RadioFrequency, out s.Radio, out double polarized, out s.RadioAbsorption);
            if (polarized > 0)
            {
                var f = WalkerPenrose.Perpendicular(u, k, b, x, a);
                WalkerPenrose.Constant(x, k, f, a, out double k1, out double k2);
                _screen.Polarization(k1, k2, out double cos2, out double sin2);
                s.Q = polarized * cos2;
                s.U = polarized * sin2;
            }
            var nu = VisibleBand.Frequencies;
            e.Emit(nu.X, out s.Blue, out _, out s.BlueAbsorption);
            e.Emit(nu.Y, out s.Green, out _, out s.GreenAbsorption);
            e.Emit(nu.Z, out s.Red, out _, out s.RedAbsorption);
            return s;
        }

        // Trapezoid rule between the step's two ends, then the exact slab update.
        void Accumulate(in Sample s0, in Sample s1, double length)
        {
            double absorption = 0.5 * (s0.RadioAbsorption + s1.RadioAbsorption);
            double t = RadioTransmittance;
            RadiativeTransfer.Step(ref StokesQ, ref t, 0.5 * (s0.Q + s1.Q), absorption, length);
            t = RadioTransmittance;
            RadiativeTransfer.Step(ref StokesU, ref t, 0.5 * (s0.U + s1.U), absorption, length);
            RadiativeTransfer.Step(ref Radio, ref RadioTransmittance, 0.5 * (s0.Radio + s1.Radio), absorption, length);

            double blue = Visible.X, green = Visible.Y, red = Visible.Z;
            double tBlue = VisibleTransmittance.X, tGreen = VisibleTransmittance.Y, tRed = VisibleTransmittance.Z;
            RadiativeTransfer.Step(ref blue, ref tBlue, 0.5 * (s0.Blue + s1.Blue), 0.5 * (s0.BlueAbsorption + s1.BlueAbsorption), length);
            RadiativeTransfer.Step(ref green, ref tGreen, 0.5 * (s0.Green + s1.Green), 0.5 * (s0.GreenAbsorption + s1.GreenAbsorption), length);
            RadiativeTransfer.Step(ref red, ref tRed, 0.5 * (s0.Red + s1.Red), 0.5 * (s0.RedAbsorption + s1.RedAbsorption), length);
            Visible = new Vector3d(blue, green, red);
            VisibleTransmittance = new Vector3d(tBlue, tGreen, tRed);
        }

        readonly struct Electrons
        {
            readonly double _thermal, _nonthermal, _kelvin, _theta, _gauss, _sin, _shift;

            public Electrons(double thermal, double nonthermal, double kelvin, double gauss, double sin, double shift)
            {
                _thermal = thermal;
                _nonthermal = nonthermal;
                _kelvin = kelvin;
                _theta = kelvin / HotFlow.ElectronRestKelvin;
                _gauss = gauss;
                _sin = sin;
                _shift = shift;
            }

            /// <summary>g² j_I, g² j_Q and α/g at camera frequency nu, with g = 1 / shift.</summary>
            public void Emit(double nu, out double emission, out double polarized, out double absorption)
            {
                double gas = nu * _shift;
                Synchrotron.Thermal(_thermal, _theta, _gauss, _sin, gas, out double jI, out double jQ);
                Synchrotron.PowerLaw(_nonthermal, _gauss, _sin, gas, out double pI, out double pQ);
                double g2 = 1 / (_shift * _shift);
                emission = g2 * (jI + pI);
                polarized = g2 * (jQ + pQ);
                absorption = jI / Synchrotron.PlanckRadiance(gas, _kelvin) * _shift; // Kirchhoff, thermal electrons only
            }
        }
    }
}
