using System.Collections.Generic;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SilkySeal.Kerr.Tests
{
    public class LensedSceneTests
    {
        // Far from anything the open scene may hold, so only this test's meshes come within reach. 2 units per M.
        static readonly Vector3 HolePosition = new Vector3(1000, 0, 0);

        readonly List<Object> _made = new List<Object>();
        LensedScene _scene;
        BlackHole _hole;

        [SetUp]
        public void SetUp()
        {
            var go = Make(new GameObject("Hole"));
            go.transform.position = HolePosition;
            _hole = go.AddComponent<BlackHole>();
            var so = new SerializedObject(_hole);
            so.FindProperty("unitsPerM").floatValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            _scene = go.AddComponent<LensedScene>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        // World (x, y, z) relative to the hole, over 2 units per M, is Kerr-Schild (x, z, y).
        [Test]
        public void Rebuild_TakesMeshesWithinRadius_InKerrSchildCoordinates()
        {
            Cube(HolePosition + new Vector3(0, 0, 40), Color.white);  // 20 M out along world z: Kerr-Schild y
            Cube(HolePosition + new Vector3(0, 0, 400), Color.white); // 200 M: beyond the default 50 M

            _scene.Rebuild();

            var surfaces = _scene.Surfaces;
            Assert.AreEqual(24, surfaces.Bvh.Vertices.Length, "one cube");
            Assert.AreEqual(36, surfaces.Bvh.Indices.Length);
            foreach (var x in surfaces.Bvh.Vertices)
            {
                Assert.AreEqual(0, x.X, 0.25 + 1e-4);
                Assert.AreEqual(20, x.Y, 0.25 + 1e-4);
                Assert.AreEqual(0, x.Z, 0.25 + 1e-4);
            }
            int facingAway = 0;
            foreach (var n in surfaces.Normals)
                if (Vector3d.Dot(n, new Vector3d(0, 1, 0)) > 0.999) facingAway++;
            Assert.AreEqual(4, facingAway, "the world +z face points along Kerr-Schild +y");
        }

        [Test]
        public void Albedo_IsTheMaterialsBaseColourInLinear()
        {
            var colour = new Color(0.5f, 0.25f, 1f);
            Cube(HolePosition + new Vector3(0, 10, 0), colour);

            _scene.Rebuild();

            var linear = colour.linear;
            foreach (var c in _scene.Surfaces.Albedo)
            {
                Assert.AreEqual(linear.r, c.X, 1e-6);
                Assert.AreEqual(linear.g, c.Y, 1e-6);
                Assert.AreEqual(linear.b, c.Z, 1e-6);
            }
        }

        // Proxies: a mesh with LODs is traced at its coarsest.
        [Test]
        public void Rebuild_UsesTheCoarsestLod()
        {
            var sphere = Make(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            sphere.transform.position = HolePosition + new Vector3(10, 0, 0);
            var filter = sphere.GetComponent<MeshFilter>();
            var mesh = Make(Object.Instantiate(filter.sharedMesh));
            MeshLodUtility.GenerateMeshLods(mesh, 4);
            Assume.That(mesh.lodCount, Is.GreaterThan(1));
            filter.sharedMesh = mesh;

            _scene.Rebuild();

            Assert.AreEqual(mesh.GetIndexCount(0, mesh.lodCount - 1), (uint)_scene.Surfaces.Bvh.Indices.Length);
            Assert.Less(_scene.Surfaces.Bvh.Indices.Length, mesh.GetIndexCount(0, 0));
        }

        [Test]
        public void Rebuild_WithNothingInRange_LeavesNoSurfaces()
        {
            Cube(HolePosition + new Vector3(0, 0, 400), Color.white);

            _scene.Rebuild();

            Assert.IsNull(_scene.Surfaces);
        }

        // A sun shining straight down is overhead: world up is the spin axis, Kerr-Schild +z.
        [Test]
        public void Light_IsTheSunInKerrSchildAxes()
        {
            var sun = Make(new GameObject("Sun")).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, 0.5f, 0.25f);
            sun.intensity = 2;
            sun.transform.rotation = Quaternion.LookRotation(Vector3.down);
            var so = new SerializedObject(_scene);
            so.FindProperty("sun").objectReferenceValue = sun;
            so.FindProperty("luminance").floatValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            var light = _scene.Light();

            Assert.AreEqual(1, light.SunDirection.Z, 1e-6);
            var expected = sun.color.linear * 2;
            Assert.AreEqual(expected.r, light.Sun.X, 1e-6);
            Assert.AreEqual(expected.g, light.Sun.Y, 1e-6);
            Assert.AreEqual(expected.b, light.Sun.Z, 1e-6);
            Assert.AreEqual(3, light.Luminance);
        }

        // The far cube is captured from the hole's centre, 2 units per M: its near face lies 145 M out along world +x and
        // a little above, where face 0 of the capture looks. The green cube in front of it is within 50 M, traced
        // instead, so the capture must see straight past it to the red one.
        [Test]
        public void Scenery_ShowsFarMeshesWhereTheyLie_AndLeavesNearOnesOut()
        {
            Cube(HolePosition + new Vector3(300, 60, 0), Color.red, "Universal Render Pipeline/Unlit").transform.localScale = 20 * Vector3.one;
            Cube(HolePosition + new Vector3(40, 8, 0), Color.green, "Universal Render Pipeline/Unlit").transform.localScale = 4 * Vector3.one;
            var so = new SerializedObject(_scene);
            so.FindProperty("scenerySize").intValue = Size;
            so.ApplyModifiedPropertiesWithoutUndo();

            _scene.Rebuild();
            _scene.CaptureScenery();

            var face = Read(_scene.Scenery, 0);
            var above = face[Texel(0.5f, 0.5f + 0.5f * 60 / 290f)];
            Assert.AreEqual(new Vector2(290, 60).magnitude / 2, above.w, 1, "distance in M to the red cube's near face");
            Assert.Greater(above.x, 0.5f, "red");
            Assert.Less(above.y, 0.1f, "not the green cube in front");
            Assert.Greater(face[Texel(0.5f, 0.5f - 0.5f * 60 / 290f)].w, 1e5f, "nothing below: the face is not upside down");
            Assert.Greater(Read(_scene.Scenery, 1)[Texel(0.5f, 0.5f)].w, 1e5f, "nothing behind");
        }

        const int Size = 64;

        static int Texel(float u, float v) => (int)(v * Size) * Size + (int)(u * Size);

        static Vector4[] Read(RenderTexture array, int face)
        {
            var request = AsyncGPUReadback.Request(array, 0, 0, Size, 0, Size, face, 1);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError);
            return request.GetData<Vector4>().ToArray();
        }

        GameObject Cube(Vector3 position, Color colour, string shader = "Universal Render Pipeline/Lit")
        {
            var go = Make(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.transform.position = position;
            var material = Make(new Material(Shader.Find(shader)));
            material.SetColor("_BaseColor", colour);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        T Make<T>(T o) where T : Object
        {
            _made.Add(o);
            return o;
        }
    }
}
