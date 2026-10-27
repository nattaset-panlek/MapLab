using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MapLab.Shapes;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabWalkableChanges
    {
        #region [Variables/Fields]
        public const string FileName = "WalkableObstacles.json";
        private const int GroupsShown = 2;

        [Serializable]
        public class Snapshot
        {
            public string area;
            public string cellSize;
            public int layers;
            public List<Entry> obstacles = new List<Entry>();
            public string stepHeight;
            public List<Entry> surfaces = new List<Entry>();
        }

        [Serializable]
        public struct Entry
        {
            public string id;
            public string name;
            public string hash;
        }
        #endregion

        #region [Public Methods]
        public static Snapshot Take(MapLabMap map)
        {
            Rect area = map.Bounds;
            var snapshot = new Snapshot
            {
                area = $"{Number(area.x)},{Number(area.y)},{Number(area.width)},{Number(area.height)}",
                cellSize = Number(map.CellSize),
                layers = map.ObstacleLayers.value
            };
            foreach (var collider in MapLabWalkableObstacles.Scan(map).Blocking)
            {
                snapshot.obstacles.Add(new Entry { id = Id(map.transform, collider), name = DisplayName(collider), hash = Hash128.Compute(MapLabBakeStatus.ColliderText(collider)).ToString() });
            }
            MapLabElevationBaker.Report heights = MapLabElevationBaker.Scan(map);
            if (!heights.Any) return snapshot;
            snapshot.stepHeight = Number(map.StepHeight);
            foreach (var region in heights.Surfaces.Concat(heights.Blockers).Concat(heights.Ramps))
            {
                Component source = region.Ramp != null ? region.Ramp : region.Shape;
                string name = region.Ramp != null ? region.Ramp.name : region.Walkable ? $"{region.Shape.name} (walk on top)" : region.Shape.name;
                snapshot.surfaces.Add(new Entry { id = Id(map.transform, source), name = name, hash = Hash128.Compute(MapLabElevationBaker.RegionText(region)).ToString() });
            }
            return snapshot;
        }

        public static string Describe(Snapshot before, Snapshot now)
        {
            var parts = new List<string>();
            Compare(before.obstacles, now.obstacles, parts);
            if (before.area != now.area) parts.Add("area changed");
            if (before.cellSize != now.cellSize) parts.Add("cell size changed");
            if (before.layers != now.layers) parts.Add("layers changed");
            Compare(before.surfaces ?? new List<Entry>(), now.surfaces, parts);
            if ((before.stepHeight ?? "") != (now.stepHeight ?? "") && before.surfaces?.Count > 0 && now.surfaces.Count > 0) parts.Add("step height changed");
            return parts.Count == 0 ? null : string.Join(", ", parts.Distinct());
        }

        public static string Describe(MapLabMap map)
        {
            Snapshot before = Load(map);
            return before == null ? null : Describe(before, Take(map));
        }

        public static string Names(List<string> names)
        {
            var groups = names.GroupBy(name => name).Select(group => (Name: group.Key, Count: group.Count())).ToList();
            List<string> shown = groups.Take(GroupsShown).Select(group => group.Count > 1 ? $"{group.Name} x{group.Count}" : group.Name).ToList();
            int rest = groups.Skip(GroupsShown).Sum(group => group.Count);
            return rest > 0 ? $"{string.Join(", ", shown)} and {rest} more" : string.Join(" and ", shown);
        }

        public static string Id(Transform root, Component component)
        {
            var path = new List<string>();
            for (Transform current = component.transform; current != null && current != root; current = current.parent)
            {
                path.Add($"{current.name}#{SameNameIndex(current)}");
            }
            path.Reverse();
            Component[] same = component.GetComponents(component.GetType());
            return $"{string.Join("/", path)}|{component.GetType().Name}#{Array.IndexOf(same, component)}";
        }

        public static string DisplayName(Component component)
        {
            Transform transform = component.transform;
            return transform.name == ExtrudedShape.CollisionName && transform.parent != null ? transform.parent.name : transform.name;
        }

        public static void Save(MapLabMap map, Snapshot snapshot)
        {
            string path = FilePath(map);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(snapshot));
        }

        public static Snapshot Load(MapLabMap map)
        {
            if (string.IsNullOrEmpty(map.gameObject.scene.path)) return null;
            string path = FilePath(map);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static string FilePath(MapLabMap map)
        {
            return $"{MapLabOutputPaths.MapFolder(map)}/{MapLabLayerFiles.DataFolderName}/{FileName}";
        }
        #endregion

        #region [Private Methods]
        private static void Compare(List<Entry> before, List<Entry> now, List<string> parts)
        {
            var old = new Dictionary<string, Entry>();
            foreach (var entry in before) old[entry.id] = entry;
            var seen = new HashSet<string>();
            var changed = new List<string>();
            var added = new List<string>();
            foreach (var entry in now)
            {
                seen.Add(entry.id);
                if (!old.TryGetValue(entry.id, out Entry was)) added.Add(entry.name);
                else if (was.hash != entry.hash) changed.Add(entry.name);
            }
            List<string> removed = before.Where(entry => !seen.Contains(entry.id)).Select(entry => entry.name).ToList();
            if (changed.Count > 0) parts.Add($"{Names(changed)} moved or changed");
            if (added.Count > 0) parts.Add($"{Names(added)} added");
            if (removed.Count > 0) parts.Add($"{Names(removed)} removed");
        }

        private static int SameNameIndex(Transform transform)
        {
            Transform parent = transform.parent;
            if (parent == null) return 0;
            int index = 0;
            int self = transform.GetSiblingIndex();
            for (int i = 0; i < self; i++)
            {
                if (parent.GetChild(i).name == transform.name) index++;
            }
            return index;
        }

        private static string Number(float value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
        #endregion
    }
}
