using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LoogaSoft.Lighting
{
    /// <summary>
    /// Hand-painted impasto for every opaque deferred material, without changing textures. After the G-buffer
    /// is written, a world-space projection of thick brush strokes replaces fine material detail in the stored
    /// normals and lets the paint's height shade cavities and catch highlights on ridges. Looga Lighting then
    /// lights the painted surface. Shadows and ambient occlusion keep the real geometry.
    /// Place it after Looga GTAO in the renderer feature list.
    /// </summary>
    [DisallowMultipleRendererFeature("Looga Impasto")]
    public sealed class LoogaImpastoFeature : ScriptableRendererFeature
    {
        private const string FeatureDisplayName = "Looga Impasto";
        private const string ShaderName = "Hidden/LoogaSoft/Impasto";
        private const string DefaultStrokesName = "LoogaImpastoStrokes";

        [InspectorName("Enable")]
        public bool enable = true;

        [Tooltip("Brush strokes: RG tangent-space normal, B paint height. Create sets with LoogaSoft > Graphics Pro > Impasto > Stroke Generator. Empty uses the package default.")]
        [InspectorName("Stroke Texture")]
        public Texture2D strokeTexture;

        [Tooltip("World size in metres of one tile of the stroke texture.")]
        [InspectorName("Stroke Tile Size"), Range(0.05f, 20f)]
        public float strokeTileSize = 1.5f;

        [Tooltip("Hex cells per stroke tile. Each cell rotates and shifts the strokes, which hides the tiling.")]
        [InspectorName("Cells Per Tile"), Range(0.25f, 4f)]
        public float cellsPerTile = 1f;

        [Tooltip("How much each cell may rotate the strokes. Zero keeps every stroke in the texture's direction.")]
        [InspectorName("Stroke Rotation"), Range(0f, 1f)]
        public float strokeRotation = 1f;

        [Tooltip("How sharply overlapping cells keep the thicker paint instead of cross-fading.")]
        [InspectorName("Overlap Sharpness"), Range(0f, 1f)]
        public float overlapSharpness = 0.7f;

        [Tooltip("Strength of the stroke normals.")]
        [InspectorName("Normal Strength"), Range(0f, 4f)]
        public float normalStrength = 1f;

        [Tooltip("How much fine material detail the paint replaces. Large shapes from the geometry always remain.")]
        [InspectorName("Detail Replacement"), Range(0f, 1f)]
        public float detailReplacement = 0.6f;

        [Tooltip("Blend sharpness between the three projection axes.")]
        [InspectorName("Projection Sharpness"), Range(1f, 16f)]
        public float projectionSharpness = 4f;

        [Tooltip("Ambient occlusion in thin paint between strokes.")]
        [InspectorName("Cavity Occlusion"), Range(0f, 1f)]
        public float cavityOcclusion = 0.5f;

        [Tooltip("Smoothness added on paint ridges and removed in cavities. Negative values make ridges dry and matte.")]
        [InspectorName("Ridge Gloss"), Range(-1f, 1f)]
        public float ridgeGloss = 0.2f;

        [Tooltip("Distance in metres where the paint starts to fade out.")]
        [InspectorName("Fade Start"), Min(0f)]
        public float fadeStart = 60f;

        [Tooltip("Distance in metres where the paint is gone.")]
        [InspectorName("Fade End"), Min(0f)]
        public float fadeEnd = 120f;

        [SerializeField, HideInInspector]
        private Shader _shader;

        [SerializeField, HideInInspector]
        private Texture2D _defaultStrokeTexture;

        private Material _material;
        private ImpastoPass _pass;

#if UNITY_EDITOR
        private void OnValidate()
        {
            bool needsSave = false;
            if (name != FeatureDisplayName)
            {
                name = FeatureDisplayName;
                needsSave = true;
            }

            if (_shader == null)
            {
                _shader = Shader.Find(ShaderName);
                needsSave |= _shader != null;
            }

            if (_defaultStrokeTexture == null)
            {
                string[] guids = AssetDatabase.FindAssets($"{DefaultStrokesName} t:Texture2D");
                if (guids.Length > 0)
                {
                    _defaultStrokeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    needsSave |= _defaultStrokeTexture != null;
                }
            }

            fadeEnd = Mathf.Max(fadeEnd, fadeStart + 0.01f);
            if (needsSave)
                EditorUtility.SetDirty(this);
        }
#endif

        public override void Create()
        {
            name = FeatureDisplayName;
            _pass ??= new ImpastoPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
                return;

            Texture2D strokes = strokeTexture != null ? strokeTexture : _defaultStrokeTexture;
            if (!isActive || !enable || strokes == null || !IsDeferredPlusRenderer(renderer) || !EnsureMaterial())
                return;

            _pass ??= new ImpastoPass();
            _pass.Setup(_material, strokes, this, UsesAccurateGBufferNormals(renderer));
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
            base.Dispose(disposing);
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
                return true;

            if (_shader == null)
                _shader = Shader.Find(ShaderName);

            if (_shader == null)
                return false;

            _material = CoreUtils.CreateEngineMaterial(_shader);
            return _material != null;
        }

        private static bool UsesAccurateGBufferNormals(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("accurateGbufferNormals", flags);
            return property != null && property.PropertyType == typeof(bool) && (bool)property.GetValue(renderer);
        }

        private static bool IsDeferredPlusRenderer(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("renderingModeActual", flags);
            object value = property?.GetValue(renderer);
            return value != null && value.ToString() == "DeferredPlus";
        }

        // Writes painted copies of G-buffer 1 and 2, then copies them back over the originals for Looga Lighting.
        private sealed class ImpastoPass : ScriptableRenderPass
        {
            private const int ApplyShaderPass = 0;
            private const int CopyShaderPass = 1;
            private const int GBufferSpecular = 1;
            private const int GBufferNormals = 2;

            private static readonly int DepthId = Shader.PropertyToID("_LoogaImpastoDepth");
            private static readonly int StencilId = Shader.PropertyToID("_LoogaImpastoStencil");
            private static readonly int GBuffer1Id = Shader.PropertyToID("_LoogaImpastoGBuffer1");
            private static readonly int GBuffer2Id = Shader.PropertyToID("_LoogaImpastoGBuffer2");
            private static readonly int StrokesId = Shader.PropertyToID("_LoogaImpastoStrokes");
            private static readonly int Params0Id = Shader.PropertyToID("_LoogaImpastoParams0");
            private static readonly int Params1Id = Shader.PropertyToID("_LoogaImpastoParams1");
            private static readonly int Params2Id = Shader.PropertyToID("_LoogaImpastoParams2");
            private static readonly int FootprintId = Shader.PropertyToID("_LoogaImpastoFootprint");

            private static readonly ShaderTagId MaskTag = new("LoogaImpastoMask");

            private readonly ProfilingSampler _maskSampler = new("Looga Impasto Mask");
            private readonly ProfilingSampler _applySampler = new("Looga Impasto");
            private readonly ProfilingSampler _copySampler = new("Looga Impasto Write G-Buffer");
            private Material _material;
            private Texture2D _strokes;
            private LoogaImpastoFeature _feature;
            private bool _octahedralNormals;

            private sealed class PassData
            {
                public Material Material;
                public MaterialPropertyBlock Properties;
                public TextureHandle Depth;
                public TextureHandle GBuffer1;
                public TextureHandle GBuffer2;
                public Texture2D Strokes;
                public Vector4 Params0;
                public Vector4 Params1;
                public Vector4 Params2;
                public Vector4 Footprint;
                public int ShaderPass;
            }

            private sealed class MaskPassData
            {
                public RendererListHandle Renderers;
            }

            public ImpastoPass()
            {
                // Before Looga Lighting (BeforeRenderingDeferredLights). Listed after Looga GTAO, it also runs
                // after the ambient occlusion, which then sees the real geometry.
                renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights - 1;
            }

            public void Setup(Material material, Texture2D strokes, LoogaImpastoFeature feature, bool octahedralNormals)
            {
                _material = material;
                _strokes = strokes;
                _feature = feature;
                _octahedralNormals = octahedralNormals;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle[] gBuffer = resourceData.gBuffer;
                TextureHandle depth = resourceData.activeDepthTexture;
                if (gBuffer == null || gBuffer.Length <= GBufferNormals || !gBuffer[GBufferSpecular].IsValid() ||
                    !gBuffer[GBufferNormals].IsValid() || !depth.IsValid())
                {
                    return;
                }

                TextureHandle painted1 = CreateCopyTarget(renderGraph, gBuffer[GBufferSpecular], "Looga Impasto G-Buffer 1");
                TextureHandle painted2 = CreateCopyTarget(renderGraph, gBuffer[GBufferNormals], "Looga Impasto G-Buffer 2");

                Camera camera = cameraData.camera;
                float pixelHeight = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
                Vector4 footprint = camera.orthographic
                    ? new Vector4(0f, 2f * camera.orthographicSize / pixelHeight, 0f, 0f)
                    : new Vector4(2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / pixelHeight, 0f, 0f, 0f);
                // Materials with their own impasto mark their pixels in stencil, which the apply pass then skips.
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                           "Looga Impasto Mask", out MaskPassData passData, _maskSampler))
                {
                    RendererListDesc desc = new(MaskTag, renderingData.cullResults, camera)
                    {
                        renderQueueRange = RenderQueueRange.opaque,
                        sortingCriteria = SortingCriteria.None,
                    };
                    passData.Renderers = renderGraph.CreateRendererList(desc);
                    builder.UseRendererList(passData.Renderers);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                        context.cmd.DrawRendererList(data.Renderers));
                }

                LoogaImpastoFeature f = _feature;
                Vector4 params0 = new(Mathf.Max(f.strokeTileSize, 0.01f), f.normalStrength, f.detailReplacement,
                    f.projectionSharpness);
                Vector4 params1 = new(f.cavityOcclusion, f.ridgeGloss, f.overlapSharpness, f.strokeRotation);
                Vector4 params2 = new(f.fadeStart, Mathf.Max(f.fadeEnd, f.fadeStart + 0.01f), f.cellsPerTile,
                    _octahedralNormals ? 1f : 0f);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                           "Looga Impasto", out PassData passData, _applySampler))
                {
                    Fill(passData, ApplyShaderPass, depth, gBuffer[GBufferSpecular], gBuffer[GBufferNormals]);
                    passData.Params0 = params0;
                    passData.Params1 = params1;
                    passData.Params2 = params2;
                    passData.Footprint = footprint;
                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.UseTexture(gBuffer[GBufferSpecular], AccessFlags.Read);
                    builder.UseTexture(gBuffer[GBufferNormals], AccessFlags.Read);
                    builder.SetRenderAttachment(painted1, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(painted2, 1, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => Draw(data, context));
                }

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                           "Looga Impasto Write G-Buffer", out PassData passData, _copySampler))
                {
                    Fill(passData, CopyShaderPass, depth, painted1, painted2);
                    builder.UseTexture(painted1, AccessFlags.Read);
                    builder.UseTexture(painted2, AccessFlags.Read);
                    builder.SetRenderAttachment(gBuffer[GBufferSpecular], 0, AccessFlags.Write);
                    builder.SetRenderAttachment(gBuffer[GBufferNormals], 1, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => Draw(data, context));
                }
            }

            private void Fill(PassData data, int shaderPass, TextureHandle depth, TextureHandle gBuffer1,
                TextureHandle gBuffer2)
            {
                data.Material = _material;
                data.Properties ??= new MaterialPropertyBlock();
                data.ShaderPass = shaderPass;
                data.Depth = depth;
                data.GBuffer1 = gBuffer1;
                data.GBuffer2 = gBuffer2;
                data.Strokes = _strokes;
            }

            private static TextureHandle CreateCopyTarget(RenderGraph renderGraph, TextureHandle source, string name)
            {
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = name;
                desc.clearBuffer = false;
                desc.depthBufferBits = DepthBits.None;
                return renderGraph.CreateTexture(desc);
            }

            private static void Draw(PassData data, RasterGraphContext context)
            {
                MaterialPropertyBlock properties = data.Properties;
                properties.Clear();
                properties.SetTexture(GBuffer1Id, (RTHandle)data.GBuffer1);
                properties.SetTexture(GBuffer2Id, (RTHandle)data.GBuffer2);
                if (data.ShaderPass == ApplyShaderPass)
                {
                    RTHandle depth = data.Depth;
                    properties.SetTexture(DepthId, depth);
                    properties.SetTexture(StencilId, depth.rt, RenderTextureSubElement.Stencil);
                    properties.SetTexture(StrokesId, data.Strokes);
                    properties.SetVector(Params0Id, data.Params0);
                    properties.SetVector(Params1Id, data.Params1);
                    properties.SetVector(Params2Id, data.Params2);
                    properties.SetVector(FootprintId, data.Footprint);
                }

                context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, data.ShaderPass, MeshTopology.Triangles,
                    3, 1, properties);
            }
        }
    }
}
