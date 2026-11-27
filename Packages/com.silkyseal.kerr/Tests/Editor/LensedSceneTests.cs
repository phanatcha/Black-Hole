using System.Collections.Generic;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;

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

        void Cube(Vector3 position, Color colour)
        {
            var go = Make(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.transform.position = position;
            var material = Make(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            material.SetColor("_BaseColor", colour);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        T Make<T>(T o) where T : Object
        {
            _made.Add(o);
            return o;
        }
    }
}
