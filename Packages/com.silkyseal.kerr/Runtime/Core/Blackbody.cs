using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// What a human eye sees of a blackbody: the Planck spectrum weighted by the CIE 1931 2° observer.
    /// Results are absolute: Y is luminance in cd/m² of the emitting surface, so a bright disk and a dim sky keep
    /// their true ratio until exposure. A blackbody at T seen with frequency shift g is exactly a blackbody at g·T,
    /// which is why colour only ever needs a temperature.
    /// </summary>
    public static class Blackbody
    {
        internal const double Planck = 6.62607015e-34, LightSpeed = 2.99792458e8, Boltzmann = 1.380649e-23;
        internal const double LuminousEfficacy = 683.0;        // lm/W at 555 nm, the definition of the candela
        const int FirstNanometre = 360, LastNanometre = 830; // CIE 1931 table range, 1 nm steps

        /// <summary>CIE XYZ of a blackbody surface at this temperature; Y in cd/m². O(471).</summary>
        public static Vector3d Xyz(double kelvin)
        {
            double x = 0, y = 0, z = 0;
            for (int nm = FirstNanometre; nm <= LastNanometre; nm++)
            {
                double radiance = SpectralRadiance(nm * 1e-9, kelvin);
                x += radiance * MatchX(nm);
                y += radiance * MatchY(nm);
                z += radiance * MatchZ(nm);
            }
            double scale = LuminousEfficacy * 1e-9; // 1 nm bin width in metres
            return new Vector3d(x * scale, y * scale, z * scale);
        }

        /// <summary>
        /// Linear sRGB (Rec. 709 primaries, D65 white) in cd/m². Very red blackbodies fall outside the gamut;
        /// their negative components are clipped to zero.
        /// </summary>
        public static Vector3d LinearSrgb(double kelvin)
        {
            var c = XyzToLinearSrgb(Xyz(kelvin));
            return new Vector3d(Math.Max(0, c.X), Math.Max(0, c.Y), Math.Max(0, c.Z));
        }

        /// <summary>CIE XYZ to linear sRGB, unclipped.</summary>
        internal static Vector3d XyzToLinearSrgb(Vector3d c) => new Vector3d(
            3.2404542 * c.X - 1.5371385 * c.Y - 0.4985314 * c.Z,
            -0.9692660 * c.X + 1.8760108 * c.Y + 0.0415560 * c.Z,
            0.0556434 * c.X - 0.2040259 * c.Y + 1.0572252 * c.Z);

        // Planck's law per unit wavelength, W·sr⁻¹·m⁻³.
        static double SpectralRadiance(double metres, double kelvin)
        {
            double l5 = metres * metres * metres * metres * metres;
            return 2 * Planck * LightSpeed * LightSpeed / (l5 * (Math.Exp(Planck * LightSpeed / (metres * Boltzmann * kelvin)) - 1));
        }

        // shortcut: CIE 1931 colour matching functions by the multi-lobe Gaussian fit of Wyman, Sloan & Shirley (2013),
        // JCGT 2(2); within ~0.005 xy of the tabulated CIE data on the Planckian locus. Swap in the 1 nm CIE table
        // if colours ever need to be closer than one just-noticeable difference.
        internal static double MatchX(double nm) =>
            1.056 * Lobe(nm, 599.8, 37.9, 31.0) + 0.362 * Lobe(nm, 442.0, 16.0, 26.7) - 0.065 * Lobe(nm, 501.1, 20.4, 26.2);
        internal static double MatchY(double nm) => 0.821 * Lobe(nm, 568.8, 46.9, 40.5) + 0.286 * Lobe(nm, 530.9, 16.3, 31.1);
        internal static double MatchZ(double nm) => 1.217 * Lobe(nm, 437.0, 11.8, 36.0) + 0.681 * Lobe(nm, 459.0, 26.0, 13.8);

        static double Lobe(double nm, double centre, double widthBelow, double widthAbove)
        {
            double t = (nm - centre) / (nm < centre ? widthBelow : widthAbove);
            return Math.Exp(-0.5 * t * t);
        }
    }
}
