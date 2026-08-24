using UnityEngine;

namespace LoogaSoft.Lighting
{
    /// <summary>
    /// Drives the global Looga wind shader uniforms.
    /// Drop this component on any GameObject in your scene (an empty "Wind" GameObject
    /// works fine). The values update every frame in both Play and Edit modes.
    ///
    /// Important: a foliage mesh's vertices need to be above the mesh's local origin
    /// for wind to displace them — the wind weight is a falloff over height. A default
    /// Unity cube has half its vertices below y=0 and won't visibly sway. Use a tall
    /// mesh or one whose pivot is at its base.
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

        static readonly int DirSpeedID         = Shader.PropertyToID("_LoogaWindDirectionAndSpeed");
        static readonly int TurbulenceID       = Shader.PropertyToID("_LoogaWindTurbulence");

        void Update() => Apply();
        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void Apply()
        {
            Vector3 d = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
            Shader.SetGlobalVector(DirSpeedID,   new Vector4(d.x, d.y, d.z, speed));
            Shader.SetGlobalVector(TurbulenceID, new Vector4(swayAmount, flutterFrequency, flutterAmount, 0f));
        }

        void OnDisable()
        {
            // Stop wind and reset SSSS modifiers to safe values when this controller is removed.
            Shader.SetGlobalVector(DirSpeedID,   Vector4.zero);
            Shader.SetGlobalVector(TurbulenceID, Vector4.zero);
        }
    }
}