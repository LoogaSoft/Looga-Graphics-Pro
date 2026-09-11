using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Owns a fixed GPU tile pool and bounded worker reads for one texture stack.</summary>
    public sealed class LoogaSvtCache : IDisposable
    {
        private sealed class Request
        {
            public uint Key;
            public int Mip;
            public int X;
            public int Y;
            public int LastSeen;
            public Task<byte[]> Read;
        }

        private readonly LoogaStreamingVirtualTextureAsset _asset;
        private readonly string _path;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Dictionary<uint, int> _resident = new Dictionary<uint, int>();
        private readonly Dictionary<uint, Request> _requests = new Dictionary<uint, Request>();
        private readonly HashSet<uint> _failed = new HashSet<uint>();
        private readonly uint[] _slots;
        private readonly int[] _lastUsed;
        private readonly float[] _table;
        private readonly byte[] _channelUpload;
        private readonly Texture2D[] _upload = new Texture2D[3];
        private readonly Texture2DArray[] _channels = new Texture2DArray[3];
        private readonly Texture2D _pageTable;
        private int _clock;
        private bool _disposed;
        private bool _tableDirty;

        public int Capacity => _slots.Length;
        public int ResidentPages => _resident.Count;
        public int PendingPages => _requests.Count;
        public int FailedPages => _failed.Count;
        public int UploadCount { get; private set; }
        public int DiskReadCount { get; private set; }
        public long BytesRead { get; private set; }
        public int EvictionCount { get; private set; }
        public string LastError { get; private set; }
        public long GpuBytes => (long)_asset.PhysicalSize * _asset.PhysicalSize * 4 * 3 * Capacity + _table.Length * 4L;
        public Texture2D PageTable => _pageTable;
        public Texture2DArray Albedo => _channels[0];
        public Texture2DArray Normal => _channels[1];
        public Texture2DArray Mask => _channels[2];
        public event Action ContentChanged;

        /// <summary>Creates a cache and uploads its always-resident coarse fallback.</summary>
        public LoogaSvtCache(LoogaStreamingVirtualTextureAsset asset, int capacity, string directory = null)
        {
            asset.Validate();
            if (!SystemInfo.supports2DArrayTextures || (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.DifferentTypes) == 0)
                throw new NotSupportedException("Looga SVT requires texture arrays and cross-type texture copies.");
            _asset = asset;
            _path = Path.Combine(directory ?? Path.Combine(Application.streamingAssetsPath, "LoogaSVT"), asset.FileName);
            capacity = Mathf.Clamp(capacity, 4, 512);
            _slots = new uint[capacity];
            _lastUsed = new int[capacity];
            _table = new float[asset.BasePageCount * asset.TableHeight];
            _channelUpload = new byte[asset.RawPageBytes / 3];
            try
            {
                _pageTable = new Texture2D(asset.BasePageCount, asset.TableHeight, TextureFormat.RFloat, false, true)
                {
                    name = "Looga SVT page table", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                for (int i = 0; i < 3; i++)
                {
                    _channels[i] = new Texture2DArray(asset.PhysicalSize, asset.PhysicalSize, capacity, TextureFormat.RGBA32, false, i != 0)
                    {
                        name = "Looga SVT channel " + i, filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                    };
                    _channels[i].Apply(false, true);
                    _upload[i] = new Texture2D(asset.PhysicalSize, asset.PhysicalSize, TextureFormat.RGBA32, false, i != 0)
                    {
                        name = "Looga SVT upload", hideFlags = HideFlags.HideAndDontSave
                    };
                }
                Install(asset.MipCount - 1, 0, 0, 0, asset.Fallback);
                FlushTable();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Packs a mip level and tile coordinates into the feedback address.</summary>
        public static uint Key(int mip, int x, int y) => (uint)((mip << 16) | (y << 8) | x);

        /// <summary>Requests a visible page and its parent. Duplicate requests share one read.</summary>
        public void RequestPage(int mip, int x, int y)
        {
            if (_disposed || mip < 0 || mip >= _asset.MipCount)
                return;
            int count = _asset.PagesAtMip(mip);
            if (x < 0 || y < 0 || x >= count || y >= count)
                return;
            RequestOne(mip, x, y);
            if (mip + 1 < _asset.MipCount)
            {
                RequestOne(mip + 1, x >> 1, y >> 1);
            }
        }

        /// <summary>Advances bounded reads and uploads. Call only from the Unity main thread.</summary>
        public void Tick(int uploadBudget = 2, int readBudget = 4)
        {
            if (_disposed)
                return;
            _clock++;
            if (_requests.Count == 0)
                return;
            int uploads = 0;
            foreach (Request request in _requests.Values.ToArray())
            {
                if (request.Read == null)
                {
                    if (_clock - request.LastSeen > 120)
                    {
                        _requests.Remove(request.Key);
                    }
                    continue;
                }
                if (!request.Read.IsCompleted || uploads >= Mathf.Max(1, uploadBudget))
                    continue;
                if (request.Read.IsCanceled)
                {
                    _requests.Remove(request.Key);
                    continue;
                }
                if (request.Read.IsFaulted)
                {
                    LastError = request.Read.Exception.GetBaseException().Message;
                    _failed.Add(request.Key);
                    _requests.Remove(request.Key);
                    continue;
                }
                if (_clock - request.LastSeen > 120)
                {
                    _requests.Remove(request.Key);
                    continue;
                }
                int slot = FindSlot();
                if (slot < 0)
                    continue;
                _requests.Remove(request.Key);
                Install(request.Mip, request.X, request.Y, slot, request.Read.Result);
                BytesRead += _asset.GetPage(request.Mip, request.X, request.Y).Length;
                uploads++;
            }
            int reading = _requests.Values.Count(r => r.Read != null);
            foreach (Request request in _requests.Values.Where(r => r.Read == null).OrderByDescending(r => r.Mip).ThenByDescending(r => r.LastSeen))
            {
                if (reading >= Mathf.Clamp(readBudget, 1, 8))
                    break;
                var page = _asset.GetPage(request.Mip, request.X, request.Y);
                int bytes = _asset.RawPageBytes;
                CancellationToken token = _cancel.Token;
                DiskReadCount++;
                request.Read = Task.Run(() => LoogaSvtPageIO.Read(_path, page, bytes, token), token);
                reading++;
            }
            FlushTable();
            if (uploads > 0)
            {
                ContentChanged?.Invoke();
            }
        }

        /// <summary>Binds one stack to a compatible material without changing the shared asset.</summary>
        public void Bind(MaterialPropertyBlock block, int id)
        {
            block.SetFloat("_LoogaSvtId", id);
            block.SetVector("_LoogaSvtLayout", new Vector4(_asset.Resolution, _asset.TileSize, _asset.MipCount, _asset.Repeat ? 1 : 0));
            block.SetTexture("_LoogaSvtPageTable", _pageTable);
            block.SetTexture("_LoogaSvtAlbedo", _channels[0]);
            block.SetTexture("_LoogaSvtNormal", _channels[1]);
            block.SetTexture("_LoogaSvtMask", _channels[2]);
        }

        /// <summary>Allows failed pages to be requested again after their file has been repaired.</summary>
        public void RetryFailedPages()
        {
            _failed.Clear();
            LastError = null;
        }

        /// <summary>Reports whether a specific page is resident, without using a fallback.</summary>
        public bool IsResident(int mip, int x, int y) => _resident.ContainsKey(Key(mip, x, y));

        /// <summary>Cancels pending reads and releases all GPU resources.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cancel.Cancel();
            foreach (Request request in _requests.Values)
            {
                if (request.Read != null)
                {
                    request.Read.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            _requests.Clear();
            _cancel.Dispose();
            foreach (var texture in _channels)
            {
                Destroy(texture);
            }
            foreach (var texture in _upload)
            {
                Destroy(texture);
            }
            Destroy(_pageTable);
        }

        private void RequestOne(int mip, int x, int y)
        {
            uint key = Key(mip, x, y);
            if (_resident.TryGetValue(key, out int slot))
            {
                _lastUsed[slot] = _clock;
                return;
            }
            if (_failed.Contains(key))
                return;
            if (_requests.TryGetValue(key, out Request existing))
            {
                existing.LastSeen = _clock;
                return;
            }
            if (_requests.Count < Capacity * 4)
            {
                _requests.Add(key, new Request { Key = key, Mip = mip, X = x, Y = y, LastSeen = _clock });
            }
        }

        private int FindSlot()
        {
            int oldest = -1;
            for (int i = 1; i < Capacity; i++)
            {
                if (_slots[i] == 0)
                    return i;
                if (_clock - _lastUsed[i] > 3 && (oldest < 0 || _lastUsed[i] < _lastUsed[oldest]))
                {
                    oldest = i;
                }
            }
            return oldest;
        }

        private void Install(int mip, int x, int y, int slot, byte[] bytes)
        {
            if (slot != 0 && _slots[slot] != 0)
            {
                uint previous = _slots[slot] - 1;
                int oldMip = (int)(previous >> 16);
                int oldY = (int)((previous >> 8) & 255);
                int oldX = (int)(previous & 255);
                _table[(_asset.RowAtMip(oldMip) + oldY) * _asset.BasePageCount + oldX] = 0;
                _resident.Remove(previous);
                EvictionCount++;
            }
            int length = bytes.Length / 3;
            for (int i = 0; i < 3; i++)
            {
                Buffer.BlockCopy(bytes, i * length, _channelUpload, 0, length);
                _upload[i].LoadRawTextureData(_channelUpload);
                _upload[i].Apply(false, false);
                Graphics.CopyTexture(_upload[i], 0, 0, _channels[i], slot, 0);
            }
            uint key = Key(mip, x, y);
            _slots[slot] = key + 1;
            _lastUsed[slot] = _clock;
            _resident[key] = slot;
            _table[(_asset.RowAtMip(mip) + y) * _asset.BasePageCount + x] = slot + 1;
            _tableDirty = true;
            UploadCount++;
        }

        private void FlushTable()
        {
            if (!_tableDirty)
                return;
            _pageTable.SetPixelData(_table, 0);
            _pageTable.Apply(false, false);
            _tableDirty = false;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (!value)
                return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
