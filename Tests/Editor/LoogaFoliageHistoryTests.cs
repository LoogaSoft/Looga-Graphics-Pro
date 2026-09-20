using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LoogaSoft.Lighting.Tests
{
    public sealed class LoogaFoliageHistoryTests
    {
        [UnityTest]
        public IEnumerator InteractionHistoryPreservesAllCamerasAndRetiresDisabledSources()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Interaction history fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                var interactor = root.AddComponent<LoogaGrassInteractor>();
                interactor.Configure(2, 0.25f);
                LoogaGrassInteractor.Publish();
                yield return NextEditorUpdate();
                LoogaGrassInteractor.Publish();
                root.transform.position = Vector3.right * 3;
                interactor.Configure(2, 0.75f);
                LoogaGrassInteractor.Publish();
                Assert.AreEqual(3, Shader.GetGlobalVectorArray("_GrassInteractors")[0].x);
                Assert.AreEqual(0, Shader.GetGlobalVectorArray("_PreviousGrassInteractors")[0].x);
                Assert.AreEqual(0.25f, Shader.GetGlobalVectorArray("_PreviousGrassInteractorStrengths")[0].x);
                LoogaGrassInteractor.Publish();
                Assert.AreEqual(0, Shader.GetGlobalVectorArray("_PreviousGrassInteractors")[0].x);
                yield return NextEditorUpdate();
                LoogaGrassInteractor.Publish();
                Assert.AreEqual(3, Shader.GetGlobalVectorArray("_PreviousGrassInteractors")[0].x);
                Assert.AreEqual(0.75f, Shader.GetGlobalVectorArray("_PreviousGrassInteractorStrengths")[0].x);
                interactor.enabled = false;
                LoogaGrassInteractor.Publish();
                Assert.Zero(Shader.GetGlobalInt("_GrassInteractorCount"));
                yield return NextEditorUpdate();
                LoogaGrassInteractor.Publish();
                Assert.Zero(Shader.GetGlobalInt("_PreviousGrassInteractorCount"));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                LoogaGrassInteractor.Publish();
            }
        }
        [UnityTest]
        public IEnumerator WindHistoryKeepsThePreviousFrameAfterSameFrameEdits()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Wind history fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                var wind = root.AddComponent<LoogaWindController>();
                wind.speed = 2;
                wind.Apply();
                yield return NextEditorUpdate();
                wind.Apply();
                wind.speed = 4;
                wind.Apply();
                LoogaWindController.Publish();
                Assert.AreEqual(4, Shader.GetGlobalVector("_LoogaWindDirectionAndSpeed").w);
                Assert.AreEqual(2, Shader.GetGlobalVector("_LoogaPreviousWindDirectionAndSpeed").w);
                yield return NextEditorUpdate();
                LoogaWindController.Publish();
                Assert.AreEqual(4, Shader.GetGlobalVector("_LoogaPreviousWindDirectionAndSpeed").w);
                wind.enabled = false;
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_LoogaWindTurbulence"));
                yield return NextEditorUpdate();
                LoogaWindController.Publish();
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_LoogaPreviousWindTurbulence"));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                LoogaWindController.Publish();
            }
        }
        private static IEnumerator NextEditorUpdate()
        {
            bool advanced = false;
            void Updated() => advanced = true;
            EditorApplication.update += Updated;
            try
            {
                while (!advanced)
                {
                    yield return null;
                }
            }
            finally
            {
                EditorApplication.update -= Updated;
            }
        }
    }
}
