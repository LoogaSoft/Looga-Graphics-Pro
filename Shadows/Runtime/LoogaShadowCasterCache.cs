using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LoogaSoft.Shadows
{
    internal enum LoogaCachedLevelMode
    {
        // Drawn like any level. The level's parameters changed this frame, so a cache filled now would
        // likely be discarded next frame, for example while the light rotates.
        Uncached,
        // The cache is redrawn whole.
        Refill,
        // The level scrolled by whole texels. Only the exposed strips are drawn into the cache.
        Scroll,
        // The cache is current.
        Reuse
    }

    /// <summary>
    /// Keeps the static shadow casters of the coarse clipmap levels between frames, per camera. A cached level
    /// stores its casters toroidally at the level's resolution: level texel p lives at cache texel
    /// (origin + p) mod resolution, where origin is the light-space texel index of the level's first texel.
    /// Scrolling therefore moves no texels; only the newly exposed strips are drawn. Each frame the cache is
    /// unwrapped into the level's atlas tile and the level's other casters are drawn over it.
    /// Static casters are renderers marked Static Shadow Caster and static Looga Instancing renderers. Static
    /// casters that change without a notification stay as cached until <see cref="Invalidate"/>.
    /// </summary>
    internal sealed class LoogaShadowCasterCache : IDisposable
    {
        internal const int MaximumExposedRects = 2;

        // A level keeps the light-space depth of its centre while the ideal depth stays within this share of
        // the depth range, because a new depth changes every cached depth value.
        private const float LightDepthTolerance = 0.1f;

        internal readonly struct Key : IEquatable<Key>
        {
            private readonly int _light;
            private readonly Quaternion _rotation;
            private readonly float _radius;
            private readonly int _resolution;
            private readonly float _depthRange;
            private readonly float _lightDepth;
            private readonly Vector4 _casterBias;

            internal Key(int light, Quaternion rotation, float radius, int resolution, float depthRange,
                float lightDepth, Vector4 casterBias)
            {
                _light = light;
                _rotation = rotation;
                _radius = radius;
                _resolution = resolution;
                _depthRange = depthRange;
                _lightDepth = lightDepth;
                _casterBias = casterBias;
            }

            // Exact comparisons: any change moves cached depths or texels.
            public bool Equals(Key other)
            {
                return _light == other._light && _rotation.Equals(other._rotation) && _radius == other._radius &&
                    _resolution == other._resolution && _depthRange == other._depthRange &&
                    _lightDepth == other._lightDepth && _casterBias == other._casterBias;
            }

            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_light, _rotation, _radius, _resolution, _lightDepth);
        }

        internal sealed class Level
        {
            public RTHandle Texture;

            // What the texture holds, as committed by the last recorded frame.
            public bool Valid;
            public Vector2Int Origin;
            public long Revision;
            public bool HasKey;
            public Key Key;
            public float LightDepth;
            public Quaternion Rotation;

            // This frame's plan.
            public LoogaCachedLevelMode Mode;
            public Vector2Int PlannedOrigin;
            public long PlannedRevision;
            public Vector2Int WrapOffset;
            public int ExposedCount;
            public readonly RectInt[] Exposed = new RectInt[MaximumExposedRects];
        }

        private sealed class CameraState
        {
            public readonly Level[] Levels;

            public CameraState(int levelCount)
            {
                Levels = new Level[levelCount];
                for (int level = 0; level < levelCount; level++)
                    Levels[level] = new Level();
            }
        }

        private static long s_Revision;
        private readonly Dictionary<Camera, CameraState> _cameras = new();
        private readonly List<Camera> _destroyedCameras = new();
        private readonly int _levelCount;

        static LoogaShadowCasterCache()
        {
            SceneManager.sceneLoaded += (_, _) => Invalidate();
            SceneManager.sceneUnloaded += _ => Invalidate();
#if UNITY_EDITOR
            // Edits move, add or remove static casters without any runtime notification.
            UnityEditor.ObjectChangeEvents.changesPublished += (ref UnityEditor.ObjectChangeEventStream _) => Invalidate();
            UnityEditor.EditorApplication.playModeStateChanged += _ => Invalidate();
#endif
        }

        internal LoogaShadowCasterCache(int levelCount)
        {
            _levelCount = levelCount;
        }

        /// <summary>Redraw every cached level, for example after static casters changed.</summary>
        internal static void Invalidate()
        {
            s_Revision++;
        }

        private static long CurrentRevision =>
            unchecked(s_Revision * 1000003L + LoogaSoft.Instancing.InstanceShadowSplits.StaticCasterRevision);

        // The light-space depth for a cached level's centre: the previous one while it stays close to the
        // ideal one and the light is unchanged.
        internal float HoldLightDepth(Camera camera, int level, Quaternion rotation, float idealDepth, float depthRange)
        {
            if (!_cameras.TryGetValue(camera, out CameraState state))
                return idealDepth;

            Level entry = state.Levels[level];
            if (!entry.HasKey || !entry.Rotation.Equals(rotation) ||
                Mathf.Abs(idealDepth - entry.LightDepth) > depthRange * LightDepthTolerance)
            {
                return idealDepth;
            }

            return entry.LightDepth;
        }

        // Chooses how a cached level is drawn this frame. Origin is the light-space texel index of the level's
        // first texel.
        internal LoogaCachedLevelMode Plan(Camera camera, int level, Key key, Quaternion rotation, float lightDepth,
            Vector2Int origin, int resolution)
        {
            CameraState state = GetState(camera);
            Level entry = state.Levels[level];
            entry.ExposedCount = 0;
            entry.PlannedOrigin = origin;
            entry.PlannedRevision = CurrentRevision;
            entry.WrapOffset = new Vector2Int(Modulo(origin.x, resolution), Modulo(origin.y, resolution));

            if (!entry.HasKey || !entry.Key.Equals(key))
            {
                entry.HasKey = true;
                entry.Key = key;
                entry.Rotation = rotation;
                entry.LightDepth = lightDepth;
                entry.Valid = false;
                entry.Mode = LoogaCachedLevelMode.Uncached;
                return entry.Mode;
            }

            if (EnsureTexture(entry, resolution))
                entry.Valid = false;

            Vector2Int shift = origin - entry.Origin;
            if (!entry.Valid || entry.Revision != entry.PlannedRevision ||
                Mathf.Abs(shift.x) >= resolution || Mathf.Abs(shift.y) >= resolution)
            {
                entry.Mode = LoogaCachedLevelMode.Refill;
                entry.Exposed[entry.ExposedCount++] = new RectInt(0, 0, resolution, resolution);
                return entry.Mode;
            }

            if (shift == Vector2Int.zero)
            {
                entry.Mode = LoogaCachedLevelMode.Reuse;
                return entry.Mode;
            }

            // Level texels increase with light-space x and y. Moving toward +x exposes the last columns.
            int keptMinX = shift.x > 0 ? 0 : -shift.x;
            int keptMaxX = shift.x > 0 ? resolution - shift.x : resolution;
            if (shift.x != 0)
            {
                entry.Exposed[entry.ExposedCount++] = shift.x > 0
                    ? new RectInt(keptMaxX, 0, shift.x, resolution)
                    : new RectInt(0, 0, -shift.x, resolution);
            }

            if (shift.y != 0)
            {
                entry.Exposed[entry.ExposedCount++] = shift.y > 0
                    ? new RectInt(keptMinX, resolution - shift.y, keptMaxX - keptMinX, shift.y)
                    : new RectInt(keptMinX, 0, keptMaxX - keptMinX, -shift.y);
            }

            entry.Mode = LoogaCachedLevelMode.Scroll;
            return entry.Mode;
        }

        internal Level GetLevel(Camera camera, int level)
        {
            return GetState(camera).Levels[level];
        }

        // The recorded frame draws this plan, so the texture now holds it.
        internal static void Commit(Level entry)
        {
            if (entry.Mode == LoogaCachedLevelMode.Uncached)
                return;

            entry.Valid = true;
            entry.Origin = entry.PlannedOrigin;
            entry.Revision = entry.PlannedRevision;
        }

        // Frees a level that is no longer cached.
        internal void ReleaseLevel(Camera camera, int level)
        {
            if (!_cameras.TryGetValue(camera, out CameraState state))
                return;

            Release(state.Levels[level]);
            state.Levels[level].HasKey = false;
        }

        internal void RemoveDestroyedCameras()
        {
            foreach (KeyValuePair<Camera, CameraState> entry in _cameras)
            {
                if (entry.Key == null)
                    _destroyedCameras.Add(entry.Key);
            }

            foreach (Camera camera in _destroyedCameras)
            {
                foreach (Level level in _cameras[camera].Levels)
                    Release(level);
                _cameras.Remove(camera);
            }

            _destroyedCameras.Clear();
        }

        public void Dispose()
        {
            foreach (CameraState state in _cameras.Values)
            {
                foreach (Level level in state.Levels)
                    Release(level);
            }

            _cameras.Clear();
        }

        private CameraState GetState(Camera camera)
        {
            if (!_cameras.TryGetValue(camera, out CameraState state))
            {
                state = new CameraState(_levelCount);
                _cameras.Add(camera, state);
            }

            return state;
        }

        // True when the texture was created, so its contents are undefined.
        private static bool EnsureTexture(Level entry, int resolution)
        {
            if (entry.Texture != null && entry.Texture.rt != null && entry.Texture.rt.width == resolution)
                return false;

            Release(entry);
            // The atlas format, read as raw depth when it is unwrapped into the atlas.
            RenderTextureDescriptor descriptor = new(resolution, resolution, RenderTextureFormat.Shadowmap, 32)
            {
                shadowSamplingMode = ShadowSamplingMode.None,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            entry.Texture = RTHandles.Alloc(
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "Looga Shadow Caster Cache");
            return true;
        }

        private static void Release(Level entry)
        {
            entry.Texture?.Release();
            entry.Texture = null;
            entry.Valid = false;
        }

        private static int Modulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
