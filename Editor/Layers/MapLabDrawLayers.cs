using System;
using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabDrawLayers
    {
        #region [Variables/Fields]
        public const int OrderStep = 4;
        public const int DefaultShadowOrder = 1;
        public const MapLabBakeKinds ShadowKinds = MapLabBakeKinds.ShapeShadows | MapLabBakeKinds.PropShadows;
        public const string ShadowLayerName = "Shadows";
        public const string DecorateLayerName = "Decorate";
        #endregion

        #region [Public Methods]
        public static List<MapLabBakeLayerSettings> DecorateLayers(MapLabMap map)
        {
            return MapLabLayerStack.DecorateLayers(map);
        }

        public static int OrderOf(MapLabMap map, int rank)
        {
            return ShadowFloor(map) - OrderStep * (rank + 1);
        }

        public static int OrderOf(MapLabMap map, string layer)
        {
            int rank = DecorateLayers(map).FindIndex(settings => MapLabLayerStack.Same(settings.Name, layer));
            return rank >= 0 ? OrderOf(map, rank) : map.ActorSortingOrder;
        }

        public static int ShadowFloor(MapLabMap map)
        {
            int floor = DefaultShadowOrder;
            foreach (var shape in Shapes(map)) floor = Mathf.Min(floor, shape.BaseShadowOrder);
            return Mathf.Min(floor, map.ActorSortingOrder - 1);
        }

        public static string EnsureShadowLayer(MapLabMap map)
        {
            List<MapLabLayerStack.Entry> stack = MapLabLayerStack.ResolveWithLine(map, false, out int line);
            List<MapLabBakeLayerSettings> mixed = stack.Skip(line).Select(entry => MapLabLayerStack.Find(map, entry.Name))
                .Where(settings => settings != null && !settings.Sorted && (settings.Kinds & ShadowKinds) != 0 && (settings.Kinds & ~ShadowKinds) != 0).ToList();
            if (mixed.Count == 0) return null;

            Undo.RecordObject(map, "Split MapLab Shadows");
            MapLabBakeKinds shadows = MapLabBakeKinds.None;
            foreach (var settings in mixed)
            {
                shadows |= settings.Kinds & ShadowKinds;
                settings.Kinds &= ~ShadowKinds;
            }
            string name = ShadowLayerName;
            for (int i = 2; MapLabLayerStack.Find(map, name) != null; i++) name = $"{ShadowLayerName} {i}";
            map.BakeLayers.Add(new MapLabBakeLayerSettings(name, shadows));
            List<string> names = MapLabLayerStack.NamesWithLine(map);
            names.RemoveAll(entry => MapLabLayerStack.Same(entry, name));
            string top = mixed[mixed.Count - 1].Name;
            int above = names.FindIndex(entry => MapLabLayerStack.Same(entry, top)) + 1;
            while (above < names.Count && MapLabLayerStack.IsPaintOf(map, names[above], top)) above++;
            names.Insert(above, name);
            MapLabLayerStack.Save(map, names);
            MapLabLayerStack.MarkDirty(map);
            return $"[MapLab] Shadows now bake into their own layer '{name}' so they fall on the Decorate layers (taken out of {string.Join(", ", mixed.Select(settings => settings.Name))})";
        }

        public static string Sync(MapLabMap map)
        {
            if (map == null) return null;
            bool upgraded = MapLabLayerStack.Upgrade(map);
            List<MapLabBakeLayerSettings> decorate = DecorateLayers(map);
            int shapes = 0;
            foreach (var shape in Shapes(map))
            {
                int rank = Owner(decorate, shape.transform);
                bool changed = rank >= 0 ? shape.SetDrawAsGround(true, OrderOf(map, rank), decorate[rank].CastShadows) : shape.SetDrawAsGround(false, 0);
                if (!changed) continue;
                EditorUtility.SetDirty(shape);
                shapes++;
            }

            var wanted = new Dictionary<GameObject, int>();
            foreach (var prop in PropRoots(map))
            {
                int rank = Owner(decorate, prop.transform);
                if (rank >= 0) wanted[prop] = rank;
            }
            int props = 0;
            foreach (var existing in map.GetComponentsInChildren<MapLabGroundProp>(true).ToList())
            {
                if (wanted.ContainsKey(existing.gameObject)) continue;
                existing.Restore();
                Object.DestroyImmediate(existing);
                props++;
            }
            int actorLayer = SortingLayer.GetLayerValueFromName(map.ActorSortingLayer);
            Func<Renderer, bool> isShadow = renderer => MapLabLayerClaims.IsBelowActors(renderer, actorLayer, map.ActorSortingOrder);
            foreach (var pair in wanted)
            {
                MapLabBakeLayerSettings layer = decorate[pair.Value];
                MapLabGroundProp ground = pair.Key.GetComponent<MapLabGroundProp>();
                if (ground == null) ground = pair.Key.AddComponent<MapLabGroundProp>();
                if (!ground.Apply(layer.Name, OrderOf(map, pair.Value), layer.CastShadows, isShadow)) continue;
                EditorUtility.SetDirty(ground);
                props++;
            }
            if (shapes + props == 0 && !upgraded) return null;
            if (MapLabLayerStack.Saved(map)) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            return $"[MapLab] '{map.name}': {shapes} shapes and {props} props changed how they draw";
        }

        public static int OwnerOf(List<MapLabBakeLayerSettings> decorate, Transform target)
        {
            return Owner(decorate, target);
        }

        public static List<ExtrudedShape> Shapes(MapLabMap map)
        {
            return map.GetComponentsInChildren<ExtrudedShape>(true).Where(shape => !shape.IsStacked && shape.GetComponentInParent<MapLabChunkStreamer>() == null).ToList();
        }

        public static List<GameObject> PropRoots(MapLabMap map)
        {
            var roots = new List<GameObject>();
            int actorLayer = SortingLayer.GetLayerValueFromName(map.ActorSortingLayer);
            foreach (Transform child in map.transform) Visit(child, roots, actorLayer, map.ActorSortingOrder);
            return roots;
        }

        public static List<Component> Members(MapLabMap map, string layer)
        {
            var members = new List<Component>();
            int order = OrderOf(map, layer);
            members.AddRange(Shapes(map).Where(shape => shape.DrawsAsGround && shape.GroundOrder == order));
            members.AddRange(map.GetComponentsInChildren<MapLabGroundProp>(true).Where(prop => MapLabLayerStack.Same(prop.Layer, layer)));
            return members;
        }
        #endregion

        #region [Private Methods]
        private static int Owner(List<MapLabBakeLayerSettings> decorate, Transform target)
        {
            for (int i = 0; i < decorate.Count; i++)
            {
                if (decorate[i].Sets.Any(set => set != null && target.IsChildOf(set.transform))) return i;
            }
            return -1;
        }

        private static void Visit(Transform node, List<GameObject> roots, int actorLayer, int actorOrder)
        {
            if (node.GetComponent<MapLabSet>() != null)
            {
                foreach (Transform child in node) Visit(child, roots, actorLayer, actorOrder);
                return;
            }
            if (!IsProp(node, actorLayer, actorOrder)) return;
            roots.Add(node.gameObject);
        }

        private static bool IsProp(Transform node, int actorLayer, int actorOrder)
        {
            if (node.GetComponentInChildren<PointShape>(true) != null || node.GetComponentInChildren<RampShape>(true) != null) return false;
            if (node.GetComponentInChildren<MapLabFloor>(true) != null || node.GetComponentInChildren<MapLabChunkStreamer>(true) != null) return false;
            if (node.GetComponentInChildren<Animator>(true) != null || node.GetComponentInChildren<Camera>(true) != null) return false;
            if (node.GetComponent<MapLabGroundProp>() != null) return true;
            return node.GetComponentsInChildren<Renderer>(true).Any(renderer => !MapLabLayerClaims.IsBelowActors(renderer, actorLayer, actorOrder));
        }
        #endregion
    }
}
