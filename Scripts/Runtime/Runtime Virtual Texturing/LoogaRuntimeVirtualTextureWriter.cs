using UnityEngine;

namespace LoogaSoft.Rendering.VirtualTexturing
{
    /// <summary>Tracks moving mesh writers and requests bounded surface-cache updates.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LoogaRuntimeVirtualTextureWriter : MonoBehaviour
    {
        private Renderer _renderer;
        private Bounds _previousBounds;
        private bool _wasVisible;

        #region Built-in
        private void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            if (!_renderer)
            {
                enabled = false;
                return;
            }
            _previousBounds = _renderer.bounds;
            Refresh();
        }

        private void LateUpdate()
        {
            if (_renderer && (_renderer is SkinnedMeshRenderer || _renderer.bounds != _previousBounds
                || (_renderer.enabled && !_renderer.forceRenderingOff) != _wasVisible))
            {
                Refresh();
            }
        }

        private void OnDisable()
        {
            Refresh();
        }
        #endregion

        /// <summary>Refreshes this writer after material, visibility or geometry changes.</summary>
        public void Refresh()
        {
            if (!_renderer) return;
            LoogaRuntimeVirtualTextureRendererFeature.NotifyWriterChanged(_renderer, _previousBounds);
            _previousBounds = _renderer.bounds;
            _wasVisible = _renderer.enabled && !_renderer.forceRenderingOff;
        }
    }
}
