using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Unpolarized transfer of intensity through emitting, absorbing matter, back from the camera: each step adds what
    /// the matter emits, dimmed by everything in front of it. Exact for matter that is uniform within a step, so the
    /// step count changes only how well the matter is sampled, never the answer for a uniform slab.
    /// </summary>
    public static class RadiativeTransfer
    {
        /// <summary>
        /// One step of length ds through matter with emissivity j and absorption coefficient α (any units with j·ds
        /// an intensity and α·ds an optical depth). intensity gains transmittance · j·ds · (1 − e^−τ)/τ, the slab's own
        /// self-absorbed glow; transmittance, the fraction of light from behind that still gets through, falls by e^−τ.
        /// </summary>
        public static void Step(ref double intensity, ref double transmittance, double emission, double absorption, double length)
        {
            double depth = absorption * length;
            // (1 − e^−τ)/τ loses digits to cancellation for small τ; the series is exact to 1e-17 there.
            double selfShielding = depth > 1e-4 ? (1 - Math.Exp(-depth)) / depth : 1 - depth * (0.5 - depth / 6);
            intensity += transmittance * emission * length * selfShielding;
            transmittance *= Math.Exp(-depth);
        }
    }
}
