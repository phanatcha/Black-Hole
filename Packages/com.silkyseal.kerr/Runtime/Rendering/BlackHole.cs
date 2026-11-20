using System.Collections.Generic;
using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// A Kerr black hole placed in the scene. Its local up axis is the spin axis.
    /// Converts between Unity world space and Kerr-Schild coordinates in units of M:
    /// local (x, y, z) / unitsPerM maps to KS (x, z, y). The swap turns Unity's left-handed,
    /// y-up axes into right-handed, z-up ones.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BlackHole : MonoBehaviour
    {
        // Upper bound keeps the prograde photon orbit more than RayTracer.HorizonMargin outside r₊; at a = 1 they touch.
        const float MaxSpin = 0.9999f;

        [Tooltip("Dimensionless spin a/M. 0 is Schwarzschild; the Gargantua of Interstellar used 0.6.")]
        [SerializeField, Range(0, MaxSpin)] float spin = 0.6f;

        [Tooltip("Unity units per black-hole mass M (GM/c²). The horizon radius is about 2 M at low spin.")]
        [SerializeField, Min(1e-4f)] float unitsPerM = 1;

        [Tooltip("Speed of light in Unity units per second. Sets how strongly flying speed causes aberration and Doppler shift.")]
        [SerializeField, Min(1e-6f)] float speedOfLight = 30;

        [Tooltip("Sky behind the hole. Empty uses a procedural grid that makes lensing easy to read.")]
        [SerializeField] Cubemap sky;

        static readonly List<BlackHole> s_Active = new List<BlackHole>();

        /// <summary>Enabled black holes, in enable order. Read-only so only OnEnable/OnDisable change it.</summary>
        public static IReadOnlyList<BlackHole> Active => s_Active;

        public double Spin => spin;
        public float UnitsPerM => unitsPerM;
        public float SpeedOfLight => speedOfLight;
        public Cubemap Sky => sky;

        /// <summary>For presets and tests; the inspector range applies.</summary>
        internal void SetSpin(float value) => spin = Mathf.Clamp(value, 0, MaxSpin);

        // OnEnable/OnDisable always pair (destroy included), so the list stays correct even when domain
        // reload is off. Never clear it on load: with scene reload off, live holes would not re-register.
        void OnEnable() => s_Active.Add(this);
        void OnDisable() => s_Active.Remove(this);

        /// <summary>World position to Kerr-Schild coordinates in units of M.</summary>
        public Vector3d WorldToKerrSchild(Vector3 worldPos)
        {
            var local = transform.InverseTransformPoint(worldPos) / unitsPerM;
            return new Vector3d(local.x, local.z, local.y);
        }

        /// <summary>World direction to Kerr-Schild components. Length is kept, not rescaled.</summary>
        public Vector3d WorldDirToKerrSchild(Vector3 worldDir)
        {
            var local = transform.InverseTransformDirection(worldDir);
            return new Vector3d(local.x, local.z, local.y);
        }

        /// <summary>Kerr-Schild direction back to a world direction.</summary>
        public Vector3 KerrSchildDirToWorld(Vector3d d) =>
            transform.TransformDirection(new Vector3((float)d.X, (float)d.Z, (float)d.Y));
    }
}
