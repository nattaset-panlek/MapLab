using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabLayerBaker
    {
        #region [Variables/Fields]
        public const string RootName = "[MapLab] Bake Layers";
        public const string LayerPrefix = "Layer: ";
        public const string PaintPrefix = "Paint: ";

        private const string ChunkPrefix = "Chunk";
        #endregion

        #region [Public Methods]
        public static Transform Root(MapLabMap map, bool create)
        {
            foreach (Transform child in map.transform)
            {
                if (child.name == RootName && child.GetComponent<MapLabChunkStreamer>() != null) return child;
            }
            if (!create) return null;
            var root = new GameObject(RootName) { layer = map.gameObject.layer };
            root.transform.SetParent(map.transform, false);
            root.AddComponent<MapLabChunkStreamer>().SetViewer(MapLabSceneBuilder.FindCamera(map.gameObject.scene));
            return root.transform;
        }

        public static MapLabBakedLayer BakedLayer(MapLabMap map, string layer)
        {
            return map.GetComponentsInChildren<MapLabBakedLayer>(true).FirstOrDefault(baked => !IsLegacy(baked) && MapLabLayerStack.Same(baked.LayerName, layer));
        }

        public static MapLabChunkLayer PaintLayer(MapLabMap map, string paint)
        {
            return map.GetComponentsInChildren<MapLabChunkLayer>(true).FirstOrDefault(layer => layer.IsPaint && MapLabLayerStack.Same(layer.PaintName, paint));
        }

        public static string Bake(MapLabMap map, string layerName)
        {
            return Bake(map, layerName, null);
        }

        public static string Bake(MapLabMap map, string layerName, ICollection<Vector2Int> megas)
        {
            return Bake(map, layerName, megas, false);
        }

        public static string Bake(MapLabMap map, string layerName, ICollection<Vector2Int> megas, bool full)
        {
            Scene scene = map.gameObject.scene;
            if (!MapLabLayerStack.Saved(map)) throw new InvalidOperationException("Save the scene before baking: MapLab writes the layer images into a folder next to it.");
            MapLabLayerStack.EnsureLayers(map);
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layerName);
            if (settings == null) throw new InvalidOperationException($"'{map.name}' has no bake layer called '{layerName}'.");
            if (settings.Locked) return $"[MapLab] Layer '{settings.Name}' is locked: unlock it to bake it again";
            MapLabDrawLayers.Sync(map);

            RemoveLegacy(map);
            MapLabBakedLayer previous = BakedLayer(map, settings.Name);
            if (previous != null) Restore(previous);
            foreach (var shape in map.GetComponentsInChildren<PointShape>(true)) shape.Rebuild();
            List<Renderer> claimed = MapLabLayerClaims.Claimed(map, settings.Name);
            if (claimed.Count == 0)
            {
                SyncScene(map);
                string what = settings.Sorted ? "no y-sorted shape" : "no ground object";
                return $"[MapLab] Layer '{settings.Name}' of '{map.name}': nothing to bake ({what} matches its kinds and sets)";
            }
            if (settings.Sorted) return MapLabSortedBaker.Bake(map, settings, claimed);

            int pixelsPerUnit = map.BakePixelsPerUnit;
            float chunkSize = MapLabChunkRenderer.SnappedChunkSize(map);
            int perMega = MapLabLayerFiles.PerMega(map);
            string folder = MapLabLayerFiles.BaseFolder(map, settings.Name);
            string data = MapLabLayerFiles.DataFolder(map, settings.Name);
            var watch = Stopwatch.StartNew();
            MapLabLayerFiles.Regroup(folder, perMega);
            Dictionary<Vector2Int, string> existing = MapLabLayerFiles.Chunks(folder);
            string key = MapLabChunkSignatures.Settings(chunkSize, pixelsPerUnit);
            Dictionary<Vector2Int, Hash128> current = MapLabChunkSignatures.Compute(claimed, MapLabChunkCoverage.Map(claimed, chunkSize), key);
            Dictionary<Vector2Int, MapLabChunkSignatures.Entry> saved = full ? null : MapLabChunkSignatures.Load(data, key);
            HashSet<Vector2Int> chosen = megas != null && (saved != null || existing.Count == 0) ? new HashSet<Vector2Int>(megas) : null;
            bool InScope(Vector2Int index) => chosen == null || chosen.Contains(MegaChunkFormula.MegaOfChunk(index, perMega));

            var dirty = new List<Vector2Int>();
            foreach (var pair in current)
            {
                if (!InScope(pair.Key)) continue;
                bool fresh = saved != null && saved.TryGetValue(pair.Key, out MapLabChunkSignatures.Entry entry) && entry.Hash == pair.Value && entry.Empty != existing.ContainsKey(pair.Key);
                if (!fresh) dirty.Add(pair.Key);
            }
            List<Vector2Int> removed = existing.Keys.Where(index => InScope(index) && !current.ContainsKey(index)).ToList();
            List<Vector2Int> replaced = dirty.Concat(removed).Where(existing.ContainsKey).ToList();
            Dictionary<Vector2Int, Hash128> before = MapLabLayerFiles.Hashes(existing, replaced);
            MapLabLayerFiles.ArchiveChunks(map, settings.Name, replaced);
            Dictionary<Vector2Int, string> rendered = MapLabChunkRenderer.Render(scene, claimed, dirty, chunkSize, pixelsPerUnit, folder, perMega);
            MapLabLayerFiles.ConfigureChunks(rendered.Values, pixelsPerUnit, MapLabChunkRenderer.ChunkPixels(chunkSize, pixelsPerUnit));

            var next = saved != null ? new Dictionary<Vector2Int, MapLabChunkSignatures.Entry>(saved) : new Dictionary<Vector2Int, MapLabChunkSignatures.Entry>();
            foreach (var index in next.Keys.Where(index => InScope(index) && !current.ContainsKey(index)).ToList()) next.Remove(index);
            foreach (var index in dirty) next[index] = new MapLabChunkSignatures.Entry(current[index], !rendered.ContainsKey(index));
            MapLabChunkSignatures.Save(data, key, next);

            Dictionary<Vector2Int, string> written = MapLabLayerFiles.Chunks(folder);
            MapLabLayerFiles.EnsureSprites(written.Values, chunkSize);
            Dictionary<Vector2Int, Hash128> after = MapLabLayerFiles.Hashes(written, dirty.Concat(removed));
            int warnings = UpdateChangedUnderPaint(map, settings, before, after, existing.Count > 0);
            foreach (var paint in MapLabLayerFiles.PaintNames(map, settings.Name)) MapLabLayerFiles.Regroup(MapLabLayerFiles.PaintFolder(map, settings.Name, paint), perMega);

            Renderer lowest = claimed.Aggregate((a, b) => MapLabLayerStack.Compare(MapLabLayerClaims.SortKey(a), MapLabLayerClaims.SortKey(b)) <= 0 ? a : b);
            MapLabBakedLayer layer = BuildBakedLayer(map, settings.Name, written, chunkSize, pixelsPerUnit);
            layer.SetSourceSorting(MapLabLayerClaims.SortingLayerOf(lowest), MapLabLayerClaims.SortingOrderOf(lowest));
            MapLabStaticBaker.HideSources(layer, claimed);
            MapLabBakeStatus.RecordStaticStamp(map, layer);
            MarkFloorBaked(map, layer, chosen);
            SyncScene(map);
            watch.Stop();
            EditorSceneManager.MarkSceneDirty(scene);

            string warning = warnings > 0 ? $"; {warnings} painted chunks changed underneath, check them in the Layers page" : "";
            string scope = chosen != null ? $" in {megas.Count} mega chunks" : "";
            int unchanged = current.Count(pair => InScope(pair.Key)) - dirty.Count;
            string work = $"rendered {dirty.Count} changed chunks{scope} ({dirty.Count - rendered.Count} came out empty), kept {unchanged} unchanged, removed {removed.Count}";
            return $"[MapLab] Baked layer '{settings.Name}' of '{map.name}': {work}; {claimed.Count} objects in {written.Count} chunks ({chunkSize:0.##} units, {pixelsPerUnit} px/unit) in {folder} ({watch.Elapsed.TotalSeconds:0.0} s){warning}";
        }

        public static string Unbake(MapLabMap map, string layerName)
        {
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layerName);
            if (settings != null && settings.Locked) return $"[MapLab] Layer '{settings.Name}' is locked: unlock it to show it live";
            MapLabBakedLayer layer = BakedLayer(map, layerName);
            if (layer == null) return $"[MapLab] Layer '{layerName}' of '{map.name}' is not baked";
            int restored = Restore(layer);
            MapLabDrawLayers.Sync(map);
            SyncScene(map);
            if (MapLabLayerStack.Saved(map)) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            return $"[MapLab] Layer '{layerName}' of '{map.name}' is live again: {restored} sources shown (images stay on disk)";
        }

        public static (int Layers, int Restored) UnbakeAll(MapLabMap map, bool includeLocked)
        {
            int layers = 0;
            int restored = RemoveLegacy(map);
            foreach (var layer in map.GetComponentsInChildren<MapLabBakedLayer>(true).Where(layer => !IsLegacy(layer)).ToList())
            {
                MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer.LayerName);
                if (!includeLocked && settings != null && settings.Locked) continue;
                restored += Restore(layer);
                layers++;
            }
            MapLabDrawLayers.Sync(map);
            SyncScene(map);
            if (MapLabLayerStack.Saved(map)) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            return (layers, restored);
        }

        public static void SyncScene(MapLabMap map)
        {
            Transform root = Root(map, false);
            if (root != null)
            {
                foreach (var paint in root.GetComponentsInChildren<MapLabChunkLayer>(true).Where(layer => layer.IsPaint).ToList()) Object.DestroyImmediate(paint.gameObject);
            }

            List<MapLabLayerStack.Entry> stack = MapLabLayerStack.Resolve(map);
            float chunkSize = MapLabChunkRenderer.SnappedChunkSize(map);
            foreach (var entry in stack.Where(entry => entry.IsPaint && !IsSorted(map, entry.Layer)))
            {
                Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(MapLabLayerFiles.PaintFolder(map, entry.Layer, entry.Name));
                if (chunks.Count == 0) continue;
                MapLabLayerFiles.EnsureSprites(chunks.Values, chunkSize);
                if (root == null) root = Root(map, true);
                BuildChunkLayer(root, PaintPrefix + entry.Name, entry.Layer, entry.Name, chunks, chunkSize, map.BakePixelsPerUnit);
            }
            if (root == null) return;

            MapLabSortedBaker.SyncPaint(map, stack);
            ApplyOrder(map, root, stack.Where(entry => !IsSorted(map, entry.Layer)).ToList());
            if (root.childCount == 0) Object.DestroyImmediate(root.gameObject);
            else root.GetComponent<MapLabChunkStreamer>().Refresh();
            MapLabLayerStack.MarkDirty(map);
        }

        public static bool IsSorted(MapLabMap map, string layer)
        {
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer);
            return settings != null && settings.Sorted;
        }

        public static void RefreshChunks(MapLabMap map, string layer)
        {
            MapLabBakedLayer baked = BakedLayer(map, layer);
            if (baked == null || baked.ChunkLayer == null) return;
            MapLabChunkLayer chunks = baked.ChunkLayer;
            for (int i = chunks.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(chunks.transform.GetChild(i).gameObject);
            Populate(chunks, layer, null, MapLabLayerFiles.Chunks(MapLabLayerFiles.BaseFolder(map, layer)), chunks.ChunkSize, chunks.PixelsPerUnit);
            SyncScene(map);
        }

        public static HashSet<Vector2Int> PaintedChunks(MapLabMap map, string layer)
        {
            var painted = new HashSet<Vector2Int>();
            if (!MapLabLayerStack.Saved(map)) return painted;
            foreach (var paint in MapLabLayerFiles.PaintNames(map, layer))
            {
                painted.UnionWith(MapLabLayerFiles.Chunks(MapLabLayerFiles.PaintFolder(map, layer, paint)).Keys);
            }
            return painted;
        }

        public static int UpdateChangedUnderPaint(MapLabMap map, MapLabBakeLayerSettings settings, Dictionary<Vector2Int, Hash128> before, Dictionary<Vector2Int, Hash128> after)
        {
            return UpdateChangedUnderPaint(map, settings, before, after, before.Count > 0);
        }

        public static int UpdateChangedUnderPaint(MapLabMap map, MapLabBakeLayerSettings settings, Dictionary<Vector2Int, Hash128> before, Dictionary<Vector2Int, Hash128> after, bool hadBase)
        {
            if (!hadBase) return 0;
            var changed = new HashSet<Vector2Int>(settings.ChangedUnderPaint);
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out Hash128 hash) || hash != pair.Value) changed.Add(pair.Key);
            }
            foreach (var index in after.Keys)
            {
                if (!before.ContainsKey(index)) changed.Add(index);
            }
            HashSet<Vector2Int> painted = PaintedChunks(map, settings.Name);
            List<Vector2Int> flagged = changed.Where(painted.Contains).OrderBy(index => index.y).ThenBy(index => index.x).ToList();
            int added = flagged.Count(index => !settings.ChangedUnderPaint.Contains(index));
            settings.SetChangedUnderPaint(flagged);
            MapLabLayerStack.MarkDirty(map);
            return added;
        }
        #endregion

        #region [Private Methods]
        private static void MarkFloorBaked(MapLabMap map, MapLabBakedLayer layer, ICollection<Vector2Int> megas)
        {
            MapLabWorldData world = map.World;
            if (world == null || !layer.BakedFloors.Any(floor => floor != null && floor.World == world)) return;
            var chosen = megas != null ? new HashSet<Vector2Int>(megas) : null;
            HashSet<Vector2Int> baked = MapLabLayerFiles.Megas(MapLabLayerFiles.BaseFolder(map, layer.LayerName), MapLabLayerFiles.PerMega(map));
            foreach (var mega in world.Megas)
            {
                if (chosen != null && !chosen.Contains(mega.Index)) continue;
                if (baked.Contains(mega.Index)) mega.MarkBaked();
            }
            EditorUtility.SetDirty(world);
        }

        private static void ApplyOrder(MapLabMap map, Transform root, List<MapLabLayerStack.Entry> stack)
        {
            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out _);
            stack = MapLabLayerStack.DrawOrder(map, stack);
            var bases = new List<Vector2Int?>(stack.Count);
            var layers = new List<MapLabChunkLayer>(stack.Count);
            foreach (var entry in stack)
            {
                if (entry.IsPaint)
                {
                    bases.Add(null);
                    layers.Add(PaintLayer(map, entry.Name));
                    continue;
                }
                MapLabBakedLayer baked = BakedLayer(map, entry.Name);
                layers.Add(baked != null ? baked.ChunkLayer : null);
                if (baked != null) bases.Add(new Vector2Int(SortingLayer.GetLayerValueFromID(baked.SourceSortingLayer), baked.SourceSortingOrder));
                else if (claims.TryGetValue(entry.Name, out List<Renderer> live) && live.Count > 0) bases.Add(live.Select(MapLabLayerClaims.SortKey).Aggregate((a, b) => MapLabLayerStack.Compare(a, b) >= 0 ? a : b));
                else bases.Add(null);
            }

            var actor = new Vector2Int(SortingLayer.GetLayerValueFromName(map.ActorSortingLayer), map.ActorSortingOrder);
            List<Vector2Int> keys = MapLabLayerStack.AssignKeys(bases, actor);
            int sibling = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                if (layers[i] == null || layers[i].transform.parent != root) continue;
                layers[i].SetSorting(MapLabLayerStack.SortingLayerId(keys[i].x), keys[i].y);
                layers[i].transform.SetSiblingIndex(sibling++);
            }
        }

        private static MapLabBakedLayer BuildBakedLayer(MapLabMap map, string layerName, Dictionary<Vector2Int, string> chunks, float chunkSize, int pixelsPerUnit)
        {
            MapLabChunkLayer layer = BuildChunkLayer(Root(map, true), LayerPrefix + layerName, layerName, null, chunks, chunkSize, pixelsPerUnit);
            return layer.gameObject.AddComponent<MapLabBakedLayer>();
        }

        private static MapLabChunkLayer BuildChunkLayer(Transform root, string objectName, string layerName, string paintName, Dictionary<Vector2Int, string> chunks, float chunkSize, int pixelsPerUnit)
        {
            var go = new GameObject(objectName) { layer = root.gameObject.layer };
            go.transform.SetParent(root, false);
            var layer = go.AddComponent<MapLabChunkLayer>();
            Populate(layer, layerName, paintName, chunks, chunkSize, pixelsPerUnit);
            return layer;
        }

        private static void Populate(MapLabChunkLayer layer, string layerName, string paintName, Dictionary<Vector2Int, string> chunks, float chunkSize, int pixelsPerUnit)
        {
            Material material = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.default2DMaterial : null;
            var indices = new List<Vector2Int>(chunks.Count);
            var renderers = new List<SpriteRenderer>(chunks.Count);
            foreach (var pair in chunks.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pair.Value);
                if (sprite == null) continue;
                var chunk = new GameObject($"{ChunkPrefix} {pair.Key.x},{pair.Key.y}") { layer = layer.gameObject.layer };
                chunk.transform.SetParent(layer.transform, false);
                Rect rect = MapLabChunkLayer.ChunkRect(pair.Key, chunkSize);
                chunk.transform.position = new Vector3(rect.x, rect.y, 0f);
                var renderer = chunk.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                if (material != null) renderer.sharedMaterial = material;
                indices.Add(pair.Key);
                renderers.Add(renderer);
            }
            layer.Setup(layerName, paintName, chunkSize, pixelsPerUnit, indices, renderers);
        }

        private static int RemoveLegacy(MapLabMap map)
        {
            int restored = 0;
            foreach (var layer in map.GetComponentsInChildren<MapLabBakedLayer>(true).Where(IsLegacy).ToList()) restored += Restore(layer);
            return restored;
        }

        private static bool IsLegacy(MapLabBakedLayer layer)
        {
            return string.IsNullOrEmpty(layer.LayerName) || layer.transform.parent == null || layer.transform.parent.name != RootName;
        }

        private static int Restore(MapLabBakedLayer layer)
        {
            foreach (var renderer in layer.HiddenRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }
            foreach (var shape in layer.BakedVisuals)
            {
                if (shape != null) shape.SetVisualBaked(false);
            }
            foreach (var shape in layer.BakedShadows)
            {
                if (shape != null) shape.SetShadowBaked(false);
            }
            foreach (var floor in layer.BakedFloors)
            {
                if (floor != null) floor.SetBaked(false);
            }
            int restored = layer.HiddenRenderers.Count + layer.BakedVisuals.Count + layer.BakedShadows.Count + layer.BakedFloors.Count;
            Object.DestroyImmediate(layer.gameObject);
            return restored;
        }
        #endregion
    }
}
