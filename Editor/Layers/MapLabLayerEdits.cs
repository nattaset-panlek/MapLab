using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MapLab.Editor
{
    public static class MapLabLayerEdits
    {
        #region [Public Methods]
        public static MapLabBakeLayerSettings Add(MapLabMap map, bool sorted = false)
        {
            MapLabLayerStack.EnsureLayers(map);
            string stem = sorted ? "Sorted" : "Layer";
            string name = stem;
            for (int i = 2; MapLabLayerStack.Find(map, name) != null; i++) name = $"{stem} {i}";
            Undo.RecordObject(map, "Add MapLab Bake Layer");
            var layer = new MapLabBakeLayerSettings(name, sorted ? MapLabBakeKinds.SortedShapes : MapLabBakeKinds.None) { Sorted = sorted };
            map.BakeLayers.Add(layer);
            MapLabLayerStack.MarkDirty(map);
            MapLabDrawLayers.Sync(map);
            return layer;
        }

        public static MapLabBakeLayerSettings AddDecorate(MapLabMap map)
        {
            MapLabLayerStack.EnsureLayers(map);
            string name = MapLabDrawLayers.DecorateLayerName;
            for (int i = 2; MapLabLayerStack.Find(map, name) != null; i++) name = $"{MapLabDrawLayers.DecorateLayerName} {i}";
            Undo.RecordObject(map, "Add MapLab Decorate Layer");
            var layer = new MapLabBakeLayerSettings(name, MapLabBakeKinds.None);
            map.BakeLayers.Add(layer);
            MapLabLayerStack.SetSide(map, name, true);
            Crossed(map, layer);
            return layer;
        }

        public static string Rename(MapLabMap map, string oldName, string newName)
        {
            newName = newName?.Trim();
            if (!MapLabLayerNames.IsValidLayerName(newName, out string reason)) return reason;
            if (oldName == newName) return null;
            if (!MapLabLayerStack.Same(oldName, newName) && MapLabLayerStack.Find(map, newName) != null) return $"There is already a layer called '{newName}'.";
            MapLabLayerStack.EnsureLayers(map);
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, oldName);
            if (settings == null) return $"There is no layer called '{oldName}'.";

            List<string> layers = MapLabLayerStack.Layers(map).Select(layer => layer.Name).ToList();
            var stack = MapLabLayerStack.NamesWithLine(map).Select(name => MapLabLayerStack.Same(name, oldName) ? newName
                : MapLabLayerStack.Find(map, name) == null && MapLabLayerNames.TryParsePaint(name, layers, out string owner, out _) && MapLabLayerStack.Same(owner, oldName)
                    ? MapLabLayerNames.RenamePaint(name, oldName, newName) : name).ToList();
            if (MapLabLayerStack.Saved(map) && Directory.Exists(MapLabLayerFiles.LayerFolder(map, oldName)))
            {
                string error = RenameFolders(map, oldName, newName);
                if (error != null) return error;
            }

            Undo.RecordObject(map, "Rename MapLab Bake Layer");
            settings.Name = newName;
            MapLabLayerStack.Save(map, stack);
            MapLabBakedLayer baked = MapLabLayerBaker.BakedLayer(map, oldName);
            if (baked != null)
            {
                if (baked.ChunkLayer != null) baked.ChunkLayer.Rename(newName, null);
                if (baked.SortedLayer != null) baked.SortedLayer.Rename(newName);
                baked.gameObject.name = MapLabLayerBaker.LayerPrefix + newName;
            }
            if (settings.Sorted && MapLabLayerStack.Saved(map)) MapLabSortedBaker.RecutPaint(map, newName);
            MapLabLayerBaker.SyncScene(map);
            return null;
        }

        public static string Remove(MapLabMap map, string name)
        {
            MapLabLayerStack.EnsureLayers(map);
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, name);
            if (settings == null) return $"There is no layer called '{name}'.";
            if (settings.Locked) return "Unlock the layer before removing it.";
            if (map.BakeLayers.Count <= 1) return "A map keeps at least one bake layer.";
            if (MapLabLayerBaker.BakedLayer(map, name) != null) MapLabLayerBaker.Unbake(map, name);
            Undo.RecordObject(map, "Remove MapLab Bake Layer");
            map.BakeLayers.Remove(settings);
            MapLabLayerStack.MarkDirty(map);
            MapLabDrawLayers.Sync(map);
            MapLabLayerBaker.SyncScene(map);
            return null;
        }

        public static void SetLocked(MapLabMap map, MapLabBakeLayerSettings layer, bool locked)
        {
            Change(map, "Lock MapLab Bake Layer", () => layer.Locked = locked);
        }

        public static void SetKinds(MapLabMap map, MapLabBakeLayerSettings layer, MapLabBakeKinds kinds)
        {
            Change(map, "Change MapLab Bake Layer", () => layer.Kinds = kinds);
        }

        public static void SetCastShadows(MapLabMap map, MapLabBakeLayerSettings layer, bool cast)
        {
            Change(map, "Change MapLab Decorate Shadows", () => layer.CastShadows = cast);
            MapLabLayerBaker.SyncScene(map);
        }

        public static void SetSide(MapLabMap map, MapLabBakeLayerSettings layer, bool decorate)
        {
            if (layer.Sorted && decorate) return;
            MapLabLayerStack.SetSide(map, layer.Name, decorate);
            Crossed(map, layer);
        }

        public static void SetSet(MapLabMap map, MapLabBakeLayerSettings layer, MapLabSet set, bool included)
        {
            Change(map, "Change MapLab Bake Layer", () =>
            {
                layer.Sets.RemoveAll(item => item == null || item == set);
                if (included) layer.Sets.Add(set);
            });
        }

        public static void MarkReviewed(MapLabMap map, MapLabBakeLayerSettings layer)
        {
            Change(map, "Review MapLab Paint", () => layer.SetChangedUnderPaint(Array.Empty<UnityEngine.Vector2Int>()));
        }

        public static void Move(MapLabMap map, string name, int delta)
        {
            List<string> names = MapLabLayerStack.NamesWithLine(map);
            int target = names.FindIndex(entry => MapLabLayerStack.Same(entry, name)) + delta;
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, name);
            if (settings != null && target >= 0 && target < names.Count && names[target] == MapLabLayerStack.DecorateMarker)
            {
                SetSide(map, settings, !MapLabLayerStack.IsDecorate(map, name));
                return;
            }
            if (!MapLabLayerStack.Move(map, name, delta)) return;
            MapLabDrawLayers.Sync(map);
            MapLabLayerBaker.SyncScene(map);
        }

        public static void DeletePaint(MapLabMap map, string layer, string paint)
        {
            AssetDatabase.MoveAssetToTrash(MapLabLayerFiles.PaintFolder(map, layer, paint));
            if (MapLabLayerBaker.IsSorted(map, layer)) MapLabSortedBaker.RecutPaint(map, layer);
            MapLabLayerBaker.SyncScene(map);
        }
        #endregion

        #region [Private Methods]
        private static void Crossed(MapLabMap map, MapLabBakeLayerSettings layer)
        {
            if (layer != null && MapLabLayerStack.IsDecorate(map, layer.Name)) MapLabDrawLayers.EnsureShadowLayer(map);
            else MapLabLayerTypes.Release(map, layer);
            MapLabLayerStack.MarkDirty(map);
            MapLabDrawLayers.Sync(map);
            MapLabLayerBaker.SyncScene(map);
        }

        private static void Change(MapLabMap map, string undo, Action change)
        {
            MapLabLayerStack.EnsureLayers(map);
            Undo.RecordObject(map, undo);
            change();
            MapLabLayerStack.MarkDirty(map);
            MapLabDrawLayers.Sync(map);
        }

        private static string RenameFolders(MapLabMap map, string oldName, string newName)
        {
            MapLabLayerFiles.ClearFolder(MapLabLayerFiles.CutRoot(map, oldName));
            foreach (var paint in MapLabLayerFiles.PaintNames(map, oldName))
            {
                string error = AssetDatabase.RenameAsset(MapLabLayerFiles.PaintFolder(map, oldName, paint), MapLabLayerNames.RenamePaint(paint, oldName, newName));
                if (!string.IsNullOrEmpty(error)) return error;
            }
            string folder = MapLabLayerFiles.LayerFolder(map, oldName);
            if (MapLabLayerStack.Same(oldName, newName))
            {
                string temporary = $"{newName}_renaming";
                string first = AssetDatabase.RenameAsset(folder, temporary);
                if (!string.IsNullOrEmpty(first)) return first;
                folder = $"{MapLabLayerFiles.LayersFolder(map)}/{temporary}";
            }
            string result = AssetDatabase.RenameAsset(folder, newName);
            return string.IsNullOrEmpty(result) ? null : result;
        }
        #endregion
    }
}
