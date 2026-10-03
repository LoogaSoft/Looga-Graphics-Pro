using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    /// <summary>Settings for a tileable set of impasto brush strokes.</summary>
    [Serializable]
    public struct LoogaImpastoStrokeSettings
    {
        [Tooltip("Width and height of the texture in texels.")]
        public int resolution;
        [Tooltip("Seed of the random stroke layout.")]
        public int seed;
        [Tooltip("Average number of strokes covering each texel.")]
        [Range(0.5f, 8f)] public float coverage;
        [Tooltip("Stroke length range as a fraction of the tile.")]
        public Vector2 length;
        [Tooltip("Stroke width range as a fraction of the tile.")]
        public Vector2 width;
        [Tooltip("Main stroke direction in degrees.")]
        [Range(-180f, 180f)] public float direction;
        [Tooltip("Random spread of stroke directions in degrees.")]
        [Range(0f, 180f)] public float directionJitter;
        [Tooltip("How far strokes bend, as a fraction of their length.")]
        [Range(0f, 1f)] public float curvature;
        [Tooltip("Random variation of paint thickness between strokes.")]
        [Range(0f, 1f)] public float thicknessVariation;
        [Tooltip("Grooves the brush bristles leave along each stroke.")]
        [Range(0, 24)] public int bristles;
        [Tooltip("Depth of the bristle grooves.")]
        [Range(0f, 1f)] public float bristleDepth;
        [Tooltip("Ridge of paint pushed up where a stroke ends.")]
        [Range(0f, 1.5f)] public float endRidge;
        [Tooltip("How much strokes narrow toward their end.")]
        [Range(0f, 1f)] public float taper;
        [Tooltip("How much new paint sits on top of earlier strokes instead of replacing them.")]
        [Range(0f, 1f)] public float layering;
        [Tooltip("Steepness of the normals derived from the paint height.")]
        [Range(0.1f, 16f)] public float normalStrength;
        [Tooltip("Soft edge of each stroke in texels.")]
        [Range(0.5f, 4f)] public float edgeSoftness;

        public static LoogaImpastoStrokeSettings Default => new()
        {
            resolution = 1024,
            seed = 1,
            coverage = 4.5f,
            length = new Vector2(0.12f, 0.3f),
            width = new Vector2(0.035f, 0.075f),
            direction = 35f,
            directionJitter = 40f,
            curvature = 0.3f,
            thicknessVariation = 0.4f,
            bristles = 7,
            bristleDepth = 0.25f,
            endRidge = 0.6f,
            taper = 0.5f,
            layering = 0.35f,
            normalStrength = 4f,
            edgeSoftness = 1.5f
        };
    }

    /// <summary>
    /// Paints tileable impasto brush strokes into a height field and derives their normals. The result packs
    /// the tangent-space normal XY in RG and the paint height in B, the layout Looga Impasto reads.
    /// </summary>
    public static class LoogaImpastoStrokeGenerator
    {
        public static Color32[] Generate(LoogaImpastoStrokeSettings settings)
        {
            int size = Mathf.Clamp(settings.resolution, 64, 4096);
            float[] height = PaintHeight(settings, size);
            return Encode(height, size, settings.normalStrength);
        }

        public static Texture2D CreateTexture(LoogaImpastoStrokeSettings settings)
        {
            int size = Mathf.Clamp(settings.resolution, 64, 4096);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                name = "Impasto Strokes"
            };
            texture.SetPixels32(Generate(settings));
            texture.Apply(true);
            return texture;
        }

        /// <summary>Writes the strokes to a PNG in the project and imports it with the settings Looga Impasto expects.</summary>
        public static Texture2D Save(LoogaImpastoStrokeSettings settings, string assetPath)
        {
            int size = Mathf.Clamp(settings.resolution, 64, 4096);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(Generate(settings));
            texture.Apply(false);
            File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = Mathf.Max(size, 32);
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static float[] PaintHeight(LoogaImpastoStrokeSettings s, int size)
        {
            var random = new System.Random(s.seed);
            float Next() => (float)random.NextDouble();
            float Range(Vector2 range) => Mathf.Lerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), Next());

            var height = new float[size * size];
            float averageLength = (s.length.x + s.length.y) * 0.5f * size;
            float averageWidth = (s.width.x + s.width.y) * 0.5f * size;
            int strokeCount = Mathf.Max(1, Mathf.RoundToInt(s.coverage * size * size /
                Mathf.Max(averageLength * averageWidth, 1f)));
            float edge = Mathf.Max(s.edgeSoftness, 0.5f);

            for (int stroke = 0; stroke < strokeCount; stroke++)
            {
                float centerX = Next() * size;
                float centerY = Next() * size;
                float angle = (s.direction + (Next() * 2f - 1f) * s.directionJitter) * Mathf.Deg2Rad;
                var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var across = new Vector2(-along.y, along.x);
                float length = Mathf.Max(Range(s.length) * size, 2f);
                float width = Mathf.Max(Range(s.width) * size, 2f);
                float bend = (Next() * 2f - 1f) * s.curvature;
                float thickness = 1f - s.thicknessVariation * Next();
                float phase = Next() * Mathf.PI * 2f;
                float wobblePhase = Next() * Mathf.PI * 2f;
                // Each brush has its own bristle count; a detuned second set keeps grooves from looking combed.
                float bristleFrequency = s.bristles * Mathf.Lerp(0.6f, 1.4f, Next());
                int reach = Mathf.CeilToInt(length * 0.5f + width + Mathf.Abs(bend) * length * 0.25f + edge);

                for (int offsetY = -reach; offsetY <= reach; offsetY++)
                {
                    int y = Wrap(Mathf.FloorToInt(centerY) + offsetY, size);
                    float dy = Mathf.FloorToInt(centerY) + offsetY + 0.5f - centerY;
                    for (int offsetX = -reach; offsetX <= reach; offsetX++)
                    {
                        float dx = Mathf.FloorToInt(centerX) + offsetX + 0.5f - centerX;
                        float u = dx * along.x + dy * along.y;
                        float t = u / length + 0.5f;
                        if (t < -edge / length || t > 1f + edge / length)
                            continue;

                        // The centreline bows sideways by bend, most in the middle of the stroke.
                        float relative = u / length;
                        float centreline = bend * length * (0.25f - relative * relative);
                        float v = dx * across.x + dy * across.y - centreline;
                        float clampedT = Mathf.Clamp01(t);
                        // A rounded start where the loaded brush lands, narrowing toward the lift-off.
                        float halfWidth = width * 0.5f * Mathf.Sqrt(Mathf.Clamp01(clampedT / 0.05f)) *
                            (1f - s.taper * clampedT * clampedT) *
                            (1f + 0.12f * Mathf.Sin(clampedT * 9.7f + wobblePhase));
                        if (halfWidth <= 0.25f || Mathf.Abs(v) > halfWidth + edge)
                            continue;

                        float mask = Mathf.Clamp01((halfWidth - Mathf.Abs(v)) / edge + 0.5f) *
                            Mathf.Clamp01(t * length / edge + 0.5f) *
                            Mathf.Clamp01((1f - t) * length / edge + 0.5f);
                        if (mask <= 0f)
                            continue;

                        float x = Mathf.Clamp(v / halfWidth, -1f, 1f);
                        float dome = Mathf.Pow(Mathf.Clamp01(1f - x * x), 0.35f);
                        // Bristle grooves deepen as the brush runs dry toward the end.
                        float bristleX = x * 0.5f + 0.5f + 0.04f * Mathf.Sin(clampedT * 13.1f + wobblePhase);
                        float bristlePattern =
                            0.65f * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * 2f * bristleFrequency * bristleX + phase)) +
                            0.35f * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * 2f * bristleFrequency * 2.37f * bristleX + phase * 1.7f));
                        float groove = s.bristles > 0
                            ? 1f - s.bristleDepth * (0.5f + 0.5f * clampedT) * bristlePattern
                            : 1f;
                        float endDistance = (1f - clampedT) / 0.06f;
                        float startDistance = clampedT / 0.08f;
                        float alongProfile = Mathf.Lerp(1f, 0.55f, clampedT) +
                            s.endRidge * Mathf.Exp(-endDistance * endDistance) +
                            0.25f * Mathf.Exp(-startDistance * startDistance);
                        float strokeHeight = thickness * dome * groove * alongProfile;

                        int index = y * size + Wrap(Mathf.FloorToInt(centerX) + offsetX, size);
                        float previous = height[index];
                        height[index] = Mathf.Lerp(previous, previous * s.layering + strokeHeight, mask);
                    }
                }
            }

            Normalize(height);
            return height;
        }

        // Scales the paint into [0, 1] by a high percentile, so a few tall ridges do not flatten the rest.
        private static void Normalize(float[] height)
        {
            var sorted = (float[])height.Clone();
            Array.Sort(sorted);
            float top = Mathf.Max(sorted[Mathf.Clamp((int)(sorted.Length * 0.995f), 0, sorted.Length - 1)], 0.0001f);
            for (int i = 0; i < height.Length; i++)
                height[i] = Mathf.Clamp01(height[i] / top);
        }

        private static Color32[] Encode(float[] height, int size, float normalStrength)
        {
            var pixels = new Color32[size * size];
            // Slopes per texel shrink with resolution; scale them so a set looks the same at any size.
            float slopeScale = normalStrength * size / 512f;
            for (int y = 0; y < size; y++)
            {
                int up = Wrap(y + 1, size) * size;
                int down = Wrap(y - 1, size) * size;
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    float dx = (height[row + Wrap(x + 1, size)] - height[row + Wrap(x - 1, size)]) * 0.5f;
                    float dy = (height[up + x] - height[down + x]) * 0.5f;
                    var normal = new Vector3(-dx * slopeScale, -dy * slopeScale, 1f).normalized;
                    pixels[row + x] = new Color32(
                        (byte)Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt(height[row + x] * 255f),
                        255);
                }
            }

            return pixels;
        }

        private static int Wrap(int value, int size)
        {
            int result = value % size;
            return result < 0 ? result + size : result;
        }
    }

    public sealed class LoogaImpastoStrokeGeneratorWindow : EditorWindow
    {
        [SerializeField] private LoogaImpastoStrokeSettings _settings = LoogaImpastoStrokeSettings.Default;
        private Texture2D _preview;
        private Texture2D _heightPreview;
        private Vector2 _scroll;
        private bool _showHeight;

        [MenuItem("LoogaSoft/Graphics Pro/Impasto/Stroke Generator", priority = 25)]
        private static void Open()
        {
            GetWindow<LoogaImpastoStrokeGeneratorWindow>("Impasto Strokes").Show();
        }

        private void OnDisable()
        {
            DestroyImmediate(_preview);
            DestroyImmediate(_heightPreview);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var serialized = new SerializedObject(this);
            SerializedProperty settings = serialized.FindProperty(nameof(_settings));
            EditorGUILayout.PropertyField(settings, new GUIContent("Strokes"), true);
            serialized.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview"))
                    BuildPreview();
                if (GUILayout.Button("Reset"))
                {
                    _settings = LoogaImpastoStrokeSettings.Default;
                    BuildPreview();
                }
                if (GUILayout.Button("Save As..."))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Impasto Strokes", "Impasto Strokes", "png",
                        "Choose where to save the stroke texture.");
                    if (!string.IsNullOrEmpty(path))
                        EditorGUIUtility.PingObject(LoogaImpastoStrokeGenerator.Save(_settings, path));
                }
            }

            if (_preview != null)
            {
                _showHeight = EditorGUILayout.Toggle("Show Height", _showHeight);
                float side = Mathf.Min(position.width - 20f, 512f);
                Rect rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, _showHeight ? _heightPreview : _preview);
                EditorGUILayout.HelpBox(
                    "Assign the saved texture to Stroke Texture on the Looga Impasto renderer feature. RG holds the " +
                    "stroke normal and B the paint height.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void BuildPreview()
        {
            DestroyImmediate(_preview);
            DestroyImmediate(_heightPreview);
            LoogaImpastoStrokeSettings preview = _settings;
            preview.resolution = Mathf.Min(_settings.resolution, 512);
            Color32[] pixels = LoogaImpastoStrokeGenerator.Generate(preview);
            int size = Mathf.Clamp(preview.resolution, 64, 4096);
            _preview = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat };
            _heightPreview = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat };
            var heights = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte h = pixels[i].b;
                heights[i] = new Color32(h, h, h, 255);
                pixels[i].b = 255;
            }
            _preview.SetPixels32(pixels);
            _preview.Apply(false);
            _heightPreview.SetPixels32(heights);
            _heightPreview.Apply(false);
        }
    }
}
