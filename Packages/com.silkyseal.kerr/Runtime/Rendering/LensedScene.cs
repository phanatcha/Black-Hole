using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// The scene as the hole bends it. Meshes near the hole (every enabled MeshRenderer whose bounds come within
    /// <see cref="Radius"/>) are traced: at their coarsest LOD, as diffuse surfaces in their material's base colour lit
    /// by a directional sun and the ambient light, with no textures or shadows. Everything farther out, to
    /// <see cref="SceneryRadius"/>, is rendered as usual into a colour and distance map around the hole's centre, one
    /// face a frame, and traced against that. The near meshes are taken as they stand: call <see cref="Rebuild"/> after
    /// moving or changing them.
    /// </summary>
    // ExecuteAlways so OnDisable runs in edit mode too and frees the buffers on destroy and domain reload.
    [ExecuteAlways]
    [RequireComponent(typeof(BlackHole))]
    [DisallowMultipleComponent]
    public sealed class LensedScene : MonoBehaviour
    {
        /// <summary>Forward and up of each capture face in the hole's local axes. Scene.hlsl picks faces in this order.</summary>
        internal static readonly (Vector3 forward, Vector3 up)[] Faces =
        {
            (Vector3.right, Vector3.up), (Vector3.left, Vector3.up), (Vector3.up, Vector3.back),
            (Vector3.down, Vector3.forward), (Vector3.forward, Vector3.up), (Vector3.back, Vector3.up),
        };

        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly Vector3[] s_Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        [Tooltip("Meshes whose bounds come within this many M of the hole are traced through it.")]
        [SerializeField, Range(5, 500)] float radius = 50;

        [Tooltip("Farthest scenery, in M, that shows through the hole. It is rendered from the hole's centre, so what " +
                 "that point cannot see (the back of a wall, say) shows the sky instead.")]
        [SerializeField, Range(100, 20000)] float sceneryRadius = 2000;

        [Tooltip("Texels along each face of the scenery map. 0 turns the scenery off: only the near meshes and the sky show.")]
        [SerializeField, Range(0, 2048)] int scenerySize = 512;

        [Tooltip("Directional light that lights the near meshes. Empty: the sun set in the Lighting window.")]
        [SerializeField] Light sun;

        [Tooltip("Luminance in cd/m² of a white surface lit by light of intensity 1, as Sky Luminance is for a sky " +
                 "texel of 1 on the Black Hole.")]
        [SerializeField, Min(0)] float luminance = 1;

        SceneSurfaces _surfaces;
        SceneBuffers _buffers;
        readonly List<MeshRenderer> _traced = new List<MeshRenderer>();
        bool _stale = true;

        Camera _camera;
        RenderTexture _scenery, _faceTarget;
        int _nextFace, _facesCaptured;

        /// <summary>The scene whose capture camera is rendering now, for KerrRendererFeature; null between captures.</summary>
        internal static LensedScene Capturing { get; private set; }

        public float Radius => radius;
        public float SceneryRadius => sceneryRadius;
        public float Luminance => luminance;

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

        /// <summary>
        /// Six faces (<see cref="Faces"/>) of float4 (linear colour, distance in M from the hole's centre), 1e6 where
        /// nothing was drawn. Null when the scenery is off or not captured yet.
        /// </summary>
        public RenderTexture Scenery => scenerySize > 0 && _facesCaptured == Faces.Length ? _scenery : null;

        /// <summary>The map the capture pass writes, captured or not.</summary>
        internal RenderTexture SceneryTarget => _scenery;

        /// <summary>The camera that captures the scenery; it renders only on request.</summary>
        internal Camera CaptureCamera => _camera;

        /// <summary>The capture face being rendered, and M per world unit, for the capture pass.</summary>
        internal int CaptureFace { get; private set; }
        internal float CaptureToM { get; private set; }

        void OnEnable() => _stale = true;
        void OnValidate() => _stale = true;

        void OnDisable()
        {
            Release();
            ReleaseScenery();
            _stale = true;
        }

        // Each frame in Play mode one face, so the scenery lags its own motion by at most six frames; all six at once in
        // edit mode, where Update runs only when something changes.
        void Update()
        {
            if (scenerySize == 0) return;
            if (Application.isPlaying && _facesCaptured == Faces.Length) Capture(_nextFace++ % Faces.Length);
            else CaptureScenery();
        }

        /// <summary>Collects the near meshes again. Cost O(n log n) in their triangles, and a GPU upload.</summary>
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
                // In range, so the scenery capture leaves it out even when it cannot be traced: never drawn twice.
                _traced.Add(renderer);
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

        /// <summary>Renders all six faces of the scenery map now. Must not be called while a camera is rendering.</summary>
        public void CaptureScenery()
        {
            if (scenerySize == 0) return;
            for (int face = 0; face < Faces.Length; face++) Capture(face);
        }

        void Capture(int face)
        {
            if (_stale) Rebuild();
            EnsureCapture();
            var hole = GetComponent<BlackHole>();
            var (forward, up) = Faces[face];
            _camera.transform.SetPositionAndRotation(transform.position, transform.rotation * Quaternion.LookRotation(forward, up));
            float unitsPerM = hole.UnitsPerM * transform.lossyScale.x;
            // Everything captured lies beyond the radius, so no nearer than radius / √3 along a face's axis.
            _camera.nearClipPlane = 0.99f * radius * unitsPerM / Mathf.Sqrt(3);
            _camera.farClipPlane = sceneryRadius * unitsPerM;
            CaptureFace = face;
            CaptureToM = 1 / unitsPerM;

            var hidden = new List<MeshRenderer>(_traced.Count);
            foreach (var r in _traced)
                if (r != null && !r.forceRenderingOff)
                {
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
            Capturing = this;
            try
            {
                RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _faceTarget });
            }
            finally
            {
                Capturing = null;
                foreach (var r in hidden) r.forceRenderingOff = false;
            }
            _facesCaptured = Mathf.Min(_facesCaptured + 1, Faces.Length);
        }

        void EnsureCapture()
        {
            if (_scenery != null && _scenery.width == scenerySize) return;
            ReleaseScenery();
            _scenery = new RenderTexture(scenerySize, scenerySize, GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormat.None)
            {
                name = "Kerr Scenery", dimension = TextureDimension.Tex2DArray, volumeDepth = Faces.Length,
                enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            _scenery.Create();
            _faceTarget = new RenderTexture(scenerySize, scenerySize, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.D32_SFloat)
            {
                name = "Kerr Scenery Face",
            };
            _faceTarget.Create();

            var go = new GameObject("Kerr Scenery Capture") { hideFlags = HideFlags.HideAndDontSave };
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 90;
            _camera.aspect = 1;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.allowMSAA = false;
            _camera.allowHDR = true;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
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
            _traced.Clear();
        }

        void ReleaseScenery()
        {
            if (_camera != null) CoreUtils.Destroy(_camera.gameObject);
            _camera = null;
            CoreUtils.Destroy(_scenery);
            CoreUtils.Destroy(_faceTarget);
            _scenery = _faceTarget = null;
            _facesCaptured = 0;
        }
    }
}
