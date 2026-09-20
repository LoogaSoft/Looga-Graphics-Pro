using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting
{
    /// <summary>
    /// Publish global wind inputs in Play and Edit modes.
    /// Wind weights use vertex height above the mesh origin. Place vegetation pivots at the base.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Looga Wind Controller")]
    public class LoogaWindController : MonoBehaviour
    {
        [Header("Wind Direction & Speed")]
        [Tooltip("World-space direction the wind blows. Magnitude is ignored.")]
        public Vector3 direction = new Vector3(1f, 0f, 0.3f);

        [Tooltip("Speed of the rolling wind cycle. 0 = static, 5 = very fast.")]
        [Range(0f, 5f)] public float speed = 1.0f;

        [Header("Wind Turbulence")]
        [Tooltip("Sway amplitude in meters. 0.25 is a gentle breeze, 0.3 is a strong wind.")]
        [Range(0f, 5f)] public float swayAmount = 0.25f;

        [Tooltip("Flutter frequency in Hz. Affects how fast individual leaves vibrate.")]
        [Range(0f, 10f)] public float flutterFrequency = 4.0f;

        [Tooltip("Flutter amplitude in meters. Only affects vertices with non-zero flutter mask.")]
        [Range(0f, 2f)] public float flutterAmount = 0.15f;

        private static int _frame = -1;
        private static bool _listening;
        private static Vector4 _currentDirection;
        private static Vector4 _currentTurbulence;
        private static Vector4 _previousDirection;
        private static Vector4 _previousTurbulence;

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

        private void Update() => Apply();
        private void OnEnable()
        {
            EnsureListening();
            Apply();
        }
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Apply();
        }

        private static void BeforeCamera(ScriptableRenderContext context, Camera camera) => Publish();

        /// <summary>Publish current and previous wind inputs for a rendering camera.</summary>
        public static void Publish()
        {
            AdvanceHistory();
            Upload();
            if (_currentTurbulence == Vector4.zero && _previousTurbulence == Vector4.zero)
            {
                RenderPipelineManager.beginCameraRendering -= BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= BeginEditorFrame;
#endif
                _listening = false;
            }
        }

        private static void EnsureListening()
        {
            if (_listening) return;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += BeginEditorFrame;
#endif
            _listening = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHistory()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= BeginEditorFrame;
#endif
            _listening = false;
            _frame = -1;
            _currentDirection = _previousDirection = Vector4.zero;
            _currentTurbulence = _previousTurbulence = Vector4.zero;
            Upload();
        }

        /// <summary>Apply the authored wind values without changing the previous frame inputs.</summary>
        public void Apply()
        {
            EnsureListening();
            AdvanceHistory();
            Vector3 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
            _currentDirection = new Vector4(normalized.x, normalized.y, normalized.z, speed);
            _currentTurbulence = new Vector4(swayAmount, flutterFrequency, flutterAmount, 0);
            Upload();
        }

        private static void AdvanceHistory()
        {
            if (_frame == Frame) return;
            _previousDirection = _currentDirection;
            _previousTurbulence = _currentTurbulence;
            _frame = Frame;
        }

        private static void Upload()
        {
            Shader.SetGlobalVector("_LoogaWindDirectionAndSpeed", _currentDirection);
            Shader.SetGlobalVector("_LoogaWindTurbulence", _currentTurbulence);
            Shader.SetGlobalVector("_LoogaPreviousWindDirectionAndSpeed", _previousDirection);
            Shader.SetGlobalVector("_LoogaPreviousWindTurbulence", _previousTurbulence);
        }

        private void OnDisable()
        {
            AdvanceHistory();
            _currentDirection = Vector4.zero;
            _currentTurbulence = Vector4.zero;
            Upload();
        }
    }
}
