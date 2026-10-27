using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabWalkableObstacles
    {
        #region [Variables/Fields]
        private const string ColliderSuffix = "Collider2D";

        public sealed class Report
        {
            public readonly List<Collider2D> Blocking = new List<Collider2D>();
            public readonly List<Collider2D> Outside = new List<Collider2D>();
            public readonly Dictionary<string, int> Kinds = new Dictionary<string, int>();
            public int Triggers;
            public int OtherLayers;
            public int TurnedOff;

            public List<KeyValuePair<string, int>> KindsByCount()
            {
                return Kinds.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToList();
            }
        }
        #endregion

        #region [Public Methods]
        public static Report Scan(MapLabMap map)
        {
            Physics2D.SyncTransforms();
            var report = new Report();
            Rect area = map.Bounds;
            int layers = map.ObstacleLayers.value;
            foreach (var collider in map.GetComponentsInChildren<Collider2D>())
            {
                if (!collider.enabled)
                {
                    report.TurnedOff++;
                    continue;
                }
                if (collider.isTrigger)
                {
                    report.Triggers++;
                    continue;
                }
                if ((layers & (1 << collider.gameObject.layer)) == 0)
                {
                    report.OtherLayers++;
                    continue;
                }
                Bounds bounds = collider.bounds;
                if (!MapLabWalkableArea.Covers(area, bounds)) report.Outside.Add(collider);
                if (!area.Overlaps(new Rect(bounds.min, bounds.size))) continue;
                report.Blocking.Add(collider);
                string kind = Kind(collider);
                report.Kinds[kind] = report.Kinds.TryGetValue(kind, out int count) ? count + 1 : 1;
            }
            return report;
        }

        public static string Kind(Collider2D collider)
        {
            string name = collider.GetType().Name;
            return name.EndsWith(ColliderSuffix) && name.Length > ColliderSuffix.Length ? name.Substring(0, name.Length - ColliderSuffix.Length) : name;
        }

        public static string Ignored(Report report)
        {
            var parts = new List<string>();
            if (report.Triggers > 0) parts.Add($"{report.Triggers} {Plural(report.Triggers, "trigger")}");
            if (report.OtherLayers > 0) parts.Add($"{report.OtherLayers} on other layers");
            if (report.TurnedOff > 0) parts.Add($"{report.TurnedOff} turned off");
            return parts.Count == 0 ? "Triggers never block walking." : $"Triggers never block walking. Not counted: {string.Join(", ", parts)}.";
        }

        public static string Plural(int count, string word)
        {
            return count == 1 ? word : word + "s";
        }
        #endregion
    }
}
