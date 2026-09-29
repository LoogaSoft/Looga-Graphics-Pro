using System.Collections.Generic;
using LoogaSoft.PostProcessing;
using LoogaSoft.PostProcessing.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
#pragma warning disable CS0618 // The migration tests create the obsolete LoogaTonemapper on purpose.
using LegacyTonemapper = LoogaSoft.Tonemapper.Runtime.LoogaTonemapper;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaPostProcessingTests
    {
        private readonly List<Volume> _volumes = new List<Volume>();
        private VolumeStack _stack;
        private LoogaPostProcessing _component;
        private Volume _volume;

        [SetUp]
        public void SetUp()
        {
            _stack = VolumeManager.instance.CreateStack();
            _volume = AddVolume(10000);
            _component = _volume.sharedProfile.Add<LoogaPostProcessing>(false);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Volume volume in _volumes)
            {
                VolumeProfile profile = volume.sharedProfile;
                Object.DestroyImmediate(volume.gameObject);
                foreach (VolumeComponent component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
            }
            _volumes.Clear();
            VolumeManager.instance.DestroyStack(_stack);
        }

        [Test]
        public void FreshOverrideIsNeutralAndUnchecked()
        {
            foreach (VolumeParameter parameter in _component.parameters) Assert.That(parameter.overrideState, Is.False);
            Assert.That(_component.tonemapMode.value, Is.EqualTo(LoogaTonemapMode.None));
            Assert.That(_component.bloomIntensity.value, Is.Zero);
            Assert.That(_component.IsActive(), Is.False);
            Assert.That(Resolve().IsActive(), Is.False);
            Assert.That(_component.preExposure.value, Is.Zero);
            Assert.That(_component.whitePoint.value, Is.EqualTo(1));
            Assert.That(_component.bloomThreshold.value, Is.Zero);
            Assert.That(_component.bloomScatter.value, Is.EqualTo(0.7f));
        }

        [Test]
        public void MissingOverrideIsInactive()
        {
            _volume.sharedProfile.Remove<LoogaPostProcessing>();
            Assert.That(Resolve().IsActive(), Is.False);
            Object.DestroyImmediate(_component);
        }

        [TestCase(LoogaTonemapMode.AgX, 0)]
        [TestCase(LoogaTonemapMode.KhronosPBRNeutral, 1)]
        [TestCase(LoogaTonemapMode.Sigmoid, 2)]
        [TestCase(LoogaTonemapMode.ReinhardExtended, 3)]
        public void ExistingCurveIdsArePreservedAndOptIn(LoogaTonemapMode mode, int serializedId)
        {
            Assert.That((int)mode, Is.EqualTo(serializedId));
            _component.tonemapMode.Override(mode);
            Assert.That(Resolve().IsTonemapActive(), Is.True);
            Assert.That(Resolve().IsBloomActive(), Is.False);
            Assert.That(Resolve().tonemapMode.value, Is.EqualTo(mode));
        }

        [Test]
        public void UncheckedStoredValuesDoNotActivateEffects()
        {
            _component.tonemapMode.value = LoogaTonemapMode.AgX;
            _component.bloomIntensity.value = 0.2f;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void NeutralModeBypassesEvenOverriddenGrading()
        {
            _component.tonemapMode.Override(LoogaTonemapMode.None);
            _component.preExposure.Override(3);
            _component.contrast.Override(2);
            Assert.That(Resolve().IsTonemapActive(), Is.False);
        }

        [Test]
        public void BloomIsOptInThroughIntensity()
        {
            _component.bloomIntensity.Override(0.1f);
            LoogaPostProcessing resolved = Resolve();
            Assert.That(resolved.IsBloomActive(), Is.True);
            Assert.That(resolved.IsTonemapActive(), Is.False);
            Assert.That(resolved.IsActive(), Is.True);
        }

        [Test]
        public void PreExposureScaleAppliesOnlyWithTonemapping()
        {
            _component.preExposure.Override(-1f);
            Assert.That(Resolve().PreExposureScale, Is.EqualTo(1f));
            _component.tonemapMode.Override(LoogaTonemapMode.Sigmoid);
            Assert.That(Resolve().PreExposureScale, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void HeaderCheckboxStopsContribution()
        {
            _component.tonemapMode.Override(LoogaTonemapMode.AgX);
            _component.bloomIntensity.Override(0.1f);
            Assert.That(Resolve().IsActive(), Is.True);
            _component.active = false;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void ZeroVolumeWeightRestoresNeutralDefault()
        {
            _component.tonemapMode.Override(LoogaTonemapMode.AgX);
            _component.bloomIntensity.Override(0.1f);
            Assert.That(Resolve().IsActive(), Is.True);
            _volume.weight = 0;
            Assert.That(Resolve().IsActive(), Is.False);
        }

        [Test]
        public void HigherPriorityNoneCanSuppressLowerCurve()
        {
            _component.tonemapMode.Override(LoogaTonemapMode.AgX);
            AddVolume(10001).sharedProfile.Add<LoogaPostProcessing>(false).tonemapMode.Override(LoogaTonemapMode.None);
            Assert.That(Resolve().IsTonemapActive(), Is.False);
        }

        [Test]
        public void LightingFeatureNoLongerOwnsTonemapping()
        {
            Assert.That(typeof(LoogaLightingFeature).GetField("tonemapperShader"), Is.Null);
            Assert.That(typeof(LoogaLightingFeature).GetField("enableTonemapper"), Is.Null);
        }

        [Test]
        public void MigrationMovesTonemapperValuesAndRemovesTheLegacyOverride()
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var legacy = profile.Add<LegacyTonemapper>(false);
            legacy.tonemapMode.Override(LoogaTonemapMode.Sigmoid);
            legacy.preExposure.Override(-0.65f);
            legacy.sigmoidCurve.Override(1.9f);
            legacy.saturation.value = 0.8f;
            try
            {
                Assert.That(LoogaPostProcessingMigration.MigrateProfile(profile), Is.True);
                Assert.That(profile.components.Exists(component => component is LegacyTonemapper), Is.False);
                Assert.That(profile.TryGet(out LoogaPostProcessing settings), Is.True);
                Assert.That(settings.tonemapMode.value, Is.EqualTo(LoogaTonemapMode.Sigmoid));
                Assert.That(settings.tonemapMode.overrideState, Is.True);
                Assert.That(settings.preExposure.value, Is.EqualTo(-0.65f));
                Assert.That(settings.sigmoidCurve.value, Is.EqualTo(1.9f));
                Assert.That(settings.saturation.value, Is.EqualTo(0.8f));
                Assert.That(settings.saturation.overrideState, Is.False);
                Assert.That(settings.bloomIntensity.overrideState, Is.False);
                Assert.That(LoogaPostProcessingMigration.MigrateProfile(profile), Is.False);
            }
            finally
            {
                foreach (VolumeComponent component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
            }
        }

        private Volume AddVolume(float priority)
        {
            var gameObject = new GameObject("Looga post processing test") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volumes.Add(volume);
            return volume;
        }

        private LoogaPostProcessing Resolve()
        {
            VolumeManager.instance.Update(_stack, _volume.transform, 1 << 31);
            return _stack.GetComponent<LoogaPostProcessing>();
        }
    }
}
