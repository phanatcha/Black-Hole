using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Meshes near the hole, drawn through it by the lensing pass: every enabled MeshRenderer whose bounds come within
    /// <see cref="Radius"/>, at its coarsest LOD, as a diffuse surface in its material's base colour lit by a directional
    /// sun and the scene's ambient light. No textures, no shadows. The meshes are taken as they stand: call
    /// <see cref="Rebuild"/> after moving or changing them.
    /// </summary>
    // ExecuteAlways so OnDisable runs in edit mode too and frees the buffers on destroy and domain reload.
    [ExecuteAlways]
    [RequireComponent(typeof(BlackHole))]
    [DisallowMultipleComponent]
    public sealed class LensedScene : MonoBehaviour
    {
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly Vector3[] s_Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        [Tooltip("Meshes whose bounds come within this many M of the hole are traced through it.")]
        [SerializeField, Range(5, 500)] float radius = 50;

        [Tooltip("Directional light that lights them. Empty: the sun set in the Lighting window.")]
        [SerializeField] Light sun;

        [Tooltip("Luminance in cd/m² of a white surface lit by light of intensity 1, as Sky Luminance is for a sky " +
                 "texel of 1 on the Black Hole.")]
        [SerializeField, Min(0)] float luminance = 1;

        SceneSurfaces _surfaces;
        SceneBuffers _buffers;
        bool _stale = true;

        public float Radius => radius;

        /// <summary>The meshes in range, in Kerr-Schild coordinates; null when none are.</summary>
        public SceneSurfaces Surfaces
        {
            get
            {
                if (_stale) Rebuild();
                return _surfaces;
            }
        }

        /// <summary>The surfaces as the trace kernel reads them; this component owns them. Null when none are in range.</summary>
        internal SceneBuffers Buffers
        {
            get
            {
                if (_stale) Rebuild();
                return _buffers;
            }
        }

        void OnEnable() => _stale = true;
        void OnValidate() => _stale = true;

        void OnDisable()
        {
            Release();
            _stale = true;
        }

        /// <summary>Collects the meshes in range again. Cost O(n log n) in their triangles, and a GPU upload.</summary>
        public void Rebuild()
        {
            Release();
            _stale = false;
            var hole = GetComponent<BlackHole>();
            var vertices = new List<Vector3d>();
            var normals = new List<Vector3d>();
            var albedo = new List<Vector3d>();
            var indices = new List<int>();
            var meshPositions = new List<Vector3>();
            var meshNormals = new List<Vector3>();
            var materials = new List<Material>();

            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                if (hole.WorldToKerrSchild(renderer.bounds.ClosestPoint(transform.position)).Length > radius) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable || !mesh.HasVertexAttribute(VertexAttribute.Normal))
                {
                    Debug.LogWarning($"{nameof(LensedScene)}: '{mesh.name}' needs Read/Write and normals to be lensed; skipped.", renderer);
                    continue;
                }
                renderer.GetSharedMaterials(materials);
                if (materials.Count == 0) continue;

                mesh.GetVertices(meshPositions);
                mesh.GetNormals(meshNormals);
                var toWorld = renderer.localToWorldMatrix;
                var normalToWorld = toWorld.inverse.transpose;
                int lod = Mathf.Max(0, mesh.lodCount - 1);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var colour = BaseColour(materials[Mathf.Min(sub, materials.Count - 1)]);
                    // shortcut: every submesh copies all the mesh's vertices; index only the ones it uses if
                    // many-material meshes make the buffers large.
                    int first = vertices.Count;
                    for (int i = 0; i < meshPositions.Count; i++)
                    {
                        vertices.Add(hole.WorldToKerrSchild(toWorld.MultiplyPoint3x4(meshPositions[i])));
                        normals.Add(hole.WorldDirToKerrSchild(normalToWorld.MultiplyVector(meshNormals[i])).Normalized);
                        albedo.Add(colour);
                    }
                    foreach (int index in mesh.GetTriangles(sub, lod, true)) indices.Add(first + index);
                }
            }

            if (indices.Count == 0) return;
            _surfaces = new SceneSurfaces(vertices.ToArray(), indices.ToArray(), normals.ToArray(), albedo.ToArray());
            _buffers = SceneBuffers.Create(_surfaces);
        }

        /// <summary>
        /// The sun (black if it is missing, off or not directional) and the ambient light, in Kerr-Schild axes.
        /// shortcut: ambient is the probe's average over all directions; aim it by normal if dark undersides matter.
        /// </summary>
        public SceneLight Light()
        {
            var hole = GetComponent<BlackHole>();
            var light = sun != null ? sun : RenderSettings.sun;
            bool lit = light != null && light.isActiveAndEnabled && light.type == LightType.Directional;
            var direct = lit ? light.color.linear * light.intensity : Color.black;

            // The six axes cancel every band of the probe but the constant one.
            var ambient = new Color[s_Axes.Length];
            RenderSettings.ambientProbe.Evaluate(s_Axes, ambient);
            var average = Color.black;
            foreach (var c in ambient) average += c / s_Axes.Length;

            return new SceneLight
            {
                SunDirection = lit ? hole.WorldDirToKerrSchild(-light.transform.forward).Normalized : new Vector3d(0, 0, 1),
                Sun = ToVector(direct),
                Ambient = ToVector(average),
                Luminance = luminance,
            };
        }

        static Vector3d BaseColour(Material material)
        {
            var c = material == null ? Color.white
                  : material.HasColor(s_BaseColor) ? material.GetColor(s_BaseColor)
                  : material.HasColor(s_Color) ? material.GetColor(s_Color) : Color.white;
            return ToVector(c.linear);
        }

        static Vector3d ToVector(Color c) => new Vector3d(c.r, c.g, c.b);

        void Release()
        {
            _buffers?.Dispose();
            _buffers = null;
            _surfaces = null;
        }
    }
}
