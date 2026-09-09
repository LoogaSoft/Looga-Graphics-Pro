var renderer=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/URP_Renderer_Renderer.asset");
var adapter=renderer.rendererFeatures.FirstOrDefault(f=>f.GetType().Name=="KuberaWaterDepthCompatibilityFeature");
var lighting=renderer.rendererFeatures.OfType<LoogaSoft.Lighting.LoogaLightingFeature>().First();
bool adapterEnabled=adapter!=null&&adapter.isActive,sss=lighting.enableSubsurfaceScattering;
var probe=UnityEngine.ScriptableObject.CreateInstance<LoogaDepthIsolationProbe>();
var gtao=UnityEngine.ScriptableObject.CreateInstance<LoogaSoft.Lighting.LoogaGtaoRendererFeature>();
var shadows=UnityEngine.ScriptableObject.CreateInstance<LoogaSoft.Shadows.LoogaShadowRendererFeature>();
var result=new UnityEngine.RenderTexture(512,320,0,UnityEngine.RenderTextureFormat.ARGBFloat,UnityEngine.RenderTextureReadWrite.Linear);result.Create();probe.result=result;
var output=new UnityEngine.RenderTexture(512,320,24,UnityEngine.RenderTextureFormat.ARGBHalf);output.Create();
var cpu=new UnityEngine.Texture2D(512,320,UnityEngine.TextureFormat.RGBAFloat,false,true);
var go=new UnityEngine.GameObject("Depth isolation validation camera"){hideFlags=UnityEngine.HideFlags.HideAndDontSave};
var camera=go.AddComponent<UnityEngine.Camera>();
var viewCamera=UnityEditor.SceneView.lastActiveSceneView.camera;
camera.CopyFrom(viewCamera);camera.transform.SetPositionAndRotation(viewCamera.transform.position,viewCamera.transform.rotation);camera.targetTexture=output;camera.enabled=false;
camera.cameraType=UnityEngine.CameraType.Game;
var cameraData=go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();cameraData.SetRenderer(0);cameraData.requiresDepthTexture=true;
var previous=UnityEngine.RenderTexture.active;
var reports=new System.Collections.Generic.List<string>();
try {
 adapter?.SetActive(false);lighting.enableSubsurfaceScattering=true;
 renderer.rendererFeatures.Add(gtao);renderer.rendererFeatures.Add(shadows);renderer.rendererFeatures.Add(probe);
 System.Action<string,bool,bool> run=(label,ao,shadow)=>{
  gtao.SetActive(ao);shadows.SetActive(shadow);renderer.SetDirty();
  int count=probe.executions;camera.Render();
  if(probe.executions==count)throw new System.Exception("Probe did not execute: "+label);
  UnityEngine.RenderTexture.active=result;cpu.ReadPixels(new UnityEngine.Rect(0,0,512,320),0,0);cpu.Apply();
  var pixels=cpu.GetPixels();int mismatches=pixels.Count(p=>p.r>.5f);int scenePixels=pixels.Count(p=>p.g>0.000001f);
  if(mismatches!=0||scenePixels==0)throw new System.Exception(label+": "+mismatches+" depth mismatches; "+scenePixels+" geometry pixels");
  reports.Add("PASS "+label+": all 163840 pixels match URP scene depth ("+scenePixels+" geometry pixels)");
 };
 run("Lighting and scattering, adapter disabled",false,false);
 run("Lighting/scattering + GTAO, adapter disabled",true,false);
 run("Lighting/scattering + GTAO + shadows, adapter disabled",true,true);
 System.IO.File.WriteAllLines("Temp/LoogaDepthValidation/results.txt",reports);
 return reports;
}
finally {
 renderer.rendererFeatures.Remove(probe);renderer.rendererFeatures.Remove(gtao);renderer.rendererFeatures.Remove(shadows);
 adapter?.SetActive(adapterEnabled);lighting.enableSubsurfaceScattering=sss;renderer.SetDirty();
 UnityEngine.RenderTexture.active=previous;camera.targetTexture=null;
 UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cpu);
 UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(gtao);UnityEngine.Object.DestroyImmediate(shadows);
 result.Release();output.Release();UnityEngine.Object.DestroyImmediate(result);UnityEngine.Object.DestroyImmediate(output);
}
