using System.Collections.Generic;
using System.IO;
using LoogaSoft.PostProcessing;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaBloomFFTTests
    {
        private const int Size = 256;
        private const float PointValue = 1000f;

        private readonly List<Object> _objects = new List<Object>();
        private LoogaBloomFFT _fft;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are not supported.");
            _fft = new LoogaBloomFFT(LoadComputeShader("LoogaBloomFFT"));
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

        [TestCase(256, 0.1f)]
        [TestCase(512, 1f)]
        [TestCase(1024, 2f)]
        public void ImageAndKernelFitInTheBuffer(int size, float kernelSize)
        {
            Vector2Int region = LoogaBloomFFT.GetRegionSize(1920, 1080, size, kernelSize);
            float radius = LoogaBloomFFT.GetKernelRadius(size, kernelSize);
            Assert.That(region.x + radius, Is.LessThanOrEqualTo(size));
            Assert.That(region.y, Is.EqualTo(Mathf.RoundToInt(region.x * 1080f / 1920f)));
            Assert.That(LoogaBloomFFT.GetRegionSize(1080, 1920, size, kernelSize), Is.EqualTo(new Vector2Int(region.y, region.x)));
        }

        [Test]
        public void BoxKernelSpreadsAPointEvenlyAroundItself()
        {
            const float kernelSize = 0.2f;
            int extent = LoogaBloomFFT.GetImageExtent(Size, kernelSize);
            int radius = Mathf.FloorToInt(LoogaBloomFFT.GetKernelRadius(Size, kernelSize));
            var point = new Vector2Int(100, 90);
            Vector4[] output = Run(new Vector2Int(extent, extent), point, new Color(PointValue, 0f, 0f),
                kernelSize, KernelTexture(Color.white, Color.white));

            float expected = PointValue / ((2 * radius + 1) * (2 * radius + 1));
            Assert.That(Texel(output, point).x, Is.EqualTo(expected).Within(expected * 0.01f));
            Assert.That(Texel(output, point + new Vector2Int(radius - 1, 1 - radius)).x, Is.EqualTo(expected).Within(expected * 0.01f));
            Assert.That(Texel(output, point + new Vector2Int(radius + 2, 0)).x, Is.EqualTo(0f).Within(expected * 0.01f));
            Assert.That(Texel(output, point + new Vector2Int(0, -radius - 2)).x, Is.EqualTo(0f).Within(expected * 0.01f));
            // Light stays in its channel.
            Assert.That(Sum(output, 1), Is.EqualTo(0f).Within(PointValue * 1e-4f));
            Assert.That(Sum(output, 2), Is.EqualTo(0f).Within(PointValue * 1e-4f));
            Assert.That(Sum(output, 0), Is.EqualTo(PointValue).Within(PointValue * 1e-3f));
        }

        [Test]
        public void ColoredKernelSpreadsEachChannelWithItsOwnShapeAndKeepsItsEnergy()
        {
            const float kernelSize = 0.2f;
            int extent = LoogaBloomFFT.GetImageExtent(Size, kernelSize);
            int radius = Mathf.FloorToInt(LoogaBloomFFT.GetKernelRadius(Size, kernelSize));
            var point = new Vector2Int(100, 90);
            // Red and blue fill the kernel. Green fills only its center half.
            Vector4[] output = Run(new Vector2Int(extent, extent), point, new Color(PointValue, PointValue, PointValue),
                kernelSize, KernelTexture(new Color(1f, 0f, 1f), Color.white));

            Vector4 near = Texel(output, point + new Vector2Int(2, 0));
            Vector4 far = Texel(output, point + new Vector2Int(radius - 2, 0));
            Assert.That(near.y, Is.GreaterThan(near.x * 2f));
            Assert.That(far.x, Is.GreaterThan(0f));
            Assert.That(far.y, Is.EqualTo(0f).Within(far.x * 0.01f));
            Assert.That(far.z, Is.EqualTo(far.x).Within(far.x * 0.01f));
            for (int channel = 0; channel < 3; channel++)
            {
                Assert.That(Sum(output, channel), Is.EqualTo(PointValue).Within(PointValue * 1e-3f));
            }
        }

        [Test]
        public void DefaultKernelKeepsEnergyAndDoesNotWrapAround()
        {
            const float kernelSize = 1f;
            Vector2Int region = LoogaBloomFFT.GetRegionSize(1920, 1080, Size, kernelSize);
            float radius = LoogaBloomFFT.GetKernelRadius(Size, kernelSize);
            Vector4[] output = Run(region, Vector2Int.zero, new Color(PointValue, PointValue, PointValue), kernelSize, null);

            Vector4 peak = Texel(output, Vector2Int.zero);
            Assert.That(Sum(output, 0), Is.EqualTo(PointValue).Within(PointValue * 1e-3f));
            Assert.That(peak.x, Is.GreaterThan(Texel(output, new Vector2Int(4, 0)).x));
            Assert.That(Texel(output, new Vector2Int(8, 8)).x, Is.GreaterThan(0f));
            // The far image corner is beyond the kernel radius. Light that wraps around the buffer would reach it.
            Assert.That(region.x - 1, Is.GreaterThan(radius));
            Assert.That(Texel(output, new Vector2Int(region.x - 1, region.y - 1)).x, Is.EqualTo(0f).Within(peak.x * 1e-4f));
        }

        private Vector4[] Run(Vector2Int region, Vector2Int point, Color value, float kernelSize, Texture kernel)
        {
            var image = new Texture2D(region.x, region.y, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _objects.Add(image);
            var pixels = new Color[region.x * region.y];
            pixels[point.y * region.x + point.x] = value;
            image.SetPixels(pixels);
            image.Apply();

            RenderTexture kernelSpectrum = CreateBuffer();
            RenderTexture buffer = CreateBuffer();
            RenderTexture output = CreateBuffer();
            var commandBuffer = new CommandBuffer { name = "Looga FFT bloom test" };
            _fft.BuildKernelSpectrum(commandBuffer, kernelSpectrum, Size, kernelSize, kernel);
            _fft.Convolve(commandBuffer, image, region, buffer, output, kernelSpectrum, Size);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            NativeArray<Vector4> data = request.GetData<Vector4>();
            return data.ToArray();
        }

        // A 4 x 4 kernel. The outer ring uses the outer color, the center 2 x 2 texels use the inner color.
        private Texture2D KernelTexture(Color outer, Color inner)
        {
            var texture = new Texture2D(4, 4, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _objects.Add(texture);
            var pixels = new Color[16];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool center = x is 1 or 2 && y is 1 or 2;
                    pixels[y * 4 + x] = center ? inner : outer;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private RenderTexture CreateBuffer()
        {
            var target = new RenderTexture(Size, Size, 0, LoogaBloomFFT.BufferFormat) { enableRandomWrite = true };
            target.Create();
            _objects.Add(target);
            return target;
        }

        private static Vector4 Texel(Vector4[] data, Vector2Int texel)
        {
            return data[texel.y * Size + texel.x];
        }

        private static float Sum(Vector4[] data, int channel)
        {
            double sum = 0.0;
            foreach (Vector4 texel in data) sum += texel[channel];
            return (float)sum;
        }

        internal static ComputeShader LoadComputeShader(string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:ComputeShader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == fileName)
                {
                    return AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
            }
            Assert.Fail(fileName + ".compute is missing.");
            return null;
        }
    }
}
