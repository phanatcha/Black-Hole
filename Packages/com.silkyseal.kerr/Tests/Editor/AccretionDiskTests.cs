using System;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SilkySeal.Kerr.Tests
{
    public class AccretionDiskTests
    {
        GameObject _go;
        BlackHole _hole;
        AccretionDisk _disk;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Hole");
            _hole = _go.AddComponent<BlackHole>();
            _disk = _go.AddComponent<AccretionDisk>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Table_StartsAtTheIscoOfTheHolesCurrentSpin()
        {
            Assert.IsNotNull(_disk.FluxTable(out var range));
            Assert.AreEqual(CircularOrbit.IscoRadius(_hole.Spin), range.x, 1e-5);

            _hole.SetSpin(0);
            _disk.FluxTable(out range);
            Assert.AreEqual(6, range.x, 1e-5);
        }

        [Test]
        public void Table_IsBakedOnce_UntilSomethingChanges()
        {
            var first = _disk.FluxTable(out _);
            var second = _disk.FluxTable(out _);
            Assert.AreSame(first, second);

            _hole.SetSpin(0.9f);
            var third = _disk.FluxTable(out _);
            Assert.AreNotSame(first, third);
            Assert.IsTrue(first == null, "the replaced table should be destroyed");
        }

        [Test]
        public void DestroyingTheDisk_InEditMode_ReleasesItsTable()
        {
            var table = _disk.FluxTable(out _);
            Object.DestroyImmediate(_disk);
            Assert.IsTrue(table == null, "the flux table outlived its disk");
        }

        [Test]
        public void InterstellarPreset_IsGargantua()
        {
            _disk.ApplyInterstellarPreset();

            Assert.AreEqual(0.6, _hole.Spin, 1e-6);
            Assert.IsFalse(_disk.Shifts);
            _disk.FluxTable(out var range);
            Assert.AreEqual(4500, range.w, 4500 * 1e-4);
        }

        [Test]
        public void RealisticPreset_TakesTheTemperatureFromMassAndAccretionRate()
        {
            _disk.ApplyRealisticPreset();

            Assert.IsTrue(_disk.Shifts);
            var reference = ThinDisk.FromAccretion(_hole.Spin, _disk.OuterRadius, _disk.MassSolar, _disk.EddingtonRatio);
            double peak = 0;
            for (int i = 0; i <= 10000; i++)
                peak = Math.Max(peak, reference.Temperature(reference.InnerRadius + (reference.OuterRadius - reference.InnerRadius) * i / 10000.0));
            _disk.FluxTable(out var range);
            Assert.AreEqual(peak, range.w, peak * 1e-3);
        }
    }
}
