using UnityEngine;
using UnityEngine.Rendering;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// A geometrically thin, optically thick disk in the hole's equatorial plane (Novikov–Thorne), drawn by the lensing
    /// pass. Gas orbits with the hole's spin from the ISCO outwards; each ring glows as a blackbody at its Page–Thorne
    /// temperature, seen through the gravitational and Doppler shift unless <see cref="Shifts"/> is off.
    /// </summary>
    [RequireComponent(typeof(BlackHole))]
    [DisallowMultipleComponent]
    public sealed class AccretionDisk : MonoBehaviour
    {
        public enum TemperatureSource
        {
            [Tooltip("Physical: from the hole's mass and accretion rate.")]
            MassAndAccretion,
            [Tooltip("Art-directed: the Page–Thorne profile scaled to a chosen hottest ring.")]
            PeakTemperature,
        }

        [SerializeField] TemperatureSource temperatureSource = TemperatureSource.MassAndAccretion;

        [Tooltip("Mass in solar masses. Heavier holes have cooler disks: T ∝ M^-1/4 at a fixed Eddington ratio.")]
        [SerializeField, Min(1)] float massSolar = 1e8f;

        [Tooltip("Luminance as a fraction of the Eddington limit. Thin disks are physical between about 0.01 and 0.3.")]
        [SerializeField, Range(0.001f, 1)] float eddingtonRatio = 0.1f;

        [Tooltip("Temperature of the hottest ring in kelvin, used with Peak Temperature.")]
        [SerializeField, Range(1000, 100000)] float peakTemperature = 6000;

        [Tooltip("Outer edge in units of M. Every prograde ISCO lies inside 6 M.")]
        [SerializeField, Range(7, 200)] float outerRadius = 30;

        [Tooltip("Gravitational redshift and Doppler beaming. Off shows each ring's rest-frame colour, as Interstellar did.")]
        [SerializeField] bool shifts = true;

        [Tooltip("Fraction of light blocked per pass through the disk. 1 is physical; lower shows the sky through it.")]
        [SerializeField, Range(0, 1)] float opacity = 1;

        BlackHole _hole;
        Texture2D _table;
        Vector4 _range;
        (double spin, TemperatureSource source, float mass, float ratio, float peak, float outer) _bakedFor;

        public TemperatureSource Source => temperatureSource;
        public float MassSolar => massSolar;
        public float EddingtonRatio => eddingtonRatio;
        public float PeakTemperature => peakTemperature;
        public float OuterRadius => outerRadius;
        public bool Shifts => shifts;
        public float Opacity => opacity;

        void OnDisable()
        {
            CoreUtils.Destroy(_table);
            _table = null;
        }

        /// <summary>
        /// The flux table for Disk.hlsl and its _DiskRange, rebaked only when the hole's spin or a temperature
        /// setting has changed since the last call. The disk owns the texture.
        /// </summary>
        internal Texture2D FluxTable(out Vector4 range)
        {
            if (_hole == null) _hole = GetComponent<BlackHole>();
            var key = (_hole.Spin, temperatureSource, massSolar, eddingtonRatio, peakTemperature, outerRadius);
            if (_table == null || key != _bakedFor)
            {
                var disk = temperatureSource == TemperatureSource.MassAndAccretion
                    ? ThinDisk.FromAccretion(_hole.Spin, outerRadius, massSolar, eddingtonRatio)
                    : ThinDisk.FromPeakTemperature(_hole.Spin, outerRadius, peakTemperature);
                CoreUtils.Destroy(_table);
                _table = DiskTables.BakeFlux(disk, out _range);
                _bakedFor = key;
            }
            range = _range;
            return _table;
        }

        /// <summary>A quasar-like disk: 10⁸ M☉ at a tenth of Eddington, every relativistic effect on.</summary>
        [ContextMenu("Preset/Realistic")]
        internal void ApplyRealisticPreset()
        {
            RecordUndo("Realistic disk");
            temperatureSource = TemperatureSource.MassAndAccretion;
            massSolar = 1e8f;
            eddingtonRatio = 0.1f;
            outerRadius = 30;
            shifts = true;
            opacity = 1;
        }

        /// <summary>
        /// Gargantua as rendered for the film (James et al. 2015, Fig. 15a): a = 0.6, a 4500 K disk, and the Doppler and
        /// gravitational shifts switched off so the disk stays a uniform warm colour.
        /// </summary>
        [ContextMenu("Preset/Interstellar")]
        internal void ApplyInterstellarPreset()
        {
            RecordUndo("Interstellar disk");
            if (_hole == null) _hole = GetComponent<BlackHole>();
            _hole.SetSpin(0.6f);
            temperatureSource = TemperatureSource.PeakTemperature;
            peakTemperature = 4500;
            shifts = false;
            opacity = 1;
        }

        void RecordUndo(string name)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObjects(new Object[] { this, GetComponent<BlackHole>() }, name);
#endif
        }
    }
}
