using UnityEngine;

namespace LoogaSoft.Rendering.VirtualTexturing
{
    /// <summary>
    /// Provides stable clipmap placement and lookup functions for Looga runtime virtual texturing.
    /// </summary>
    public static class LoogaRuntimeVirtualTextureMath
    {
        public const int MaximumClipmapCount = 4;

        /// <summary>Gets a top-down view with the same handedness as a Unity camera.</summary>
        public static Matrix4x4 GetCaptureView(Vector2 center, float height)
        {
            Quaternion rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(
                new Vector3(center.x, height, center.y), rotation, Vector3.one).inverse;
        }

        /// <summary>Converts a GPU projection for use with depth clear 1 and a less-equal depth test.</summary>
        public static Matrix4x4 ToForwardDepthProjection(Matrix4x4 gpuProjection, bool reversedDepth)
        {
            if (reversedDepth)
                gpuProjection.SetRow(2, gpuProjection.GetRow(3) - gpuProjection.GetRow(2));
            return gpuProjection;
        }

        /// <summary>
        /// Snaps an XZ position to the texel grid for a clipmap level.
        /// </summary>
        public static Vector2 SnapCenter(Vector3 position, float extent, int resolution)
        {
            float texelSize = extent / Mathf.Max(1, resolution);
            return new Vector2(
                Mathf.Floor(position.x / texelSize) * texelSize,
                Mathf.Floor(position.z / texelSize) * texelSize);
        }

        /// <summary>
        /// Gets the normalized atlas rectangle for a packed clipmap level.
        /// </summary>
        public static Vector4 GetAtlasRect(int level)
        {
            int clampedLevel = Mathf.Clamp(level, 0, MaximumClipmapCount - 1);
            return new Vector4(
                (clampedLevel & 1) * 0.5f,
                (clampedLevel >> 1) * 0.5f,
                0.5f,
                0.5f);
        }

        /// <summary>
        /// Gets the full width of a clipmap level.
        /// </summary>
        public static float GetExtent(float firstExtent, int level)
        {
            return Mathf.Max(1f, firstExtent) * (1 << Mathf.Clamp(level, 0, MaximumClipmapCount - 1));
        }

        /// <summary>
        /// Gets the first clipmap level that contains a world position.
        /// </summary>
        public static int SelectClipmap(Vector2 worldPosition, Vector4[] centerExtents, int clipmapCount)
        {
            int count = Mathf.Clamp(clipmapCount, 0, MaximumClipmapCount);
            for (int level = 0; level < count; level++)
            {
                Vector4 clipmap = centerExtents[level];
                Vector2 delta = worldPosition - new Vector2(clipmap.x, clipmap.y);
                if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) <= clipmap.z)
                    return level;
            }

            return -1;
        }
    }
}
