using System;
using LoogaSoft.Instancing;
using UnityEngine;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Binds BRG prototype materials to the existing Graphics Pro SVT registry and feedback pass.</summary>
    [CreateAssetMenu(menuName = "Looga/Graphics/SVT Instance Profile")]
    public sealed class LoogaSvtInstanceProfile : InstanceMaterialProfile
    {
        [SerializeField] private LoogaStreamingVirtualTextureAsset _asset;
        [SerializeField, Range(4, 256)] private int _residentPages = 64;
        /// <summary>Current shared cache, or null when no registered prototype owns the stack.</summary>
        public LoogaSvtCache Cache => _asset ? LoogaSvtRegistry.Get(_asset) : null;
        public override float BoundsPadding => 0;
        public override bool Supports(Material material) => material && material.HasProperty("_LoogaSvtId") &&
            material.FindPass("Feedback") >= 0;

        /// <summary>Assign a baked stack and the shared cache capacity before registering prototypes.</summary>
        public void Configure(LoogaStreamingVirtualTextureAsset asset, int residentPages = 64)
        {
            if (!asset) throw new ArgumentNullException(nameof(asset));
            _asset = asset;
            _residentPages = Mathf.Clamp(residentPages, 4, 256);
        }

        public override InstanceMaterialBinding CreateMaterial(Material source)
        {
            if (!_asset || !Supports(source))
            {
                throw new InvalidOperationException("Assign a baked SVT stack and a material with the SVT feedback pass.");
            }
            return new Binding(_asset, _residentPages, source);
        }

        private sealed class Binding : InstanceMaterialBinding
        {
            private LoogaStreamingVirtualTextureAsset _asset;
            private Material _material;
            public override Material Material => _material;

            internal Binding(LoogaStreamingVirtualTextureAsset asset, int capacity, Material source)
            {
                var entry = LoogaSvtRegistry.Acquire(asset, capacity);
                _asset = asset;
                try
                {
                    _material = new Material(source) { name = source.name + " (Looga SVT instance)", hideFlags = HideFlags.HideAndDontSave };
                    entry.Cache.Bind(_material, entry.Id);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public override void Dispose()
            {
                if (_material)
                {
                    UnityEngine.Object.DestroyImmediate(_material);
                    _material = null;
                }
                if (!ReferenceEquals(_asset, null))
                {
                    LoogaSvtRegistry.Release(_asset);
                    _asset = null;
                }
            }
        }
    }
}
