using System;
using System.Globalization;
using System.IO;

namespace SilkySeal.Kerr
{
    /// <summary>A point source on the sky: unit direction in Unity world axes, illuminance at the observer, temperature.</summary>
    public readonly struct Star
    {
        public readonly Vector3d Direction;
        public readonly double Lux, Kelvin;

        public Star(Vector3d direction, double lux, double kelvin) { Direction = direction; Lux = lux; Kelvin = kelvin; }
    }

    /// <summary>
    /// The Hipparcos catalogue (ESA 1997, VizieR I/239) as stars: direction, illuminance and blackbody temperature.
    /// The packed form keeps the catalogue's own numbers so the physics stays here: int32 count, then float32 RA°, Dec°,
    /// V and B − V per star (NaN when B − V is unknown), little-endian.
    /// </summary>
    public static class StarCatalogue
    {
        /// <summary>Illuminance of a V = 0 star above the atmosphere, set by the Sun: V = −26.74 gives about 127 klux.</summary>
        public const double ZeroMagnitudeLux = 2.54e-6;

        /// <summary>Temperature given to a star without a B − V index (1 % of Hipparcos, nearly all faint).</summary>
        // shortcut: a Sun-like guess; take the spectral type from the catalogue if these stars ever matter.
        public const double DefaultKelvin = 5800;

        /// <summary>Illuminance in lux of a star of visual magnitude V.</summary>
        // shortcut: V is read as photopic. Its passband is close to the eye's, off by a few tenths of a magnitude for the
        // reddest and bluest stars; switch to a B − V colour term if star brightness ever needs that accuracy.
        public static double Lux(double vMagnitude) => ZeroMagnitudeLux * Math.Pow(10, -0.4 * vMagnitude);

        /// <summary>Blackbody temperature from the B − V colour index, Ballesteros (2012), EPL 97, 34008.</summary>
        public static double Kelvin(double bMinusV) => 4600 * (1 / (0.92 * bMinusV + 1.7) + 1 / (0.92 * bMinusV + 0.62));

        /// <summary>
        /// ICRS right ascension and declination as a unit vector: celestial north is +Y, RA 0 is +Z, RA 90° is −X.
        /// That is where Unity's latitude–longitude cube import puts an RA-centred sky panorama, and the odd axis swap
        /// takes right-handed ICRS into Unity's left-handed world without mirroring the sky.
        /// </summary>
        public static Vector3d Direction(double raDegrees, double decDegrees)
        {
            double ra = raDegrees * Math.PI / 180, dec = decDegrees * Math.PI / 180;
            return new Vector3d(-Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec), Math.Cos(dec) * Math.Cos(ra));
        }

        /// <summary>
        /// Packs the VizieR TSV export of I/239 with columns HIP, RAICRS, DEICRS, Vmag, B-V. Rows without a position or
        /// a V magnitude are dropped.
        /// </summary>
        public static byte[] Pack(TextReader tsv)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(0);
            int count = 0;
            string line;
            while ((line = tsv.ReadLine()) != null)
            {
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                var fields = line.Split('\t');
                // Column names, units and dashes fail to parse as numbers and drop out with the incomplete rows.
                if (fields.Length < 5 || !TryParse(fields[1], out double ra) || !TryParse(fields[2], out double dec) ||
                    !TryParse(fields[3], out double v))
                    continue;
                writer.Write((float)ra);
                writer.Write((float)dec);
                writer.Write((float)v);
                writer.Write(TryParse(fields[4], out double bMinusV) ? (float)bMinusV : float.NaN);
                count++;
            }
            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(count);
            writer.Flush();
            return stream.ToArray();
        }

        public static Star[] Unpack(byte[] packed)
        {
            using var reader = new BinaryReader(new MemoryStream(packed));
            var stars = new Star[reader.ReadInt32()];
            for (int s = 0; s < stars.Length; s++)
            {
                double ra = reader.ReadSingle(), dec = reader.ReadSingle(), v = reader.ReadSingle(), bMinusV = reader.ReadSingle();
                stars[s] = new Star(Direction(ra, dec), Lux(v), double.IsNaN(bMinusV) ? DefaultKelvin : Kelvin(bMinusV));
            }
            return stars;
        }

        static bool TryParse(string field, out double value) =>
            double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
