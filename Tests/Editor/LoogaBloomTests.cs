using System.Collections.Generic;
using LoogaSoft.PostProcessing;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaBloomTests
    {
        private const int SourceWidth = 96;
        private const int SourceHeight = 64;

        private readonly List<Object> _objects = new List<Object>();
        private LoogaBloomKernels _kernels;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are not supported.");
            _kernels = new LoogaBloomKernels(LoogaBloomFFTTests.LoadComputeShader("LoogaBloom"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in _objects)
            {
                if (item is RenderTexture target) target.Release();
                Object.DestroyImmediate(item);
            }
            _objects.Clear();
        }

        [Test]
        public void LevelCountStopsAtTheMaximumOrAtTwoTexels()
        {
            Assert.That(LoogaBloomKernels.GetLevelCount(1920, 1080, 7), Is.EqualTo(7));
            Assert.That(LoogaBloomKernels.GetLevelSize(1920, 1080, 0), Is.EqualTo(new Vector2Int(960, 540)));
            Assert.That(LoogaBloomKernels.GetLevelSize(1920, 1080, 6), Is.EqualTo(new Vector2Int(15, 9)));
            Assert.That(LoogaBloomKernels.GetLevelCount(8, 8, 8), Is.EqualTo(2));
            Assert.That(LoogaBloomKernels.GetLevelCount(3, 3, 8), Is.EqualTo(1));
        }

        [Test]
        public void UniformLightKeepsItsValueThroughThePyramid()
        {
            var color = new Color(0.5f, 0.25f, 0.125f);
            Vector4[] bloom = RunBloom(Fill(color), new LoogaBloomSettings(0f, 0.5f, 0.7f), out _);
            foreach (Vector4 texel in bloom)
            {
                Assert.That(texel.x, Is.EqualTo(color.r).Within(0.002f));
                Assert.That(texel.y, Is.EqualTo(color.g).Within(0.002f));
                Assert.That(texel.z, Is.EqualTo(color.b).Within(0.002f));
            }
        }

        [Test]
        public void KarisAverageDampsAHighlightThatOnlyPartOfTheFilterSees()
        {
            // Source texel (2o - 2, 2o - 2) is in the top-left outer group of level 0 texel o only.
            var level = new Vector2Int(12, 8);
            Color[] pixels = Fill(Color.black);
            pixels[(2 * level.y - 2) * SourceWidth + 2 * level.x - 2] = new Color(10000f, 10000f, 10000f);
            RunBloom(pixels, new LoogaBloomSettings(0f, 0.5f, 0.7f), out Vector4[] level0);
            Vector2Int size = LoogaBloomKernels.GetLevelSize(SourceWidth, SourceHeight, 0);
            float value = level0[level.y * size.x + level.x].x;
            // A plain 13-tap average gives 10000 / 4 * 0.03125 = 78 for this texel.
            Assert.That(value, Is.GreaterThan(0f));
            Assert.That(value, Is.LessThan(1f));
        }

        [Test]
        public void ABrightAreaSpreadsAndKeepsItsEnergy()
        {
            Color[] pixels = Fill(Color.black);
            for (int y = 28; y < 36; y++)
            {
                for (int x = 44; x < 52; x++) pixels[y * SourceWidth + x] = new Color(100f, 100f, 100f);
            }
            Vector4[] bloom = RunBloom(pixels, new LoogaBloomSettings(0f, 0.5f, 0.85f), out Vector4[] level0);
            Vector2Int size = LoogaBloomKernels.GetLevelSize(SourceWidth, SourceHeight, 0);
            // Each level 0 texel covers four source texels.
            float input = 64f * 100f;
            float prefiltered = 0f;
            float output = 0f;
            foreach (Vector4 texel in level0) prefiltered += texel.x * 4f;
            foreach (Vector4 texel in bloom) output += texel.x * 4f;
            // Level 0 texel 8 texels (16 source texels) from the block center.
            float far = bloom[16 * size.x + 24 + 8].x;
            TestContext.WriteLine("input " + input + " prefiltered " + prefiltered + " output " + output + " far " + far);
            // The small test texture loses some energy at its clamped borders during the upsample.
            Assert.That(output, Is.EqualTo(prefiltered).Within(prefiltered * 0.1f));
            Assert.That(prefiltered, Is.GreaterThan(input * 0.5f));
            Assert.That(far, Is.GreaterThan(0.2f));
        }

        [TestCase(0.5f, 0f)]
        [TestCase(2f, 1f)]
        public void HardThresholdKeepsOnlyTheLightAboveIt(float input, float expected)
        {
            Vector4[] bloom = RunBloom(Fill(new Color(input, input, input)), new LoogaBloomSettings(1f, 0f, 0.7f), out _);
            foreach (Vector4 texel in bloom)
            {
                Assert.That(texel.x, Is.EqualTo(expected).Within(0.002f));
            }
        }

        [Test]
        public void InvalidValuesDoNotReachTheBloom()
        {
            Color[] pixels = Fill(new Color(0.2f, 0.2f, 0.2f));
            pixels[10] = new Color(float.NaN, float.PositiveInfinity, -5f);
            Vector4[] bloom = RunBloom(pixels, new LoogaBloomSettings(0f, 0.5f, 0.7f), out _);
            foreach (Vector4 texel in bloom)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    Assert.That(float.IsFinite(texel[channel]) && texel[channel] >= 0f, Is.True);
                }
            }
        }

        private static Color[] Fill(Color color)
        {
            var pixels = new Color[SourceWidth * SourceHeight];
            for (int index = 0; index < pixels.Length; index++) pixels[index] = color;
            return pixels;
        }

        // Returns the final bloom and level 0 of the pyramid.
        private Vector4[] RunBloom(Color[] pixels, LoogaBloomSettings settings, out Vector4[] level0)
        {
            var source = new Texture2D(SourceWidth, SourceHeight, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
            _objects.Add(source);
            source.SetPixels(pixels);
            source.Apply();

            int levelCount = LoogaBloomKernels.GetLevelCount(SourceWidth, SourceHeight, LoogaBloomKernels.MaxLevels);
            var down = new RenderTargetIdentifier[levelCount];
            var up = new RenderTargetIdentifier[levelCount];
            var downTargets = new RenderTexture[levelCount];
            var upTargets = new RenderTexture[levelCount];
            for (int level = 0; level < levelCount; level++)
            {
                Vector2Int size = LoogaBloomKernels.GetLevelSize(SourceWidth, SourceHeight, level);
                downTargets[level] = CreateTarget(size);
                down[level] = downTargets[level];
                if (level < levelCount - 1)
                {
                    upTargets[level] = CreateTarget(size);
                    up[level] = upTargets[level];
                }
            }

            var commandBuffer = new CommandBuffer { name = "Looga bloom test" };
            _kernels.Dispatch(commandBuffer, source,
                new Vector2Int(SourceWidth, SourceHeight), down, up, levelCount, settings);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();

            level0 = Read(downTargets[0]);
            return Read(levelCount > 1 ? upTargets[0] : downTargets[0]);
        }

        private RenderTexture CreateTarget(Vector2Int size)
        {
            var target = new RenderTexture(size.x, size.y, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            target.Create();
            _objects.Add(target);
            return target;
        }

        private static Vector4[] Read(RenderTexture target)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            NativeArray<Vector4> data = request.GetData<Vector4>();
            return data.ToArray();
        }
    }
}
