using System.Collections.Generic;
using LoogaSoft.Lighting;
using LoogaSoft.Tonemapper.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaTonemapperTests
    {
        readonly List<Volume> volumes = new List<Volume>();
        VolumeStack stack;
        LoogaTonemapper component;
        Volume volume;

        [SetUp]
        public void SetUp()
        {
            stack = VolumeManager.instance.CreateStack();
            volume = AddVolume(10000);
            component = volume.sharedProfile.Add<LoogaTonemapper>(false);
        }

        Volume AddVolume(float priority)
        {
            var go = new GameObject("Looga tonemapper test") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumes.Add(v);
            return v;
        }

        LoogaTonemapper Resolve()
        {
            VolumeManager.instance.Update(stack, volume.transform, 1 << 31);
            return stack.GetComponent<LoogaTonemapper>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var v in volumes)
            {
                var profile = v.sharedProfile;
                Object.DestroyImmediate(v.gameObject);
                foreach (var c in profile.components) Object.DestroyImmediate(c);
                Object.DestroyImmediate(profile);
            }
            volumes.Clear();
            VolumeManager.instance.DestroyStack(stack);
        }

        [Test]
        public void FreshOverrideIsNeutralAndUnchecked()
        {
            Assert.That(component.tonemapMode.value, Is.EqualTo(LoogaTonemapMode.None));
            foreach (var p in component.parameters) Assert.That(p.overrideState, Is.False);
            Assert.That(component.IsActive(), Is.False);
            Assert.That(Resolve().IsActive(), Is.False);
            Assert.That(component.preExposure.value, Is.Zero);
            Assert.That(component.postExposure.value, Is.Zero);
            Assert.That(component.blackPoint.value, Is.Zero);
            Assert.That(component.whitePoint.value, Is.EqualTo(1));
            Assert.That(component.contrast.value, Is.EqualTo(1));
            Assert.That(component.saturation.value, Is.EqualTo(1));
        }

        [Test]
        public void MissingOverrideIsInactive()
        {
            volume.sharedProfile.Remove<LoogaTonemapper>();
            Assert.That(Resolve().IsActive(), Is.False);
            Object.DestroyImmediate(component);
        }

        [TestCase(LoogaTonemapMode.AgX, 0)]
        [TestCase(LoogaTonemapMode.KhronosPBRNeutral, 1)]
        [TestCase(LoogaTonemapMode.Sigmoid, 2)]
        [TestCase(LoogaTonemapMode.ReinhardExtended, 3)]
        public void ExistingCurveIdsArePreservedAndOptIn(LoogaTonemapMode mode, int serializedId)
        {
            Assert.That((int)mode, Is.EqualTo(serializedId));
            component.tonemapMode.Override(mode);
            Assert.That(Resolve().IsActive(), Is.True);
            Assert.That(Resolve().tonemapMode.value, Is.EqualTo(mode));
        }

        [Test]
        public void UncheckedStoredCurveDoesNotActivateEffect()
        {
            component.tonemapMode.value = LoogaTonemapMode.AgX;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void NeutralModeBypassesEvenOverriddenGrading()
        {
            component.tonemapMode.Override(LoogaTonemapMode.None);
            component.preExposure.Override(3);
            component.contrast.Override(2);
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void HeaderCheckboxStopsContribution()
        {
            component.tonemapMode.Override(LoogaTonemapMode.AgX);
            Assert.That(Resolve().IsActive(), Is.True);
            component.active = false;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void ZeroVolumeWeightRestoresNeutralDefault()
        {
            component.tonemapMode.Override(LoogaTonemapMode.AgX);
            Assert.That(Resolve().IsActive(), Is.True);
            volume.weight = 0;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void HigherPriorityNoneCanSuppressLowerCurve()
        {
            component.tonemapMode.Override(LoogaTonemapMode.AgX);
            AddVolume(10001).sharedProfile.Add<LoogaTonemapper>(false).tonemapMode.Override(LoogaTonemapMode.None);
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void RendererHasNoSeparateTonemapperToggle()
        {
            Assert.That(typeof(LoogaLightingFeature).GetField("enableTonemapper"), Is.Null);
        }
    }
}
