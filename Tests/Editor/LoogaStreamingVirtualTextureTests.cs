using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LoogaSoft.Rendering.StreamingVirtualTexturing;
using NUnit.Framework;
using UnityEngine;

namespace LoogaSoft.Rendering.Tests
{
    public sealed class LoogaStreamingVirtualTextureTests
    {
        private string _directory;
        private LoogaStreamingVirtualTextureAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "LoogaSvtTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _asset = ScriptableObject.CreateInstance<LoogaStreamingVirtualTextureAsset>();
            var records = new List<LoogaStreamingVirtualTextureAsset.Page>();
            byte[] fallback = null;
            using (var file = File.Create(Path.Combine(_directory, "test.lsvt")))
            {
                using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                writer.Write(LoogaSvtPageIO.Magic);
                writer.Write(LoogaStreamingVirtualTextureAsset.FormatVersion);
                for (int mip = 0; mip < 9; mip++)
                {
                    int side = Mathf.Max(1, 2 >> mip);
                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            byte[] bytes = new byte[136 * 136 * 4 * 3];
                            for (int i = 0; i < bytes.Length; i++)
                            {
                                bytes[i] = (byte)(31 + mip + x * 40 + y * 70);
                            }
                            records.Add(LoogaSvtPageIO.Write(file, bytes));
                            fallback = bytes;
                        }
                    }
                }
            }
            _asset.Initialize(256, 128, true, "test.lsvt", records.ToArray(), fallback);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_asset);
            Directory.Delete(_directory, true);
        }

        [Test]
        public void TileRoundTripPreservesAllChannels()
        {
            byte[] bytes = LoogaSvtPageIO.Read(Path.Combine(_directory, "test.lsvt"), _asset.GetPage(0, 1, 1),
                _asset.RawPageBytes, CancellationToken.None);
            Assert.That(bytes.Length, Is.EqualTo(_asset.RawPageBytes));
            Assert.That(Array.TrueForAll(bytes, value => value == 141));
        }

        [Test]
        public void CorruptChecksumIsRejected()
        {
            var page = _asset.GetPage(0, 0, 0);
            page.Checksum++;
            Assert.Throws<InvalidDataException>(() => LoogaSvtPageIO.Read(Path.Combine(_directory, "test.lsvt"),
                page, _asset.RawPageBytes, CancellationToken.None));
        }

        [Test]
        public void InvalidDiskRangeIsRejected()
        {
            var page = _asset.GetPage(0, 0, 0);
            page.Offset = long.MaxValue;
            Assert.Throws<InvalidDataException>(() => LoogaSvtPageIO.Read(Path.Combine(_directory, "test.lsvt"),
                page, _asset.RawPageBytes, CancellationToken.None));
        }

        [Test]
        public void CancellationStopsTileRead()
        {
            Assert.Throws<OperationCanceledException>(() => LoogaSvtPageIO.Read(Path.Combine(_directory, "test.lsvt"),
                _asset.GetPage(0, 0, 0), _asset.RawPageBytes, new CancellationToken(true)));
        }

        [Test]
        public void MipTableAddressesDoNotOverlap()
        {
            var indices = new HashSet<int>();
            for (int mip = 0; mip < _asset.MipCount; mip++)
            {
                for (int y = 0; y < _asset.PagesAtMip(mip); y++)
                {
                    for (int x = 0; x < _asset.PagesAtMip(mip); x++)
                    {
                        Assert.That(indices.Add((_asset.RowAtMip(mip) + y) * _asset.BasePageCount + x));
                    }
                }
            }
            Assert.That(indices.Count, Is.EqualTo(12));
            Assert.Throws<ArgumentOutOfRangeException>(() => _asset.PageIndex(0, 2, 0));
        }

        [Test]
        public void FeedbackKeyPreservesCoordinates()
        {
            uint value = LoogaSvtCache.Key(13, 63, 47) | (4095u << 20);
            Assert.That(value >> 20, Is.EqualTo(4095));
            Assert.That((value >> 16) & 15, Is.EqualTo(13));
            Assert.That((value >> 8) & 255, Is.EqualTo(47));
            Assert.That(value & 255, Is.EqualTo(63));
        }

        [Test]
        public void FallbackDoesNotRequireDiskFile()
        {
            File.Delete(Path.Combine(_directory, "test.lsvt"));
            using var cache = new LoogaSvtCache(_asset, 4, _directory);
            Assert.That(cache.IsResident(8, 0, 0));
            Assert.That(cache.ResidentPages, Is.EqualTo(1));
            cache.RequestPage(0, 0, 0);
            Pump(cache);
            Assert.That(cache.FailedPages, Is.GreaterThan(0));
            Assert.That(cache.IsResident(8, 0, 0));
        }

        [Test]
        public void DuplicateRequestsShareReads()
        {
            using var cache = new LoogaSvtCache(_asset, 4, _directory);
            for (int i = 0; i < 30; i++)
            {
                cache.RequestPage(0, 0, 0);
            }
            Assert.That(cache.PendingPages, Is.EqualTo(2));
            Pump(cache);
            Assert.That(cache.IsResident(0, 0, 0));
            Assert.That(cache.UploadCount, Is.EqualTo(3));
        }

        [Test]
        public void EvictionPreservesFallbackAndBudget()
        {
            using var cache = new LoogaSvtCache(_asset, 4, _directory);
            cache.RequestPage(0, 0, 0);
            cache.RequestPage(0, 1, 0);
            Pump(cache);
            for (int i = 0; i < 10; i++)
            {
                cache.Tick();
            }
            cache.RequestPage(0, 0, 1);
            Pump(cache);
            Assert.That(cache.IsResident(0, 0, 1));
            Assert.That(cache.IsResident(8, 0, 0));
            Assert.That(cache.ResidentPages, Is.LessThanOrEqualTo(4));
            Assert.That(cache.EvictionCount, Is.GreaterThan(0));
            Assert.That(cache.PageTable.GetPixel(0, _asset.RowAtMip(8)).r, Is.EqualTo(1));
        }

        [Test]
        public void FullVisibleCacheDoesNotRepeatDiskReads()
        {
            using var cache = new LoogaSvtCache(_asset, 4, _directory);
            for (int frame = 0; frame < 80; frame++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        cache.RequestPage(0, x, y);
                    }
                }
                cache.Tick();
                Thread.Sleep(2);
            }
            Assert.That(cache.DiskReadCount, Is.EqualTo(5));
            Assert.That(cache.ResidentPages, Is.EqualTo(4));
            Assert.That(cache.PendingPages, Is.EqualTo(2));
            Assert.That(cache.EvictionCount, Is.EqualTo(0));
        }

        [Test]
        public void DisposeWithPendingReadsIsIdempotent()
        {
            var cache = new LoogaSvtCache(_asset, 4, _directory);
            cache.RequestPage(0, 0, 0);
            cache.Tick();
            cache.Dispose();
            Assert.DoesNotThrow(cache.Dispose);
            Assert.DoesNotThrow(() => cache.Tick());
        }

        private static void Pump(LoogaSvtCache cache)
        {
            for (int i = 0; i < 300 && cache.PendingPages > 0; i++)
            {
                cache.Tick();
                Thread.Sleep(2);
            }
            Assert.That(cache.PendingPages, Is.EqualTo(0), "Tile reads did not complete.");
        }
    }
}
