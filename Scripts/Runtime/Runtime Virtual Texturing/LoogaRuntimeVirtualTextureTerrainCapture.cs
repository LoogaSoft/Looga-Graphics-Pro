using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Rendering.VirtualTexturing
{
    // Terrain writers reuse exported surface producers. Native terrain data remains read-only.
    internal sealed class LoogaRuntimeVirtualTextureTerrainCapture
    {
        internal sealed class Draw
        {
            public Material Material;
            public MaterialPropertyBlock Properties;
            public Vector3 Origin;
            public Vector3 Size;
        }

        private readonly Dictionary<UnityEngine.Terrain, Material> _materials = new();
        public readonly List<Draw> Draws = new();

        public void Prepare(Material[] writers, int layerMask, Vector4 region, UnityEngine.SceneManagement.Scene scene)
        {
            Draws.Clear();
            foreach (UnityEngine.Terrain terrain in UnityEngine.Terrain.activeTerrains)
            {
                if (!terrain || !terrain.terrainData || (layerMask & (1 << terrain.gameObject.layer)) == 0) continue;
                if (scene.IsValid() && terrain.gameObject.scene != scene) continue;
                Material source = terrain.materialTemplate;
                if (!source || !source.shader) continue;
                Material writer = null;
                foreach (Material candidate in writers)
                {
                    if (candidate && candidate.GetTag("LoogaRvtSourceShader", false) == source.shader.name)
                    {
                        writer = candidate;
                        break;
                    }
                }
                if (!writer) continue;
                Vector3 origin = terrain.transform.position;
                TerrainData data = terrain.terrainData;
                Vector3 size = data.size;
                if (origin.x > region.x + region.z || origin.z > region.y + region.z
                    || origin.x + size.x < region.x - region.z || origin.z + size.z < region.y - region.z) continue;
                if (!_materials.TryGetValue(terrain, out Material material) || !material)
                {
                    material = new Material(writer) { hideFlags = HideFlags.HideAndDontSave };
                    _materials[terrain] = material;
                }
                material.shader = writer.shader;
                material.CopyPropertiesFromMaterial(source);
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_LoogaHeightmap", data.heightmapTexture);
                properties.SetTexture("_LoogaHoles", data.holesTexture);
                properties.SetVector("_LoogaOrigin", origin);
                properties.SetVector("_LoogaSize", size);
                properties.SetVector("_LoogaHeightInfo", new Vector4(data.heightmapResolution, 65535f / 32766f, 0, 0));
                properties.SetTexture("_TerrainHeightmapTexture", data.heightmapTexture);
                Texture normal = terrain.normalmapTexture;
                properties.SetTexture("_TerrainNormalmapTexture", normal ? normal : Texture2D.grayTexture);
                properties.SetTexture("_PerPixelNormal", normal ? normal : Texture2D.grayTexture);
                Texture2D[] controls = data.alphamapTextures;
                for (int index = 0; index < 8; index++)
                {
                    properties.SetTexture("_Control" + index, index < controls.Length ? controls[index] : Texture2D.blackTexture);
                }
                Draws.Add(new Draw { Material = material, Properties = properties, Origin = origin, Size = size });
            }
        }

        public void Dispose()
        {
            foreach (Material material in _materials.Values)
            {
                CoreUtils.Destroy(material);
            }
            _materials.Clear();
            Draws.Clear();
        }
    }
}
