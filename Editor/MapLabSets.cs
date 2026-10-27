using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabSets
    {
        #region [Variables/Fields]
        public static readonly Color[] Palette =
        {
            new Color32(0x4F, 0xB3, 0xA9, 0xFF), new Color32(0x5B, 0x8D, 0xEF, 0xFF), new Color32(0x7B, 0x7F, 0xE6, 0xFF),
            new Color32(0x9C, 0x7B, 0xE0, 0xFF), new Color32(0x4C, 0x9F, 0xC8, 0xFF), new Color32(0x6E, 0x7A, 0x8C, 0xFF)
        };
        #endregion

        #region [Public Methods]
        public static List<MapLabSet> SetsOf(MapLabMap map)
        {
            return map.GetComponentsInChildren<MapLabSet>(true).ToList();
        }

        public static List<GameObject> Members(MapLabSet set)
        {
            var members = new List<GameObject>();
            foreach (Transform child in set.transform)
            {
                if (!IsGenerated(child.gameObject)) members.Add(child.gameObject);
            }
            return members;
        }

        public static List<GameObject> Unassigned(MapLabMap map)
        {
            var loose = new List<GameObject>();
            foreach (var shape in map.GetComponentsInChildren<PointShape>(true))
            {
                if (shape is ExtrudedShape extruded && extruded.IsStacked) continue;
                if (shape.GetComponentInParent<MapLabSet>(true) != null) continue;
                if (shape.GetComponentInParent<MapLabChunkStreamer>(true) != null) continue;
                loose.Add(shape.gameObject);
            }
            return loose;
        }

        public static List<GameObject> AllObjects(MapLabMap map)
        {
            var all = new List<GameObject>();
            foreach (var set in SetsOf(map)) all.AddRange(Members(set));
            all.AddRange(Unassigned(map));
            return all;
        }

        public static MapLabSet SetOf(GameObject target)
        {
            return target != null && target.transform.parent != null ? target.transform.parent.GetComponent<MapLabSet>() : null;
        }

        public static MapLabSet CreateSet(MapLabMap map, string name)
        {
            var go = new GameObject(UniqueName(map, name));
            Undo.RegisterCreatedObjectUndo(go, "Create MapLab Set");
            go.layer = map.gameObject.layer;
            Undo.SetTransformParent(go.transform, map.transform, "Create MapLab Set");
            go.transform.localPosition = Vector3.zero;
            var set = go.AddComponent<MapLabSet>();
            set.SetColor(Palette[SetsOf(map).Count(other => other != set) % Palette.Length]);
            return set;
        }

        public static bool CanAdd(MapLabSet set, GameObject target)
        {
            if (target == null || set == null || target == set.gameObject) return false;
            MapLabMap map = set.GetComponentInParent<MapLabMap>(true);
            if (map == null || target == map.gameObject || target.GetComponentInParent<MapLabMap>(true) != map) return false;
            if (IsGenerated(target) || set.transform.IsChildOf(target.transform)) return false;
            if (target.GetComponent<ExtrudedShape>() is ExtrudedShape shape && shape.IsStacked) return false;
            if (target.GetComponentInParent<MapLabChunkStreamer>(true) != null) return false;
            return target.transform.parent != set.transform;
        }

        public static int Add(MapLabSet set, IEnumerable<GameObject> targets)
        {
            int added = 0;
            foreach (var target in targets.Distinct().ToList())
            {
                if (!CanAdd(set, target)) continue;
                Undo.SetTransformParent(target.transform, set.transform, "Add to MapLab Set");
                added++;
            }
            return added;
        }

        public static int Remove(MapLabMap map, IEnumerable<GameObject> targets)
        {
            int removed = 0;
            foreach (var target in targets.Distinct().ToList())
            {
                if (SetOf(target) == null) continue;
                Undo.SetTransformParent(target.transform, map.transform, "Remove from MapLab Set");
                removed++;
            }
            return removed;
        }

        public static void Rename(MapLabSet set, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Undo.RecordObject(set.gameObject, "Rename MapLab Set");
            set.gameObject.name = name.Trim();
        }

        public static void Recolor(MapLabSet set, Color color)
        {
            Undo.RecordObject(set, "Change MapLab Set Colour");
            set.SetColor(color);
            EditorUtility.SetDirty(set);
        }

        public static void Delete(MapLabMap map, MapLabSet set)
        {
            Remove(map, Members(set));
            Undo.DestroyObjectImmediate(set.gameObject);
        }

        public static void Locate(IList<GameObject> targets)
        {
            var alive = targets.Where(target => target != null).ToArray();
            if (alive.Length == 0) return;
            Selection.objects = alive;
            EditorGUIUtility.PingObject(alive[0]);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }

        public static ExtrudedShape GroundOf(ExtrudedShape shape)
        {
            while (shape.ParentShape != null) shape = shape.ParentShape;
            return shape;
        }

        public static List<(ExtrudedShape Shape, int Depth)> TierStack(ExtrudedShape shape)
        {
            var stack = new List<(ExtrudedShape Shape, int Depth)>();
            CollectTiers(GroundOf(shape), 0, stack);
            return stack;
        }

        public static string TierLabel(int depth)
        {
            return depth == 0 ? "Tier 1 (ground)" : $"Tier {depth + 1}";
        }

        public static string TypeLabel(GameObject target)
        {
            if (target.GetComponent<MapLabSet>() != null) return "Set";
            if (target.GetComponent<PathShape>() != null) return "Path";
            if (target.GetComponent<ExtrudedShape>() is ExtrudedShape shape)
            {
                if (shape.Height <= 0f) return "Flat shape";
                if (shape.Type == ExtrudedShapeType.Void) return "Void shape";
                if (shape.Type == ExtrudedShapeType.Ring) return "Ring shape";
                if (shape.Points.Count == 4 && Enumerable.Range(0, 4).All(shape.IsSharp)) return "Box shape";
                int tiers = TierStack(shape).Count - 1;
                return tiers == 0 ? "Plateau" : tiers == 1 ? "Plateau, 1 tier on top" : $"Plateau, {tiers} tiers on top";
            }
            if (target.GetComponent<SpriteRenderer>() != null) return "Sprite";
            return target.transform.childCount > 0 ? $"Group, {target.transform.childCount} children" : "Object";
        }
        #endregion

        #region [Private Methods]
        private static void CollectTiers(ExtrudedShape shape, int depth, List<(ExtrudedShape Shape, int Depth)> stack)
        {
            stack.Add((shape, depth));
            foreach (Transform child in shape.transform)
            {
                var tier = child.GetComponent<ExtrudedShape>();
                if (tier != null) CollectTiers(tier, depth + 1, stack);
            }
        }

        private static bool IsGenerated(GameObject target)
        {
            return (target.hideFlags & HideFlags.DontSave) != 0;
        }

        private static string UniqueName(MapLabMap map, string name)
        {
            var taken = new HashSet<string>(SetsOf(map).Select(set => set.name));
            if (!taken.Contains(name)) return name;
            int index = 2;
            while (taken.Contains($"{name} {index}")) index++;
            return $"{name} {index}";
        }
        #endregion
    }
}
