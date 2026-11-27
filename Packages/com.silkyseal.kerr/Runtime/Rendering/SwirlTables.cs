using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// How far each ring of the <see cref="SwirlPattern"/> has turned, for the shaders' _SwirlTurns. Worked out in
    /// double: in float, Ω t after an hour of play would lose the frame-to-frame motion.
    /// </summary>
    static class SwirlTables
    {
        /// <summary>Length of the _SwirlTurns array: the disk's rings, then the hot flow's coarser ones, four to a vector.</summary>
        public const int Size = (SwirlPattern.Rings + SwirlPattern.Rings / SwirlPattern.GasCoarseness) / 4;

        /// <param name="time">Kerr-Schild coordinate time at the camera, M.</param>
        public static void Turns(double spin, double time, Vector4[] turns)
        {
            for (int i = 0; i < Size; i++)
            {
                var v = new Vector4();
                for (int c = 0; c < 4; c++)
                {
                    int ring = 4 * i + c;
                    v[c] = (float)(ring < SwirlPattern.Rings
                        ? SwirlPattern.Turns(ring, spin, time)
                        : SwirlPattern.Turns(ring - SwirlPattern.Rings, spin, time, SwirlPattern.GasCoarseness));
                }
                turns[i] = v;
            }
        }
    }
}
