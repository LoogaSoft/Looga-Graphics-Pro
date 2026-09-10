using NUnit.Framework;
using UnityEngine;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaLightAttenuationTests
    {
        private GameObject _gameObject;
        private LoogaLightAttenuation _attenuation;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("Looga light attenuation test");
            _attenuation = _gameObject.AddComponent<LoogaLightAttenuation>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void NewComponentUsesUrpDefault()
        {
            Assert.That(
                _attenuation.Mode,
                Is.EqualTo(LoogaLightAttenuationMode.UrpDefault));
            Assert.That(_gameObject.GetComponent<Light>(), Is.Not.Null);
        }

        [Test]
        public void LinearModeUsesNormalizedDistance()
        {
            _attenuation.SetMode(LoogaLightAttenuationMode.Linear);

            Assert.That(_attenuation.EvaluateDistance(0f, 10f), Is.EqualTo(1f));
            Assert.That(_attenuation.EvaluateDistance(5f, 10f), Is.EqualTo(0.5f));
            Assert.That(_attenuation.EvaluateDistance(10f, 10f), Is.Zero);
        }

        [Test]
        public void QuadraticModeSquaresRemainingDistance()
        {
            _attenuation.SetMode(LoogaLightAttenuationMode.Quadratic);

            Assert.That(_attenuation.EvaluateDistance(5f, 10f), Is.EqualTo(0.25f));
        }

        [Test]
        public void PhysicalModeClampsDistanceToSourceRadius()
        {
            _attenuation.SetMode(LoogaLightAttenuationMode.Physical);
            _attenuation.SetParameters(0.8f, 0.5f, 2f);

            Assert.That(_attenuation.EvaluateDistance(0f, 10f), Is.EqualTo(4f));
            Assert.That(_attenuation.EvaluateDistance(0.25f, 10f), Is.EqualTo(4f));
        }

        [Test]
        public void SoftPhysicalModeFadesToZeroAtRange()
        {
            _attenuation.SetMode(LoogaLightAttenuationMode.SoftPhysical);
            _attenuation.SetParameters(0.5f, 0.1f, 2f);

            Assert.That(_attenuation.EvaluateDistance(5f, 10f), Is.GreaterThan(0f));
            Assert.That(_attenuation.EvaluateDistance(10f, 10f), Is.Zero);
        }

        [Test]
        public void CustomCurveUsesNormalizedDistance()
        {
            _attenuation.SetMode(LoogaLightAttenuationMode.CustomCurve);
            _attenuation.SetCustomCurve(AnimationCurve.Linear(0f, 1f, 1f, 0f));

            Assert.That(
                _attenuation.EvaluateDistance(2.5f, 10f),
                Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void ParametersClampToSupportedValues()
        {
            _attenuation.SetParameters(2f, 0f, 20f);

            Assert.That(_attenuation.RangeFadeStart, Is.EqualTo(0.99f));
            Assert.That(_attenuation.SourceRadius, Is.EqualTo(0.001f));
            Assert.That(_attenuation.FalloffExponent, Is.EqualTo(8f));
        }
    }
}
