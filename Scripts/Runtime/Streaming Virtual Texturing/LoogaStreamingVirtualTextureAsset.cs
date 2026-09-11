using System;
using UnityEngine;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Stores a disk tile index and a resident fallback without retaining source textures.</summary>
    public sealed class LoogaStreamingVirtualTextureAsset : ScriptableObject
    {
        public const int FormatVersion = 1;
        public const int ChannelCount = 3;
        public const int Border = 4;

        [Serializable]
        public struct Page
        {
            public long Offset;
            public int Length;
            public uint Checksum;
        }

        [SerializeField] private int _version;
        [SerializeField] private int _resolution;
        [SerializeField] private int _tileSize;
        [SerializeField] private bool _repeat;
        [SerializeField] private string _fileName;
        [SerializeField] private Page[] _pages;
        [SerializeField] private byte[] _fallback;

        public int Resolution => _resolution;
        public int TileSize => _tileSize;
        public int PhysicalSize => _tileSize + Border * 2;
        public int MipCount => (int)Mathf.Log(_resolution, 2) + 1;
        public int BasePageCount => Mathf.Max(1, _resolution / _tileSize);
        public bool Repeat => _repeat;
        public string FileName => _fileName;
        public byte[] Fallback => _fallback;
        public int RawPageBytes => PhysicalSize * PhysicalSize * 4 * ChannelCount;

        /// <summary>Returns the page count on one axis at this mip level.</summary>
        public int PagesAtMip(int mip) => Mathf.Max(1, BasePageCount >> mip);

        /// <summary>Returns the packed page-table row for this mip level.</summary>
        public int RowAtMip(int mip)
        {
            int row = 0;
            for (int i = 0; i < mip; i++)
            {
                row += PagesAtMip(i);
            }
            return row;
        }

        public int TableHeight => RowAtMip(MipCount);

        /// <summary>Returns the disk index for a virtual page.</summary>
        public int PageIndex(int mip, int x, int y)
        {
            if (mip < 0 || mip >= MipCount || x < 0 || y < 0 || x >= PagesAtMip(mip) || y >= PagesAtMip(mip))
                throw new ArgumentOutOfRangeException(nameof(mip));
            int offset = 0;
            for (int i = 0; i < mip; i++)
            {
                int side = PagesAtMip(i);
                offset += side * side;
            }
            return offset + y * PagesAtMip(mip) + x;
        }

        /// <summary>Returns the compressed disk range for a virtual page.</summary>
        public Page GetPage(int mip, int x, int y) => _pages[PageIndex(mip, x, y)];

        /// <summary>Validates the serialized tile layout before allocating a cache.</summary>
        public void Validate()
        {
            if (_version != FormatVersion || _resolution < 128 || _resolution > 8192 ||
                !Mathf.IsPowerOfTwo(_resolution) || _tileSize != 128 ||
                string.IsNullOrEmpty(_fileName) || System.IO.Path.GetFileName(_fileName) != _fileName ||
                !_fileName.EndsWith(".lsvt", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid Looga SVT asset layout or version.");
            if (_pages == null || _pages.Length != PageIndex(MipCount - 1, 0, 0) + 1 ||
                _fallback == null || _fallback.Length != RawPageBytes)
                throw new InvalidOperationException("Incomplete Looga SVT page index or fallback.");
            long previousEnd = 8;
            foreach (Page page in _pages)
            {
                if (page.Offset < previousEnd || page.Length <= 0 || page.Length > RawPageBytes + 1024)
                    throw new InvalidOperationException("Invalid Looga SVT disk range.");
                previousEnd = checked(page.Offset + page.Length);
            }
        }

        /// <summary>Sets the output of the offline texture baker.</summary>
        public void Initialize(int resolution, int tileSize, bool repeat, string fileName, Page[] pages, byte[] fallback)
        {
            _version = FormatVersion;
            _resolution = resolution;
            _tileSize = tileSize;
            _repeat = repeat;
            _fileName = fileName;
            _pages = pages;
            _fallback = fallback;
            Validate();
        }
    }
}
