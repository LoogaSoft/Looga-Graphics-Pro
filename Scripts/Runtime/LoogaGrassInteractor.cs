using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting
{
    /// <summary>A bounded grass-bending cylinder. Materials control maximum local displacement and root weighting.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LoogaGrassInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _radius = 2;
        [SerializeField, Range(0, 1)] private float _strength = 1;
        private static readonly List<LoogaGrassInteractor> _active = new();
        private static readonly Vector4[] _current = new Vector4[64];
        private static readonly Vector4[] _previous = new Vector4[64];
        private static readonly Vector4[] _strengths = new Vector4[64];
        private static readonly Vector4[] _oldStrengths = new Vector4[64];
        private static int _count;
        private static int _oldCount;
        private static int _frame = -1;
        private static bool _subscribed;
#if UNITY_EDITOR
        private static int _editorFrame;
        private static void BeginEditorFrame() => _editorFrame++;
#endif
        private static int Frame
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) return _editorFrame;
#endif
                return Time.frameCount;
            }
        }

        private void OnEnable()
        {
            if (!_active.Contains(this))
            {
                _active.Add(this);
            }
            if (!_subscribed)
            {
                RenderPipelineManager.beginCameraRendering += BeforeCamera;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update += BeginEditorFrame;
#endif
                _subscribed = true;
            }
        }
        private void OnDisable() => _active.Remove(this);

        /// <summary>Set world radius and normalized influence. At most sixty-four active interactors are supported.</summary>
        public void Configure(float radius, float strength = 1)
        {
            if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(strength) || strength < 0 || strength > 1)
            {
                throw new ArgumentException("Use a positive radius and strength between zero and one.");
            }
            _radius = radius;
            _strength = strength;
        }

        private static void BeforeCamera(ScriptableRenderContext context, Camera camera) => Publish();

        /// <summary>Publish current inputs and preserve the previous frame for all cameras.</summary>
        public static void Publish()
        {
            if (_frame != Frame)
            {
                Array.Copy(_current, _previous, 64);
                Array.Copy(_strengths, _oldStrengths, 64);
                _oldCount = _count;
                _frame = Frame;
            }
            _count = 0;
            foreach (var item in _active)
            {
                if (!item || !item.isActiveAndEnabled || !float.IsFinite(item._radius) || item._radius <= 0)
                {
                    continue;
                }
                if (_count == 64)
                {
                    break;
                }
                Vector3 position = item.transform.position;
                _current[_count] = new Vector4(position.x, position.y, position.z, item._radius);
                _strengths[_count++] = new Vector4(Mathf.Clamp01(item._strength), 0, 0, 0);
            }
            Upload();
            if (_count == 0 && _oldCount == 0)
            {
                RenderPipelineManager.beginCameraRendering -= BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= BeginEditorFrame;
#endif
                _subscribed = false;
            }
        }

        private static void Upload()
        {
            Shader.SetGlobalInt("_GrassInteractorHasStrength", _count > 0 || _oldCount > 0 ? 1 : 0);
            Shader.SetGlobalInt("_GrassInteractorCount", _count);
            Shader.SetGlobalInt("_PreviousGrassInteractorCount", _oldCount);
            Shader.SetGlobalVectorArray("_GrassInteractors", _current);
            Shader.SetGlobalVectorArray("_PreviousGrassInteractors", _previous);
            Shader.SetGlobalVectorArray("_GrassInteractorStrengths", _strengths);
            Shader.SetGlobalVectorArray("_PreviousGrassInteractorStrengths", _oldStrengths);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= BeginEditorFrame;
#endif
            _active.Clear();
            _count = _oldCount = 0;
            _frame = -1;
            _subscribed = false;
            Upload();
        }
    }
}
