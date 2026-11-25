using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Hot, see-through gas around the hole: the accretion flow fitted to Sgr A* (<see cref="HotFlow.SgrA"/>), glowing by
    /// synchrotron and drawn by the lensing pass in one of three views. It follows the hole's spin.
    /// </summary>
    [RequireComponent(typeof(BlackHole))]
    [DisallowMultipleComponent]
    public sealed class HotGas : MonoBehaviour
    {
        /// <summary>What the pass draws; the values are _GasView in Gas.hlsl.</summary>
        public enum View
        {
            [Tooltip("What an eye would see: the gas's glow at visible wavelengths, over the sky, stars and disk.")]
            Eye = 1,
            [Tooltip("230 GHz brightness in the EHT's colour map, on a black sky.")]
            Radio = 2,
            [Tooltip("230 GHz linear polarization: hue is the polarization angle, brightness the polarized intensity.")]
            Polarization = 3,
        }

        [SerializeField] View view = View.Radio;

        [Tooltip("The field's direction as weights of radial, toroidal and vertical. Broderick & Loeb's fit is toroidal; " +
                 "radial weight twists the polarization pattern.")]
        [SerializeField] Vector3 fieldMix = new Vector3(0, 1, 0);

        [Tooltip("Brightness temperature in kelvin drawn as white in the radio and polarization views. The EHT's " +
                 "blurred Sgr A* ring peaks near 10¹⁰ K; the unblurred model reaches a few 10¹⁰ K.")]
        [SerializeField, Min(1e6f)] float whiteKelvin = 6e10f;

        public View CurrentView => view;
        public float WhiteKelvin => whiteKelvin;

        /// <summary>The flow around a hole of this spin.</summary>
        public HotFlow Flow(double spin) => HotFlow.SgrA(spin, new Vector3d(fieldMix.x, fieldMix.y, fieldMix.z));
    }
}
