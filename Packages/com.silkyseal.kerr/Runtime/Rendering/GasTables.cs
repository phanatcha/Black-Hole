using System;
using Unity.Collections;
using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// What Gas.hlsl reads: the power-law cutoff integrals as a table over ln x, and a <see cref="HotFlow"/>'s numbers as
    /// _Gas. Baking in double keeps the Core functions the only definition of both.
    /// </summary>
    static class GasTables
    {
        /// <summary>Texels in the power-law table; KERR_GAS_TABLE_SIZE in Gas.hlsl.</summary>
        public const int Size = 1024;
        /// <summary>Length of _Gas.</summary>
        public const int ParameterCount = 8;
        const double Gigahertz = 1e9;

        static double Span => Math.Log(Synchrotron.TableMaxX / Synchrotron.TableMinX);

        /// <summary>(ln G_I, ln G_Q) over ln x, x_min to x_max. Callers own the texture. O(Size).</summary>
        public static Texture2D BakePowerLaw()
        {
            var texture = new Texture2D(Size, 1, TextureFormat.RGFloat, false, true)
            {
                name = "Kerr Power-Law Integrals",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point, // the shader interpolates itself; 32-bit float filtering is optional on Metal
            };
            var texels = new NativeArray<Vector2>(Size, Allocator.Temp);
            for (int i = 0; i < Size; i++)
            {
                Synchrotron.PowerLawIntegrals(Synchrotron.TableMinX * Math.Exp(Span * i / (Size - 1)), out double gI, out double gQ);
                texels[i] = new Vector2((float)Math.Log(gI), (float)Math.Log(gQ));
            }
            texture.SetPixelData(texels, 0);
            texture.Apply(false, true);
            texels.Dispose();
            return texture;
        }

        /// <summary>Fills _Gas (<see cref="ParameterCount"/> long) for flow; the layout is spelled out in Gas.hlsl.</summary>
        public static void Parameters(in HotFlow flow, Vector4[] parameters)
        {
            double a = flow.Spin, isco = CircularOrbit.IscoRadius(a);
            var nu = VisibleBand.Frequencies;
            var rows = VisibleBand.Rows;
            parameters[0] = V(flow.ThermalDensityScale, flow.NonthermalDensityScale, flow.NonthermalSlope, flow.TemperatureScale);
            parameters[1] = V(isco, CircularOrbit.SpecificEnergy(isco, a), CircularOrbit.SpecificAngularMomentum(isco, a), flow.LengthScale);
            parameters[2] = V(flow.FieldMix.X, flow.FieldMix.Y, flow.FieldMix.Z, 0);
            parameters[3] = V(nu.X / Gigahertz, nu.Y / Gigahertz, nu.Z / Gigahertz, HotFlowRay.RadioFrequency / Gigahertz);
            for (int i = 0; i < 3; i++) parameters[4 + i] = V(rows[i].X, rows[i].Y, rows[i].Z, 0);
            parameters[7] = V(Math.Log(Synchrotron.TableMinX), 1 / Span, Synchrotron.SmallXSlope, 0);
        }

        /// <summary>_GasScale: 1 / the 230 GHz intensity of brightness temperature whiteKelvin (Rayleigh–Jeans).</summary>
        public static float WhiteScale(double whiteKelvin)
        {
            double nu = HotFlowRay.RadioFrequency;
            return (float)(Synchrotron.LightSpeed * Synchrotron.LightSpeed / (2 * Synchrotron.Boltzmann * nu * nu * whiteKelvin));
        }

        static Vector4 V(double x, double y, double z, double w) => new Vector4((float)x, (float)y, (float)z, (float)w);
    }
}
