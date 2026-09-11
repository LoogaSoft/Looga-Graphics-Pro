using LoogaSoft.Rendering.VirtualTexturing;
using NUnit.Framework;
using UnityEngine;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaRuntimeVirtualTextureCaptureTests
    {
        [Test]
        public void DestroyedCameraReleasesItsPersistentAtlases()
        {
            var feature = ScriptableObject.CreateInstance<LoogaRuntimeVirtualTextureRendererFeature>();
            var first = new GameObject("Expired RVT camera");
            var second = new GameObject("Live RVT camera");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                feature.Create();
                object pass = typeof(LoogaRuntimeVirtualTextureRendererFeature).GetField("_pass", flags).GetValue(feature);
                var get = pass.GetType().GetMethod("GetCameraResources", flags);
                object resources = get.Invoke(pass, new object[] { first.AddComponent<Camera>() });
                pass.GetType().GetMethod("EnsureResources", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, new object[] { resources, 512 });
                var atlas = (UnityEngine.Rendering.RTHandle)resources.GetType().GetField("AlbedoAtlas").GetValue(resources);
                RenderTexture texture = atlas.rt;
                Assert.That(texture.IsCreated(), Is.True);
                Object.DestroyImmediate(first);
                get.Invoke(pass, new object[] { second.AddComponent<Camera>() });
                var cameras = (System.Collections.IDictionary)pass.GetType().GetField("_cameraResources", flags).GetValue(pass);
                Assert.That(cameras.Count, Is.EqualTo(1));
                Assert.That(!texture || !texture.IsCreated(), Is.True);
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
                if (first) Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void MovingWriterDoesNotLeaveEmptySpatialCells()
        {
            var type = typeof(LoogaRuntimeVirtualTextureRendererFeature).Assembly.GetType(
                "LoogaSoft.Rendering.VirtualTexturing.LoogaRuntimeVirtualTextureMeshCapture");
            object capture = System.Activator.CreateInstance(type, true);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var update = type.GetMethod("UpdateRenderer");
                // A default scene handle of zero includes all loaded scenes.
                type.GetField("_sceneHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(capture, 0);
                for (int i = 0; i < 100; i++)
                {
                    cube.transform.position = new Vector3(i * 300 + 10, 0, 10);
                    update.Invoke(capture, new object[] { cube.GetComponent<Renderer>() });
                }
                var cells = (System.Collections.IDictionary)type.GetField("_cells",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(capture);
                Assert.That(cells.Count, Is.EqualTo(1));
            }
            finally
            {
                type.GetMethod("Dispose").Invoke(capture, null);
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void LodGroupContributesOnlyItsHighestDetailSurface()
        {
            var type = typeof(LoogaRuntimeVirtualTextureRendererFeature).Assembly.GetType(
                "LoogaSoft.Rendering.VirtualTexturing.LoogaRuntimeVirtualTextureMeshCapture");
            object capture = System.Activator.CreateInstance(type, true);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("RVT LOD fixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                var high = GameObject.CreatePrimitive(PrimitiveType.Cube);
                high.transform.SetParent(root.transform);
                var low = GameObject.CreatePrimitive(PrimitiveType.Cube);
                low.transform.SetParent(root.transform);
                var first = high.GetComponent<Renderer>();
                var second = low.GetComponent<Renderer>();
                first.sharedMaterial = second.sharedMaterial = material;
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.5f, new[] { first }), new LOD(0.01f, new[] { second }) });
                type.GetMethod("Prepare").Invoke(capture, new object[]
                {
                    Shader.Find("Hidden/LoogaSoft/Runtime Virtual Texture/Writer"), -1,
                    new Vector4(0, 0, 32, 0), -10f, 10f, true, scene,
                    new System.Collections.Generic.List<Bounds> { new Bounds(Vector3.zero, Vector3.one * 100) }
                });
                var draws = (System.Collections.IList)type.GetField("Draws").GetValue(capture);
                Assert.That(draws.Count, Is.EqualTo(1));
                Assert.That(draws[0].GetType().GetField("Renderer").GetValue(draws[0]), Is.SameAs(first));
            }
            finally
            {
                type.GetMethod("Dispose").Invoke(capture, null);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TopDownVolumeContainsTerrainOutsideTheViewingFrustum()
        {
            Matrix4x4 view = LoogaRuntimeVirtualTextureMath.GetCaptureView(new Vector2(1000f, 1000f), 700f);
            Matrix4x4 projection = Matrix4x4.Ortho(-512f, 512f, -512f, 512f, 1f, 1000f);
            Vector3 inside = (projection * view).MultiplyPoint(new Vector3(1490f, 350f, 510f));
            Assert.That(Mathf.Abs(inside.x), Is.LessThan(1f));
            Assert.That(Mathf.Abs(inside.y), Is.LessThan(1f));
            Assert.That(Mathf.Abs(inside.z), Is.LessThan(1f));
            Vector3 outside = (projection * view).MultiplyPoint(new Vector3(1600f, 350f, 1000f));
            Assert.That(Mathf.Abs(outside.x), Is.GreaterThan(1f));
        }

        [Test]
        public void CaptureDepthSelectsTheUpperSurface()
        {
            Matrix4x4 view = LoogaRuntimeVirtualTextureMath.GetCaptureView(Vector2.zero, 700f);
            Matrix4x4 gpu = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-64f, 64f, -64f, 64f, 1f, 1000f), true);
            gpu = LoogaRuntimeVirtualTextureMath.ToForwardDepthProjection(gpu, SystemInfo.usesReversedZBuffer);
            float top = (gpu * view).MultiplyPoint(new Vector3(0f, 410f, 0f)).z;
            float bottom = (gpu * view).MultiplyPoint(new Vector3(0f, 390f, 0f)).z;
            Assert.That(top, Is.LessThan(bottom), "A less-equal depth test must keep the upper face.");
        }

        [Test]
        public void PageMovementIgnoresHeightAndSubPageTranslation()
        {
            Vector2 original = LoogaRuntimeVirtualTextureMath.SnapCenter(new Vector3(1000f, 600f, 1000f), 128f, 16);
            Assert.That(LoogaRuntimeVirtualTextureMath.SnapCenter(new Vector3(1007f, 10f, 1007f), 128f, 16), Is.EqualTo(original));
            Assert.That(LoogaRuntimeVirtualTextureMath.SnapCenter(new Vector3(1008f, 600f, 1000f), 128f, 16).x,
                Is.EqualTo(original.x + 8f));
        }
    }
}
