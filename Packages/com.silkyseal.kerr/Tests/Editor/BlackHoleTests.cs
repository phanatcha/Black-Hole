using System.Reflection;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    public class BlackHoleTests
    {
        GameObject _go;
        BlackHole _hole;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Hole");
            _hole = _go.AddComponent<BlackHole>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void CoordinateTime_RunsAtTheSpeedOfLightInM()
        {
            // Defaults: light covers 30 units a second and an M is 1 unit, so 2 s is 60 M.
            Assert.AreEqual(60, _hole.CoordinateTime(2), 1e-9);
        }

        [Test]
        public void Active_TracksEnableAndDisable()
        {
            CollectionAssert.Contains(BlackHole.Active, _hole);
            _hole.enabled = false;
            CollectionAssert.DoesNotContain(BlackHole.Active, _hole);
        }

        // Entering Play mode with scene reload off does not re-run OnEnable, so no load-time hook may
        // drop a hole that is still enabled.
        [Test]
        public void Active_KeepsEnabledHole_AfterRuntimeInitializeHooks()
        {
            foreach (var method in typeof(BlackHole).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null)
                    method.Invoke(null, null);

            CollectionAssert.Contains(BlackHole.Active, _hole);
        }
    }
}
