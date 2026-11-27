using System;
using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// <see cref="SceneSurfaces"/> as the trace kernel reads them (Scene.hlsl): the tree's nodes, then per leaf slot the
    /// triangle as a corner and two edges, then its three normals and three albedos. Float, Kerr-Schild coordinates in M.
    /// </summary>
    public sealed class SceneBuffers : IDisposable
    {
        public const int NodeStride = 2, TriangleStride = 3, ShadingStride = 6;

        internal static readonly int s_Nodes = Shader.PropertyToID("_SceneNodes");
        internal static readonly int s_Triangles = Shader.PropertyToID("_SceneTriangles");
        internal static readonly int s_Shading = Shader.PropertyToID("_SceneShading");
        internal static readonly int s_Enabled = Shader.PropertyToID("_SceneEnabled");
        internal static readonly int s_SunDirection = Shader.PropertyToID("_SceneSunDirection");
        internal static readonly int s_Sun = Shader.PropertyToID("_SceneSun");
        internal static readonly int s_Ambient = Shader.PropertyToID("_SceneAmbient");

        public readonly GraphicsBuffer Nodes, Triangles, Shading;

        SceneBuffers(Vector4[] nodes, Vector4[] triangles, Vector4[] shading)
        {
            Nodes = Upload("Kerr Scene Nodes", nodes);
            Triangles = Upload("Kerr Scene Triangles", triangles);
            Shading = Upload("Kerr Scene Shading", shading);
        }

        /// <summary>One placeholder of each: the trace kernel declares the buffers, and every declared buffer must be bound.</summary>
        public static SceneBuffers Empty() =>
            new SceneBuffers(new Vector4[NodeStride], new Vector4[TriangleStride], new Vector4[ShadingStride]);

        public static SceneBuffers Create(SceneSurfaces surfaces)
        {
            var bvh = surfaces.Bvh;
            var nodes = new Vector4[NodeStride * bvh.Nodes.Length];
            for (int i = 0; i < bvh.Nodes.Length; i++)
            {
                var n = bvh.Nodes[i];
                nodes[2 * i] = new Vector4((float)n.Min.X, (float)n.Min.Y, (float)n.Min.Z, n.First);
                nodes[2 * i + 1] = new Vector4((float)n.Max.X, (float)n.Max.Y, (float)n.Max.Z, n.Count);
            }
            int count = bvh.Order.Length;
            var triangles = new Vector4[TriangleStride * count];
            var shading = new Vector4[ShadingStride * count];
            for (int slot = 0; slot < count; slot++)
            {
                int t = bvh.Order[slot];
                int i0 = bvh.Indices[3 * t], i1 = bvh.Indices[3 * t + 1], i2 = bvh.Indices[3 * t + 2];
                var v0 = bvh.Vertices[i0];
                triangles[3 * slot] = ToFloat(v0, t);
                triangles[3 * slot + 1] = ToFloat(bvh.Vertices[i1] - v0, 0);
                triangles[3 * slot + 2] = ToFloat(bvh.Vertices[i2] - v0, 0);
                shading[6 * slot] = ToFloat(surfaces.Normals[i0], 0);
                shading[6 * slot + 1] = ToFloat(surfaces.Normals[i1], 0);
                shading[6 * slot + 2] = ToFloat(surfaces.Normals[i2], 0);
                shading[6 * slot + 3] = ToFloat(surfaces.Albedo[i0], 0);
                shading[6 * slot + 4] = ToFloat(surfaces.Albedo[i1], 0);
                shading[6 * slot + 5] = ToFloat(surfaces.Albedo[i2], 0);
            }
            return new SceneBuffers(nodes, triangles, shading);
        }

        public void Bind(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, s_Nodes, Nodes);
            shader.SetBuffer(kernel, s_Triangles, Triangles);
            shader.SetBuffer(kernel, s_Shading, Shading);
        }

        /// <summary>The scene's light as the kernel takes it, luminance folded in. Directions in Kerr-Schild axes.</summary>
        public static void SetLight(ComputeShader shader, in SceneLight light)
        {
            Light(light, out var direction, out var sun, out var ambient);
            shader.SetVector(s_SunDirection, direction);
            shader.SetVector(s_Sun, sun);
            shader.SetVector(s_Ambient, ambient);
        }

        internal static void Light(in SceneLight light, out Vector4 direction, out Vector4 sun, out Vector4 ambient)
        {
            direction = ToFloat(light.SunDirection, 0);
            sun = ToFloat(light.Sun * light.Luminance, 0);
            ambient = ToFloat(light.Ambient * light.Luminance, 0);
        }

        public void Dispose()
        {
            Nodes.Release();
            Triangles.Release();
            Shading.Release();
        }

        static GraphicsBuffer Upload(string name, Vector4[] data)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 4 * sizeof(float)) { name = name };
            buffer.SetData(data);
            return buffer;
        }

        static Vector4 ToFloat(Vector3d v, float w) => new Vector4((float)v.X, (float)v.Y, (float)v.Z, w);
    }
}
