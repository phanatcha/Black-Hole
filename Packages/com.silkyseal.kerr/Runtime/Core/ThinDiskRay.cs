using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Shades one ray against a <see cref="ThinDisk"/>: front to back over every pass through the disk, so lensed
    /// images of the far side and the photon-ring copies all add in. Trace with the disk's own spin.
    /// </summary>
    public struct ThinDiskRay : IEquatorObserver
    {
        /// <summary>Below this the remaining sky and disk behind cannot show; the ray stops as absorbed.</summary>
        public const double OpaqueTransmittance = 1e-3;

        readonly ThinDisk _disk;
        readonly double _opacity;
        readonly bool _shifts;

        /// <summary>Linear sRGB in cd/m², collected so far.</summary>
        public Vector3d Radiance;
        /// <summary>Fraction of whatever lies behind (more disk, the sky) still visible.</summary>
        public double Transmittance;
        /// <summary>Boyer–Lindquist radius of the first disk hit; NaN while the ray has not touched the disk.</summary>
        public double FirstHitRadius;

        /// <param name="opacity">Fraction of light each pass blocks; 1 is the physical, optically thick disk.</param>
        /// <param name="shifts">Apply the gravitational and Doppler frequency shift; off shows rest-frame colours.</param>
        public ThinDiskRay(ThinDisk disk, double opacity, bool shifts)
        {
            _disk = disk;
            _opacity = opacity;
            _shifts = shifts;
            Radiance = default;
            Transmittance = 1;
            FirstHitRadius = double.NaN;
        }

        public bool OnEquatorCrossing(Vector3d x, double kt, double kPhi)
        {
            double a = _disk.Spin, r = KerrSchild.Radius(x, a), t = _disk.Temperature(r);
            if (t <= 0) return true;

            // A blackbody at T seen with frequency ratio g is a blackbody at gT, the g⁴ beaming included.
            double g = _shifts ? CircularOrbit.Shift(x, kt, kPhi, a) : 1;
            Radiance += Blackbody.LinearSrgb(g * t) * (Transmittance * _opacity);
            Transmittance *= 1 - _opacity;
            if (double.IsNaN(FirstHitRadius)) FirstHitRadius = r;
            return Transmittance >= OpaqueTransmittance;
        }
    }
}
