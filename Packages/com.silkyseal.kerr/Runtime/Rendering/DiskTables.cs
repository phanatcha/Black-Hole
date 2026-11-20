using System;
using Unity.Collections;
using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// 1D float tables the disk shader reads with linear interpolation (Disk.hlsl): the blackbody colour over ln T and
    /// the Page–Thorne flux over ln r. Baking in double keeps the exact Core functions the only definition of both.
    /// Callers own the returned textures.
    /// </summary>
    static class DiskTables
    {
        /// <summary>Texels per table; KERR_TABLE_SIZE in Disk.hlsl.</summary>
        public const int Size = 1024;

        // Below 500 K a disk is invisible next to anything else in the frame; 1e8 K covers X-ray binaries.
        const double MinKelvin = 500, MaxKelvin = 1e8;

        /// <summary>_BlackbodyRange: (ln T_min, 1 / (ln T_max − ln T_min)).</summary>
        public static Vector4 BlackbodyRange =>
            new Vector4((float)Math.Log(MinKelvin), (float)(1 / Math.Log(MaxKelvin / MinKelvin)), 0, 0);

        /// <summary>
        /// rgb = linear sRGB / Y, a = log2 Y with Y in cd/m². Splitting chromaticity from log luminance keeps both
        /// smooth over the 30 orders of magnitude Y spans, so linear interpolation stays within 0.1 %. O(Size × 471).
        /// </summary>
        public static Texture2D BakeBlackbody()
        {
            var texture = Create("Kerr Blackbody", TextureFormat.RGBAFloat);
            var texels = new NativeArray<Vector4>(Size, Allocator.Temp);
            for (int i = 0; i < Size; i++)
            {
                double kelvin = MinKelvin * Math.Pow(MaxKelvin / MinKelvin, (double)i / (Size - 1));
                double y = Blackbody.Xyz(kelvin).Y;
                var rgb = Blackbody.LinearSrgb(kelvin) / y;
                texels[i] = new Vector4((float)rgb.X, (float)rgb.Y, (float)rgb.Z, (float)Math.Log(y, 2));
            }
            return Upload(texture, texels);
        }

        /// <summary>
        /// (T / T_peak)⁴ over ln r from the inner to the outer edge. The flux, unlike T, is linear in r at the ISCO,
        /// so the inner edge interpolates cleanly. range = _DiskRange: (r_in, 1 / ln(r_out / r_in), r_out, T_peak).
        /// </summary>
        public static Texture2D BakeFlux(ThinDisk disk, out Vector4 range)
        {
            var texture = Create("Kerr Disk Flux", TextureFormat.RFloat);
            var t4 = new double[Size];
            double peak = 0, span = Math.Log(disk.OuterRadius / disk.InnerRadius);
            for (int i = 0; i < Size; i++)
            {
                double t = disk.Temperature(disk.InnerRadius * Math.Exp(span * i / (Size - 1)));
                t4[i] = t * t * t * t;
                peak = Math.Max(peak, t4[i]);
            }

            var texels = new NativeArray<float>(Size, Allocator.Temp);
            for (int i = 0; i < Size; i++) texels[i] = peak > 0 ? (float)(t4[i] / peak) : 0;
            range = new Vector4((float)disk.InnerRadius, (float)(1 / span), (float)disk.OuterRadius, (float)Math.Sqrt(Math.Sqrt(peak)));
            return Upload(texture, texels);
        }

        static Texture2D Create(string name, TextureFormat format) =>
            new Texture2D(Size, 1, format, false, true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point, // the shader interpolates itself; 32-bit float filtering is optional on Metal
            };

        static Texture2D Upload<T>(Texture2D texture, NativeArray<T> texels) where T : struct
        {
            texture.SetPixelData(texels, 0);
            texture.Apply(false, true);
            texels.Dispose();
            return texture;
        }
    }
}
