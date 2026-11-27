using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Shades one ray against a <see cref="ThinDisk"/>: front to back over every pass through the disk, so lensed
    /// images of the far side and the photon-ring copies all add in. Trace with the disk's own spin.
    /// </summary>
    public struct ThinDiskRay : IRayObserver
    {
        /// <summary>Below this the remaining sky and disk behind cannot show; the ray stops as absorbed.</summary>
        public const double OpaqueTransmittance = 1e-3;

        readonly ThinDisk _disk;
        readonly double _opacity;
        readonly bool _shifts;
        readonly SwirlPattern _swirl;

        /// <summary>Linear sRGB in cd/m², collected so far.</summary>
        public Vector3d Radiance;
        /// <summary>Fraction of whatever lies behind (more disk, the sky) still visible.</summary>
        public double Transmittance;
        /// <summary>Boyer–Lindquist radius of the first disk hit; NaN while the ray has not touched the disk.</summary>
        public double FirstHitRadius;

        /// <param name="opacity">Fraction of light each pass blocks; 1 is the physical, optically thick disk.</param>
        /// <param name="shifts">Apply the gravitational and Doppler frequency shift; off shows rest-frame colours.</param>
        /// <param name="swirl">Streaks in the disk's light; the default leaves it smooth.</param>
        public ThinDiskRay(ThinDisk disk, double opacity, bool shifts, SwirlPattern swirl = default)
        {
            _disk = disk;
            _opacity = opacity;
            _shifts = shifts;
            _swirl = swirl;
            Radiance = default;
            Transmittance = 1;
            FirstHitRadius = double.NaN;
        }

        public bool OnEquatorCrossing(Vector3d x, double time, double kt, double kPhi)
        {
            double a = _disk.Spin, r = KerrSchild.Radius(x, a), t = _disk.Temperature(r);
            if (t <= 0) return true;

            // A blackbody at T seen with frequency ratio g is a blackbody at gT, the g⁴ beaming included. The streaks
            // are where the gas was when this light left it.
            double g = _shifts ? CircularOrbit.Shift(x, kt, kPhi, a) : 1;
            double streaks = _swirl.Value(r, Math.Atan2(x.Y, x.X), 0, time);
            var emission = Blackbody.LinearSrgb(g * t);
            double opacity = _opacity;
            if (_swirl.BroadStreams)
            {
                emission = new Vector3d(0.18, 0.47, 1.0) * Math.Max(0, emission.Z);
                double cloud = _swirl.CloudTexture;
                streaks = 1 + (streaks - 1) * Math.Pow(Transmittance, 4 - 2 * cloud);
                if (cloud > 0)
                {
                    opacity += 0.55 * cloud * opacity * (1 - opacity) * (streaks - 1);
                    double tOuter = Math.Max(0, Math.Min(1, (r / _disk.InnerRadius - 3) / 7));
                    double outer = tOuter * tOuter * (3 - 2 * tOuter);
                    streaks *= 1 + 0.8 * cloud * outer * Math.Max(0, streaks - 1);
                }
            }
            Radiance += emission * (Transmittance * opacity * streaks);
            Transmittance *= 1 - opacity;
            if (double.IsNaN(FirstHitRadius)) FirstHitRadius = r;
            return Transmittance >= OpaqueTransmittance;
        }

        public bool OnStep(Vector3d x, double time, Vector4d p, double h, ref double nextStep) => true;
    }
}
