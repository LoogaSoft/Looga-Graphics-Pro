using LoogaSoft.Rendering.VirtualTexturing;
using NUnit.Framework;
using UnityEngine;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaRuntimeVirtualTextureMathTests
    {
        [Test]
        public void SnapCenterUsesClipmapTexelGrid()
        {
            Vector2 center = LoogaRuntimeVirtualTextureMath.SnapCenter(
                new Vector3(13.7f, 100f, -9.1f),
                128f,
                1024);

            Assert.That(center.x, Is.EqualTo(13.625f).Within(0.0001f));
            Assert.That(center.y, Is.EqualTo(-9.125f).Within(0.0001f));
        }

        [TestCase(0, 0f, 0f)]
        [TestCase(1, 0.5f, 0f)]
        [TestCase(2, 0f, 0.5f)]
        [TestCase(3, 0.5f, 0.5f)]
        public void AtlasRectUsesTwoByTwoPacking(int level, float expectedX, float expectedY)
        {
            Vector4 rect = LoogaRuntimeVirtualTextureMath.GetAtlasRect(level);

            Assert.That(rect, Is.EqualTo(new Vector4(expectedX, expectedY, 0.5f, 0.5f)));
        }

        [Test]
        public void SelectClipmapPrefersFinestContainingLevel()
        {
            Vector4[] clipmaps =
            {
                new(0f, 0f, 64f, 0.125f),
                new(0f, 0f, 128f, 0.25f),
                new(0f, 0f, 256f, 0.5f),
                new(0f, 0f, 512f, 1f)
            };

            Assert.That(
                LoogaRuntimeVirtualTextureMath.SelectClipmap(new Vector2(20f, 20f), clipmaps, 4),
                Is.EqualTo(0));
            Assert.That(
                LoogaRuntimeVirtualTextureMath.SelectClipmap(new Vector2(100f, 20f), clipmaps, 4),
                Is.EqualTo(1));
            Assert.That(
                LoogaRuntimeVirtualTextureMath.SelectClipmap(new Vector2(600f, 0f), clipmaps, 4),
                Is.EqualTo(-1));
        }

        [Test]
        public void ExtentDoublesAtEachLevel()
        {
            Assert.That(LoogaRuntimeVirtualTextureMath.GetExtent(128f, 0), Is.EqualTo(128f));
            Assert.That(LoogaRuntimeVirtualTextureMath.GetExtent(128f, 3), Is.EqualTo(1024f));
        }
    }
}
