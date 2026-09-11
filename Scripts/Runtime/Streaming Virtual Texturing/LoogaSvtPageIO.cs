using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Reads independent compressed tiles with bounded output and integrity checks.</summary>
    public static class LoogaSvtPageIO
    {
        public const int Magic = 0x5456534C;

        /// <summary>Computes a checksum for the decoded tile bytes.</summary>
        public static uint Checksum(byte[] bytes)
        {
            uint hash = 2166136261;
            foreach (byte value in bytes)
            {
                hash = unchecked((hash ^ value) * 16777619);
            }
            return hash;
        }

        /// <summary>Reads one tile on a worker thread. Unity objects must not be accessed here.</summary>
        public static byte[] Read(string path, LoogaStreamingVirtualTextureAsset.Page page, int rawLength, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (rawLength <= 0 || rawLength > 16 * 1024 * 1024 || page.Length > rawLength + 1024)
                throw new InvalidDataException("Looga SVT tile size exceeds the read budget.");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new BinaryReader(file);
            if (file.Length < 8 || reader.ReadInt32() != Magic || reader.ReadInt32() != LoogaStreamingVirtualTextureAsset.FormatVersion)
                throw new InvalidDataException("Invalid Looga SVT file header.");
            if (page.Offset < 8 || page.Length <= 0 || page.Offset > file.Length - page.Length)
                throw new InvalidDataException("Looga SVT page is outside the file.");
            file.Position = page.Offset;
            byte[] compressed = reader.ReadBytes(page.Length);
            if (compressed.Length != page.Length)
                throw new EndOfStreamException();
            using var stream = new DeflateStream(new MemoryStream(compressed, false), CompressionMode.Decompress);
            byte[] result = new byte[rawLength];
            int position = 0;
            while (position < result.Length)
            {
                token.ThrowIfCancellationRequested();
                int count = stream.Read(result, position, result.Length - position);
                if (count == 0)
                    throw new InvalidDataException("Truncated Looga SVT page.");
                position += count;
            }
            if (stream.ReadByte() != -1 || Checksum(result) != page.Checksum)
                throw new InvalidDataException("Looga SVT tile integrity check failed.");
            return result;
        }

        /// <summary>Writes one independently compressed tile and returns its disk range.</summary>
        public static LoogaStreamingVirtualTextureAsset.Page Write(Stream target, byte[] bytes)
        {
            using var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
            {
                deflate.Write(bytes, 0, bytes.Length);
            }
            var page = new LoogaStreamingVirtualTextureAsset.Page
            {
                Offset = target.Position,
                Length = checked((int)compressed.Length),
                Checksum = Checksum(bytes)
            };
            compressed.Position = 0;
            compressed.CopyTo(target);
            return page;
        }
    }
}
