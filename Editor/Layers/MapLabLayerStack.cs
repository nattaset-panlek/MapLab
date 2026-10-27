using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabLayerStack
    {
        #region [Variables/Fields]
        public const string DefaultLayer = "Ground";
        public const string DecorateMarker = "[Decorate]";
        public const string LegacyActorsMarker = "[Actors]";
        public const MapLabBakeKinds LegacyPullKinds = MapLabBakeKinds.PulledShapes | MapLabBakeKinds.PulledProps;

        public readonly struct Entry
        {
            public readonly string Name;
            public readonly string Layer;
            public readonly bool IsPaint;

            public Entry(string name, string layer, bool isPaint)
            {
                Name = name;
                Layer = layer;
                IsPaint = isPaint;
            }
        }
        #endregion

        #region [Public Methods]
        public static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Saved(MapLabMap map)
        {
            return !string.IsNullOrEmpty(map.gameObject.scene.path);
        }

        public static List<MapLabBakeLayerSettings> Layers(MapLabMap map)
        {
            if (map.BakeLayers.Count > 0) return map.BakeLayers;
            return new List<MapLabBakeLayerSettings> { new MapLabBakeLayerSettings(DefaultLayer, MapLabBakeKinds.All) };
        }

        public static void EnsureLayers(MapLabMap map)
        {
            if (map.BakeLayers.Count > 0) return;
            Undo.RecordObject(map, "Add MapLab Bake Layer");
            map.BakeLayers.Add(new MapLabBakeLayerSettings(DefaultLayer, MapLabBakeKinds.All));
            MarkDirty(map);
        }

        public static MapLabBakeLayerSettings Find(MapLabMap map, string layer)
        {
            return Layers(map).FirstOrDefault(settings => Same(settings.Name, layer));
        }

        public static List<MapLabBakeLayerSettings> LayersBottomToTop(MapLabMap map)
        {
            List<MapLabBakeLayerSettings> layers = Layers(map);
            List<Entry> stack = Resolve(map, false);
            return layers.OrderBy(layer => stack.FindIndex(entry => !entry.IsPaint && Same(entry.Name, layer.Name))).ToList();
        }

        public static List<Entry> Resolve(MapLabMap map, bool includePaint = true)
        {
            List<string> layers = Layers(map).Select(layer => layer.Name).ToList();
            var paints = new List<Entry>();
            if (includePaint && Saved(map))
            {
                foreach (var layer in layers)
                {
                    foreach (var paint in MapLabLayerFiles.PaintNames(map, layer)) paints.Add(new Entry(paint, layer, true));
                }
            }
            return Resolve(layers, paints, map.LayerStack);
        }

        public static List<Entry> Resolve(IReadOnlyList<string> layers, IReadOnlyList<Entry> paints, IReadOnlyList<string> saved)
        {
            return Resolve(layers, paints, saved, out _);
        }

        public static List<Entry> Resolve(IReadOnlyList<string> layers, IReadOnlyList<Entry> paints, IReadOnlyList<string> saved, out int line)
        {
            List<Entry> valid = layers.Select(name => new Entry(name, name, false)).Concat(paints).ToList();
            var result = new List<Entry>();
            line = -1;
            foreach (var name in saved)
            {
                if (name == DecorateMarker)
                {
                    if (line < 0) line = result.Count;
                    continue;
                }
                int index = valid.FindIndex(entry => Same(entry.Name, name));
                if (index >= 0 && !Contains(result, name)) result.Add(valid[index]);
            }
            if (line < 0) line = 0;
            foreach (var entry in valid)
            {
                if (Contains(result, entry.Name)) continue;
                int owner = entry.IsPaint ? result.FindIndex(item => !item.IsPaint && Same(item.Name, entry.Layer)) : -1;
                int at = entry.IsPaint ? AboveLayer(result, entry.Layer) : result.Count;
                result.Insert(at, entry);
                if (at < line || (owner >= 0 && owner < line && at == line)) line++;
            }
            return result;
        }

        public static int DecorateLine(MapLabMap map, bool includePaint = true)
        {
            ResolveWithLine(map, includePaint, out int line);
            return line;
        }

        public static List<Entry> ResolveWithLine(MapLabMap map, bool includePaint, out int line)
        {
            List<string> layers = Layers(map).Select(layer => layer.Name).ToList();
            var paints = new List<Entry>();
            if (includePaint && Saved(map))
            {
                foreach (var layer in layers)
                {
                    foreach (var paint in MapLabLayerFiles.PaintNames(map, layer)) paints.Add(new Entry(paint, layer, true));
                }
            }
            return Resolve(layers, paints, map.LayerStack, out line);
        }

        public static List<MapLabBakeLayerSettings> DecorateLayers(MapLabMap map)
        {
            List<Entry> stack = ResolveWithLine(map, false, out int line);
            var decorate = new List<MapLabBakeLayerSettings>();
            for (int i = line - 1; i >= 0; i--)
            {
                MapLabBakeLayerSettings settings = Find(map, stack[i].Name);
                if (settings != null && !settings.Sorted) decorate.Add(settings);
            }
            return decorate;
        }

        public static bool IsDecorate(MapLabMap map, string layer)
        {
            return DecorateLayers(map).Any(settings => Same(settings.Name, layer));
        }

        public static List<Entry> DrawOrder(MapLabMap map, IReadOnlyList<Entry> stack)
        {
            var decorate = new HashSet<string>(DecorateLayers(map).Select(settings => settings.Name), StringComparer.OrdinalIgnoreCase);
            List<Entry> normal = stack.Where(entry => !decorate.Contains(entry.Layer)).ToList();
            List<Entry> decor = stack.Where(entry => decorate.Contains(entry.Layer)).ToList();
            int at = normal.FindIndex(entry => !entry.IsPaint && HoldsShadows(Find(map, entry.Layer)));
            if (at < 0) at = normal.Count;
            else if (MixesShadows(Find(map, normal[at].Layer)))
            {
                at++;
                while (at < normal.Count && normal[at].IsPaint && Same(normal[at].Layer, normal[at - 1].Layer)) at++;
            }
            return normal.Take(at).Concat(decor).Concat(normal.Skip(at)).ToList();
        }

        public static bool MixesShadows(MapLabBakeLayerSettings settings)
        {
            return HoldsShadows(settings) && (settings.Kinds & ~(MapLabBakeKinds.ShapeShadows | MapLabBakeKinds.PropShadows)) != 0;
        }

        public static bool HoldsShadows(MapLabBakeLayerSettings settings)
        {
            return settings != null && !settings.Sorted && (settings.Kinds & (MapLabBakeKinds.ShapeShadows | MapLabBakeKinds.PropShadows)) != 0;
        }

        public static List<string> NamesWithLine(MapLabMap map)
        {
            List<Entry> stack = ResolveWithLine(map, true, out int line);
            List<string> names = stack.Select(entry => entry.Name).ToList();
            names.Insert(line, DecorateMarker);
            return names;
        }

        public static void SetSide(MapLabMap map, string layer, bool decorate)
        {
            List<string> names = NamesWithLine(map);
            List<string> moving = names.Where(name => Same(name, layer) || IsPaintOf(map, name, layer)).ToList();
            if (moving.Count == 0) return;
            names.RemoveAll(name => moving.Contains(name));
            int marker = names.IndexOf(DecorateMarker);
            names.InsertRange(decorate ? marker : marker + 1, moving);
            Save(map, names);
        }

        public static bool Upgrade(MapLabMap map)
        {
            if (map == null) return false;
            bool legacy = map.LayerStack.Contains(LegacyActorsMarker);
            List<string> pulling = map.BakeLayers.Where(settings => !settings.Sorted && (settings.Kinds & LegacyPullKinds) != 0).Select(settings => settings.Name).ToList();
            if (!legacy && pulling.Count == 0) return false;
            Undo.RecordObject(map, "Upgrade MapLab Layers");
            List<string> names = NamesWithLine(map);
            List<string> moving = names.Where(name => pulling.Any(layer => Same(name, layer) || IsPaintOf(map, name, layer))).ToList();
            names.RemoveAll(name => moving.Contains(name));
            names.InsertRange(names.IndexOf(DecorateMarker), moving);
            foreach (var settings in map.BakeLayers)
            {
                if (pulling.Any(layer => Same(layer, settings.Name))) settings.Kinds = MapLabBakeKinds.None;
                else settings.Kinds &= ~LegacyPullKinds;
            }
            map.LayerStack.Clear();
            map.LayerStack.AddRange(names);
            MarkDirty(map);
            return true;
        }

        public static List<string> Merge(IReadOnlyList<string> current, IReadOnlyList<string> imported)
        {
            var result = new List<string>();
            foreach (var name in imported)
            {
                string known = current.FirstOrDefault(entry => Same(entry, name));
                if (known != null && !result.Any(entry => Same(entry, known))) result.Add(known);
            }
            for (int i = 0; i < current.Count; i++)
            {
                if (result.Any(entry => Same(entry, current[i]))) continue;
                int anchor = -1;
                for (int k = i - 1; k >= 0 && anchor < 0; k--) anchor = result.FindIndex(entry => Same(entry, current[k]));
                result.Insert(anchor + 1, current[i]);
            }
            return result;
        }

        public static void Save(MapLabMap map, IEnumerable<string> names)
        {
            Undo.RecordObject(map, "Reorder MapLab Layers");
            List<string> list = names.Where(name => name != LegacyActorsMarker).ToList();
            int oldMarker = map.LayerStack.IndexOf(DecorateMarker);
            if (!list.Contains(DecorateMarker) && oldMarker >= 0)
            {
                var below = new HashSet<string>(map.LayerStack.Take(oldMarker), StringComparer.OrdinalIgnoreCase);
                int at = 0;
                while (at < list.Count && below.Contains(list[at])) at++;
                list.Insert(at, DecorateMarker);
            }
            map.LayerStack.Clear();
            map.LayerStack.AddRange(list);
            MarkDirty(map);
        }

        public static bool Move(MapLabMap map, string name, int delta)
        {
            List<string> names = NamesWithLine(map);
            int index = names.FindIndex(entry => Same(entry, name));
            int target = Mathf.Clamp(index + delta, 0, names.Count - 1);
            if (index < 0 || target == index) return false;
            MapLabBakeLayerSettings settings = Find(map, name);
            if (settings != null && settings.Sorted && names[target] == DecorateMarker) return false;
            string moved = names[index];
            names.RemoveAt(index);
            names.Insert(target, moved);
            Save(map, names);
            return true;
        }

        public static List<Vector2Int> AssignKeys(IReadOnlyList<Vector2Int?> bases, Vector2Int actor)
        {
            int count = bases.Count;
            int first = -1;
            for (int i = 0; i < count && first < 0; i++)
            {
                if (bases[i].HasValue) first = i;
            }
            Vector2Int previous = first >= 0
                ? new Vector2Int(bases[first].Value.x, bases[first].Value.y - first - 1)
                : new Vector2Int(actor.x, actor.y - count - 1);
            var keys = new List<Vector2Int>(count);
            for (int i = 0; i < count; i++)
            {
                var candidate = new Vector2Int(previous.x, previous.y + 1);
                if (bases[i].HasValue && Compare(bases[i].Value, candidate) > 0) candidate = bases[i].Value;
                keys.Add(candidate);
                previous = candidate;
            }
            var limit = new Vector2Int(actor.x, actor.y - 1);
            for (int i = count - 1; i >= 0; i--)
            {
                if (Compare(keys[i], limit) > 0) keys[i] = limit;
                limit = new Vector2Int(keys[i].x, keys[i].y - 1);
            }
            return keys;
        }

        public static int Compare(Vector2Int a, Vector2Int b)
        {
            return a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
        }

        public static int SortingLayerId(int value)
        {
            foreach (var layer in SortingLayer.layers)
            {
                if (layer.value == value) return layer.id;
            }
            return 0;
        }

        public static bool IsPaintOf(MapLabMap map, string name, string layer)
        {
            return Find(map, name) == null && MapLabLayerNames.TryParsePaint(name, Layers(map).Select(settings => settings.Name).ToList(), out string owner, out _) && Same(owner, layer);
        }

        public static void MarkDirty(MapLabMap map)
        {
            EditorUtility.SetDirty(map);
            if (Saved(map)) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
        #endregion

        #region [Private Methods]
        private static bool Contains(List<Entry> entries, string name)
        {
            return entries.Any(entry => Same(entry.Name, name));
        }

        private static int AboveLayer(List<Entry> entries, string layer)
        {
            int index = entries.FindIndex(entry => !entry.IsPaint && Same(entry.Name, layer));
            if (index < 0) return entries.Count;
            index++;
            while (index < entries.Count && entries[index].IsPaint && Same(entries[index].Layer, layer)) index++;
            return index;
        }
        #endregion
    }
}
