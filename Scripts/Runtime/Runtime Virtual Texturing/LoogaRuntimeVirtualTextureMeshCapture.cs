using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LoogaSoft.Rendering.VirtualTexturing
{
    // Native terrain vegetation batches cannot use the generic override shader in player builds.
    internal sealed class LoogaRuntimeVirtualTextureMeshCapture
    {
        internal sealed class Draw
        {
            public Renderer Renderer;
            public Material Material;
            public int Submesh;
            public Bounds Bounds;
        }

        private sealed class Entry
        {
            public Renderer Renderer;
            public Bounds Bounds;
            public readonly List<Vector2Int> Cells = new();
            public LODGroup LodGroup;
            public bool PrimaryLod = true;
        }

        private const float CellSize = 256f;
        private readonly Dictionary<Renderer, Entry> _entries = new();
        private readonly Dictionary<Vector2Int, List<Entry>> _cells = new();
        private readonly List<Entry> _largeEntries = new();
        private readonly HashSet<Entry> _visited = new();
        private readonly List<Entry> _candidates = new();
        private readonly Dictionary<Material, Material> _materials = new();
        private readonly List<Material> _sharedMaterials = new();
        private readonly HashSet<Material> _preparedMaterials = new();
        private readonly List<Renderer> _renderers = new();
        private int _sceneHandle = int.MinValue;
        private bool _discoveryDirty = true;
        public readonly List<Draw> Draws = new();
        public int DiscoveryCount { get; private set; }
        public int CandidateCount => _candidates.Count;

        #region Index maintenance
        public void InvalidateDiscovery()
        {
            _discoveryDirty = true;
        }

        public void UpdateRenderer(Renderer renderer)
        {
            if (!renderer) return;
            if (_entries.Remove(renderer, out Entry old))
            {
                foreach (Vector2Int cell in old.Cells)
                {
                    List<Entry> entries = _cells[cell];
                    entries.Remove(old);
                    if (entries.Count == 0)
                    {
                        _cells.Remove(cell);
                    }
                }
                _largeEntries.Remove(old);
            }
            if (_sceneHandle == 0 || renderer.gameObject.scene.handle == _sceneHandle)
            {
                AddRenderer(renderer);
            }
        }

        private void AddRenderer(Renderer renderer)
        {
            if (!renderer || (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)) return;
            Mesh mesh = GetMesh(renderer);
            if (!mesh || mesh.subMeshCount == 0) return;
            var entry = new Entry { Renderer = renderer, Bounds = renderer.bounds };
            entry.LodGroup = renderer.GetComponentInParent<LODGroup>();
            if (entry.LodGroup)
            {
                LOD[] lods = entry.LodGroup.GetLODs();
                bool member = false;
                bool primary = false;
                for (int index = 0; index < lods.Length; index++)
                {
                    foreach (Renderer candidate in lods[index].renderers)
                    {
                        if (candidate != renderer) continue;
                        member = true;
                        primary |= index == 0;
                    }
                }
                entry.PrimaryLod = !member || primary;
            }
            _entries[renderer] = entry;
            Vector2Int minimum = Cell(entry.Bounds.min);
            Vector2Int maximum = Cell(entry.Bounds.max);
            if ((long)(maximum.x - minimum.x + 1) * (maximum.y - minimum.y + 1) > 256)
            {
                _largeEntries.Add(entry);
                return;
            }
            for (int y = minimum.y; y <= maximum.y; y++)
            {
                for (int x = minimum.x; x <= maximum.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!_cells.TryGetValue(cell, out List<Entry> entries))
                    {
                        entries = new List<Entry>();
                        _cells.Add(cell, entries);
                    }
                    entries.Add(entry);
                    entry.Cells.Add(cell);
                }
            }
        }

        private void Discover(Scene scene)
        {
            foreach (Material material in _materials.Values)
            {
                CoreUtils.Destroy(material);
            }
            _materials.Clear();
            _preparedMaterials.Clear();
            _entries.Clear();
            _cells.Clear();
            _largeEntries.Clear();
            _renderers.Clear();
            _sceneHandle = scene.IsValid() ? scene.handle : 0;
            if (scene.IsValid())
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, _renderers);
                    foreach (Renderer renderer in _renderers)
                    {
                        AddRenderer(renderer);
                    }
                }
            }
            else
            {
                foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    AddRenderer(renderer);
                }
            }
            _discoveryDirty = false;
            DiscoveryCount++;
        }
        #endregion

        #region Capture
        public void Prepare(Shader writer, int layerMask, Vector4 region, float minimumHeight,
            float maximumHeight, bool alphaTested, Scene scene, IReadOnlyList<Bounds> updates)
        {
            if (_discoveryDirty || _sceneHandle != (scene.IsValid() ? scene.handle : 0))
            {
                Discover(scene);
            }
            Draws.Clear();
            _preparedMaterials.Clear();
            _visited.Clear();
            _candidates.Clear();
            Bounds volume = new(new Vector3(region.x, (minimumHeight + maximumHeight) * 0.5f, region.y),
                new Vector3(region.z * 2f, maximumHeight - minimumHeight, region.z * 2f));
            Vector2Int minimum = Cell(volume.min);
            Vector2Int maximum = Cell(volume.max);
            for (int y = minimum.y; y <= maximum.y; y++)
            {
                for (int x = minimum.x; x <= maximum.x; x++)
                {
                    if (!_cells.TryGetValue(new Vector2Int(x, y), out List<Entry> entries))
                    {
                        continue;
                    }
                    foreach (Entry entry in entries)
                    {
                        if (_visited.Add(entry))
                        {
                            _candidates.Add(entry);
                        }
                    }
                }
            }
            _candidates.AddRange(_largeEntries);
            foreach (Entry entry in _candidates)
            {
                Renderer renderer = entry.Renderer;
                if (!renderer)
                {
                    _discoveryDirty = true;
                    continue;
                }
                if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                // A persistent cache uses LOD 0, independent of the viewing camera.
                if (entry.LodGroup && entry.LodGroup.enabled && !entry.PrimaryLod)
                {
                    continue;
                }
                if ((layerMask & (1 << renderer.gameObject.layer)) == 0 || !volume.Intersects(entry.Bounds) || !IntersectsUpdates(entry.Bounds, updates))
                {
                    continue;
                }
                Mesh mesh = GetMesh(renderer);
                if (!mesh)
                {
                    continue;
                }
                renderer.GetSharedMaterials(_sharedMaterials);
                for (int submesh = 0; submesh < _sharedMaterials.Count; submesh++)
                {
                    Material source = _sharedMaterials[submesh];
                    if (!source || !source.HasProperty("_BaseColor"))
                    {
                        continue;
                    }
                    if (source.renderQueue > (int)RenderQueue.GeometryLast)
                    {
                        continue;
                    }
                    if (!alphaTested && source.renderQueue >= (int)RenderQueue.AlphaTest)
                    {
                        continue;
                    }
                    if (source.HasProperty("_LoogaRvtReceiverOnly") && source.GetFloat("_LoogaRvtReceiverOnly") > 0.5f)
                    {
                        continue;
                    }
                    if (!_materials.TryGetValue(source, out Material material) || !material)
                    {
                        material = new Material(writer) { hideFlags = HideFlags.HideAndDontSave };
                        _materials[source] = material;
                    }
                    if (_preparedMaterials.Add(source))
                    {
                        material.CopyPropertiesFromMaterial(source);
                        material.shader = writer;
                        material.enableInstancing = false;
                    }
                    Draws.Add(new Draw { Renderer = renderer, Material = material, Bounds = entry.Bounds, Submesh = Mathf.Min(submesh, mesh.subMeshCount - 1) });
                }
            }
        }

        private static bool IntersectsUpdates(Bounds bounds, IReadOnlyList<Bounds> updates)
        {
            foreach (Bounds update in updates)
            {
                if (bounds.Intersects(update))
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector2Int Cell(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }
        #endregion

        public void Dispose()
        {
            foreach (Material material in _materials.Values)
            {
                CoreUtils.Destroy(material);
            }
            _materials.Clear();
            _preparedMaterials.Clear();
            _entries.Clear();
            _cells.Clear();
            _largeEntries.Clear();
            _visited.Clear();
            _candidates.Clear();
            _renderers.Clear();
            Draws.Clear();
        }
    }
}
