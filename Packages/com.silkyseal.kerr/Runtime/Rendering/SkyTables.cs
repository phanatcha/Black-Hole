using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Textures the shade kernel reads for the sky (Stars.hlsl, Spectrum.hlsl), copied from the Core data so the CPU
    /// stays the only definition. All are point-sampled with Load. Callers own the returned textures.
    /// </summary>
    static class SkyTables
    {
        /// <summary>The star map as one slice per cube face, its levels as mips; texels are (lux, kelvin, u, v).</summary>
        public static Texture2DArray BakeStars(StarMap map)
        {
            var texture = new Texture2DArray(map.Size, map.Size, 6, TextureFormat.RGBAFloat, map.LevelCount, true)
            {
                name = "Kerr Stars",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            for (int level = 0; level < map.LevelCount; level++)
            {
                int floatsPerFace = (map.Size >> level) * (map.Size >> level) * 4;
                for (int face = 0; face < 6; face++)
                    texture.SetPixelData(map.Levels[level], level, face, face * floatsPerFace);
            }
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// RgbSpectrum's fit as one slice per largest channel of (ln T, a, b, 0), and its quadrature weights as a
        /// SampleCount × 1 texture of linear sRGB.
        /// </summary>
        public static Texture2DArray BakeSpectrum(out Texture2D weights)
        {
            const int n = RgbSpectrum.TableSize;
            var fit = RgbSpectrum.Table;
            var texels = new Vector4[fit.Length];
            for (int t = 0; t < fit.Length; t++) texels[t] = new Vector4((float)fit[t].X, (float)fit[t].Y, (float)fit[t].Z, 0);
            var table = new Texture2DArray(n, n, 3, TextureFormat.RGBAFloat, false, true)
            {
                name = "Kerr Spectrum",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            for (int slice = 0; slice < 3; slice++) table.SetPixelData(texels, 0, slice, slice * n * n);
            table.Apply(false, true);

            var w = RgbSpectrum.Weights;
            var row = new Vector4[w.Length];
            for (int k = 0; k < w.Length; k++) row[k] = new Vector4((float)w[k].X, (float)w[k].Y, (float)w[k].Z, 0);
            weights = new Texture2D(w.Length, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "Kerr Spectrum Weights",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            weights.SetPixelData(row, 0);
            weights.Apply(false, true);
            return table;
        }
    }
}
