using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Binds a streamed texture stack to one renderer material slot.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("Looga/Graphics/Streaming Virtual Texture")]
    public sealed class LoogaStreamingVirtualTexture : MonoBehaviour
    {
        [SerializeField] private LoogaStreamingVirtualTextureAsset _asset;
        [SerializeField, Min(0)] private int _materialIndex;
        [SerializeField, Range(4, 256)] private int _residentPages = 64;

        private Renderer _renderer;
        private MaterialPropertyBlock _previous;
        private LoogaStreamingVirtualTextureAsset _boundAsset;
        private int _boundIndex;
        private int _boundCapacity;
        private string _error;
        private LoogaStreamingVirtualTextureAsset _attemptedAsset;

        public LoogaStreamingVirtualTextureAsset Asset => _asset;
        public LoogaSvtCache Cache => _boundAsset ? LoogaSvtRegistry.Get(_boundAsset) : null;
        public string Error => _error;

        private void OnEnable() => Rebind();
        private void OnDisable() => Unbind();

        private void Update()
        {
            if ((!_asset && !ReferenceEquals(_boundAsset, null)) || _asset != _attemptedAsset || _materialIndex != _boundIndex || _residentPages != _boundCapacity)
            {
                Rebind();
            }
        }

        /// <summary>Assigns a texture stack and refreshes the renderer binding.</summary>
        public void Configure(LoogaStreamingVirtualTextureAsset asset, int residentPages = 64, int materialIndex = 0)
        {
            _asset = asset;
            _residentPages = Mathf.Clamp(residentPages, 4, 256);
            _materialIndex = materialIndex;
            Rebind();
        }

        /// <summary>Releases the old binding and binds the current asset.</summary>
        public void Rebind()
        {
            Unbind();
            _attemptedAsset = _asset;
            _boundIndex = _materialIndex;
            _boundCapacity = _residentPages;
            _error = null;
            if (!_asset || !isActiveAndEnabled)
                return;
            _renderer = GetComponent<Renderer>();
            Material[] materials = _renderer.sharedMaterials;
            if (_materialIndex < 0 || _materialIndex >= materials.Length || !materials[_materialIndex] ||
                !materials[_materialIndex].HasProperty("_LoogaSvtId"))
            {
                _error = "Assign a Looga SVT material to the selected material slot.";
                return;
            }
            try
            {
                var entry = LoogaSvtRegistry.Acquire(_asset, _residentPages);
                _boundAsset = _asset;
                _previous = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_previous, _materialIndex);
                var block = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(block, _materialIndex);
                entry.Cache.Bind(block, entry.Id);
                _renderer.SetPropertyBlock(block, _materialIndex);
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                Unbind();
            }
        }

        private void Unbind()
        {
            if (_renderer && _previous != null)
            {
                _renderer.SetPropertyBlock(_previous, _boundIndex);
            }
            _previous = null;
            if (!ReferenceEquals(_boundAsset, null))
            {
                LoogaSvtRegistry.Release(_boundAsset);
            }
            _boundAsset = null;
        }
    }

    internal static class LoogaSvtRegistry
    {
        internal sealed class Entry
        {
            public int Id;
            public int References;
            public LoogaSvtCache Cache;
        }

        private static readonly Dictionary<LoogaStreamingVirtualTextureAsset, Entry> Entries = new();
        private static readonly Dictionary<int, Entry> ById = new();
        private static int _nextId = 1;
        private static double _lastTick;
        public static bool HasCaches => Entries.Count != 0;

        internal static Entry Acquire(LoogaStreamingVirtualTextureAsset asset, int capacity)
        {
            if (!Entries.TryGetValue(asset, out Entry entry))
            {
                if (Entries.Count >= 8)
                    throw new InvalidOperationException("Looga SVT supports eight active stacks per session. Disable unused stacks.");
                while (ById.ContainsKey(_nextId))
                {
                    _nextId = _nextId % 4095 + 1;
                }
                entry = new Entry { Id = _nextId, Cache = new LoogaSvtCache(asset, capacity) };
                _nextId = _nextId % 4095 + 1;
                Entries.Add(asset, entry);
                ById.Add(entry.Id, entry);
            }
            else if (entry.Cache.Capacity != Mathf.Clamp(capacity, 4, 512))
            {
                throw new InvalidOperationException("Renderers sharing an SVT asset must use the same page capacity.");
            }
            entry.References++;
            return entry;
        }

        internal static LoogaSvtCache Get(LoogaStreamingVirtualTextureAsset asset) =>
            Entries.TryGetValue(asset, out Entry entry) ? entry.Cache : null;

        internal static void Release(LoogaStreamingVirtualTextureAsset asset)
        {
            if (!Entries.TryGetValue(asset, out Entry entry) || --entry.References > 0)
                return;
            Entries.Remove(asset);
            ById.Remove(entry.Id);
            entry.Cache.Dispose();
        }

        internal static Dictionary<int, LoogaSvtCache> Snapshot()
        {
            var snapshot = new Dictionary<int, LoogaSvtCache>();
            foreach (var pair in ById)
            {
                snapshot.Add(pair.Key, pair.Value.Cache);
            }
            return snapshot;
        }

        internal static void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastTick < 1.0 / 120.0)
                return;
            _lastTick = now;
            foreach (Entry entry in Entries.Values)
            {
                entry.Cache.Tick();
            }
        }
    }
}
