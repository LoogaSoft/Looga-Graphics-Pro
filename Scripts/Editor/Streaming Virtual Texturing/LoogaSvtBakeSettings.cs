using LoogaSoft.Rendering.StreamingVirtualTexturing;
using UnityEngine;

namespace LoogaSoft.Rendering.Editor.StreamingVirtualTexturing
{
    /// <summary>Stores editor-only source references for a virtual texture stack.</summary>
    [CreateAssetMenu(menuName = "Looga/Graphics/SVT Bake Settings", fileName = "SVT Bake Settings")]
    public sealed class LoogaSvtBakeSettings : ScriptableObject
    {
        public Texture2D Albedo;
        public Texture2D Normal;
        [Tooltip("R: metallic, G: occlusion, B: reserved, A: smoothness. Import as linear data.")]
        public Texture2D Mask;
        public bool Repeat = true;
        public LoogaStreamingVirtualTextureAsset Output;
    }

}
