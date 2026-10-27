using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Elevation;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabElevationBaker
    {
        #region [Variables/Fields]
        public const string GroundName = "Ground";

        private const float StepEpsilon = 0.0001f;
        private const float RampPriority = 0.001f;
        private const byte GroundShade = 48;
        private const byte LowestShade = 96;

        public sealed class Region
        {
            public ExtrudedShape Shape;
            public RampShape Ramp;
            public string Name;
            public int Level;
            public int Below;
            public int Upper;
            public float BelowHeight;
            public bool Walkable;
            public Vector2[] Outer;
            public Vector2[] Inner;
            public Rect Box;

            public float Rise => Shape.TopElevation - BelowHeight;
            public float Priority => Ramp != null ? Shape.TopElevation + RampPriority : Shape.TopElevation;

            public bool Contains(Vector2 point)
            {
                return Box.Contains(point) && Inside(Outer, point) && (Inner == null || !Inside(Inner, point));
            }
        }

        public sealed class Report
        {
            public readonly List<Region> Surfaces = new List<Region>();
            public readonly List<Region> Blockers = new List<Region>();
            public readonly List<Region> Ramps = new List<Region>();
            public readonly List<string> DeadRamps = new List<string>();
            public readonly Dictionary<ExtrudedShape, Region> ByShape = new Dictionary<ExtrudedShape, Region>();
            public readonly List<string> Islands = new List<string>();
            public readonly List<string> Overlaps = new List<string>();
            public float StepHeight;

            public bool Any => Surfaces.Count > 0;

            public bool Steppable(Region region)
            {
                return region.Rise <= StepHeight + StepEpsilon;
            }
        }
        #endregion

        #region [Public Methods]
        public static Report Scan(MapLabMap map)
        {
            var report = new Report { StepHeight = map.StepHeight };
            foreach (var shape in map.GetComponentsInChildren<ExtrudedShape>())
            {
                if (!shape.isActiveAndEnabled || shape.Points.Count < shape.MinPointCount) continue;
                Region floor = Floor(report, shape.transform.parent);
                if (shape.WalkOnTop)
                {
                    Region surface = Build(map, shape, report.Surfaces.Count + 1, floor, true);
                    report.Surfaces.Add(surface);
                    report.ByShape[shape] = surface;
                }
                else if (floor != null)
                {
                    Region blocker = Build(map, shape, 0, floor, false);
                    report.Blockers.Add(blocker);
                    report.ByShape[shape] = blocker;
                }
            }

            foreach (var ramp in map.GetComponentsInChildren<RampShape>())
            {
                if (!ramp.isActiveAndEnabled) continue;
                ExtrudedShape parent = ramp.Parent;
                if (parent == null || !report.ByShape.TryGetValue(parent, out Region upper) || !upper.Walkable)
                {
                    report.DeadRamps.Add(PathName(map.transform, ramp.transform));
                    continue;
                }
                report.Ramps.Add(BuildRamp(map, ramp, upper, report.Surfaces.Count + report.Ramps.Count + 1));
            }

            FindIslands(report);
            FindOverlaps(report);
            return report;
        }

        public static WalkableElevationJson Compute(MapLabMap map, WalkableAreaJson walkable)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Report report = Scan(map);
            if (!report.Any) return null;
            if (report.Surfaces.Count + report.Ramps.Count + 1 > WalkableElevationJson.MaxLevels)
            {
                throw new InvalidOperationException($"{report.Surfaces.Count} Walk On Top shapes and {report.Ramps.Count} ramps under '{map.name}'. A map can have at most {WalkableElevationJson.MaxLevels - 1} together.");
            }

            Physics2D.SyncTransforms();
            var data = new WalkableElevationJson
            {
                bounds = new ElevationBounds(walkable.bounds.x, walkable.bounds.y, walkable.bounds.width, walkable.bounds.height),
                cellSize = walkable.cellSize,
                columns = walkable.columns,
                rows = walkable.rows,
                stepHeight = map.StepHeight,
                levels = Levels(report),
                cliffs = Cliffs(report),
                ramps = Ramps(report)
            };
            data.cellLevel = Convert.ToBase64String(Rasterize(map, report, data));
            return data;
        }

        public static string Bake(MapLabMap map, WalkableAreaJson walkable)
        {
            WalkableElevationJson data = Compute(map, walkable);
            string jsonPath = MapLabOutputPaths.ElevationJson(map);
            string maskPath = MapLabOutputPaths.ElevationMask(map);
            var serialized = new SerializedObject(map);
            if (data == null)
            {
                bool removed = AssetDatabase.DeleteAsset(jsonPath) | AssetDatabase.DeleteAsset(maskPath);
                serialized.FindProperty("_elevationData").objectReferenceValue = null;
                serialized.FindProperty("_elevationMask").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return removed ? $"[MapLab] Removed {jsonPath}: nothing under this map is Walk On Top any more" : null;
            }

            WriteFiles(data, jsonPath, maskPath);
            serialized.FindProperty("_elevationData").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            serialized.FindProperty("_elevationMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            int surfaces = data.levels.Length - 1;
            return $"[MapLab] Baked {jsonPath}: {surfaces} {MapLabWalkableObstacles.Plural(surfaces, "surface")} to stand on, {data.ramps.Length} {MapLabWalkableObstacles.Plural(data.ramps.Length, "ramp")}, {data.cliffs.Length} cliff outlines, step height {data.stepHeight:0.###}";
        }

        public static void WriteFiles(WalkableElevationJson data, string jsonPath, string maskPath)
        {
            MapLabOutputPaths.EnsureFolder(Path.GetDirectoryName(jsonPath).Replace('\\', '/'));
            File.WriteAllText(jsonPath, JsonUtility.ToJson(data, true));
            WriteMask(maskPath, data);
            AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
            MapLabWalkableBaker.ConfigureMask(maskPath);
        }

        public static byte[] DecodeLevels(string cellLevel, int count)
        {
            byte[] bytes = Convert.FromBase64String(cellLevel ?? string.Empty);
            if (bytes.Length == count) return bytes;
            var sized = new byte[count];
            Array.Copy(bytes, sized, Mathf.Min(count, bytes.Length));
            return sized;
        }

        public static float RampHeight(ElevationRamp ramp, Vector2 point)
        {
            var corners = new RampCorners(ramp.top[0], ramp.top[1], ramp.bottom[1], ramp.bottom[0], Vector2.zero, Vector2.zero);
            return RampFormula.Height(corners, ramp.lowHeight, ramp.highHeight, ramp.stairs ? ramp.steps : 0, point);
        }

        public static string StampText(MapLabMap map)
        {
            Report report = Scan(map);
            if (!report.Any) return string.Empty;
            var text = new StringBuilder("elevation|").Append(Number(map.StepHeight)).Append('\n');
            foreach (var region in report.Surfaces.Concat(report.Blockers).Concat(report.Ramps)) text.Append(RegionText(region)).Append('\n');
            return text.ToString();
        }

        public static string RegionText(Region region)
        {
            var text = new StringBuilder();
            ExtrudedShape shape = region.Shape;
            if (region.Ramp != null)
            {
                text.Append("ramp|").Append(region.Name).Append('|').Append(region.Below).Append('|').Append(region.Upper).Append('|');
                text.Append(Number(region.Ramp.LowElevation)).Append('|').Append(Number(region.Ramp.HighElevation)).Append('|');
                foreach (var point in region.Outer) text.Append(Number(point.x)).Append(',').Append(Number(point.y)).Append(' ');
                return text.ToString();
            }
            text.Append(region.Name).Append('|').Append(region.Walkable ? 1 : 0).Append('|').Append((int)shape.Type).Append('|');
            text.Append(Number(shape.TopElevation)).Append('|').Append(Number(shape.SurfaceSortBias)).Append('|').Append(Number(shape.RimWidth)).Append('|');
            foreach (var point in region.Outer) text.Append(Number(point.x)).Append(',').Append(Number(point.y)).Append(' ');
            if (region.Inner == null) return text.ToString();
            text.Append('/');
            foreach (var point in region.Inner) text.Append(Number(point.x)).Append(',').Append(Number(point.y)).Append(' ');
            return text.ToString();
        }
        #endregion

        #region [Private Methods]
        private static Region Floor(Report report, Transform start)
        {
            for (Transform current = start; current != null; current = current.parent)
            {
                var shape = current.GetComponent<ExtrudedShape>();
                if (shape != null && report.ByShape.TryGetValue(shape, out Region region) && region.Walkable) return region;
            }
            return null;
        }

        private static Region Build(MapLabMap map, ExtrudedShape shape, int level, Region floor, bool walkable)
        {
            List<Vector2> footprint = shape.Footprint();
            Vector2[] outer = World(shape.transform, footprint);
            Vector2[] inner = shape.Type == ExtrudedShapeType.Ring ? World(shape.transform, ExtrudeFormula.Offset(footprint, -shape.RimWidth)) : null;
            Vector2 min = outer[0];
            Vector2 max = outer[0];
            foreach (var point in outer)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return new Region
            {
                Shape = shape,
                Name = PathName(map.transform, shape.transform),
                Level = level,
                Below = floor != null ? floor.Level : 0,
                BelowHeight = floor != null ? floor.Shape.TopElevation : 0f,
                Walkable = walkable,
                Outer = outer,
                Inner = inner,
                Box = Rect.MinMaxRect(min.x, min.y, max.x, max.y)
            };
        }

        private static Region BuildRamp(MapLabMap map, RampShape ramp, Region upper, int level)
        {
            RampCorners corners = ramp.WorldCorners();
            Vector2[] outer = corners.Footprint();
            Vector2 min = outer[0];
            Vector2 max = outer[0];
            foreach (var point in outer)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return new Region
            {
                Shape = upper.Shape,
                Ramp = ramp,
                Name = PathName(map.transform, ramp.transform),
                Level = level,
                Below = upper.Below,
                Upper = upper.Level,
                BelowHeight = upper.BelowHeight,
                Walkable = true,
                Outer = outer,
                Box = Rect.MinMaxRect(min.x, min.y, max.x, max.y)
            };
        }

        private static void FindIslands(Report report)
        {
            var reachable = new HashSet<int> { 0 };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var surface in report.Surfaces)
                {
                    if (reachable.Contains(surface.Level) || !reachable.Contains(surface.Below) || !report.Steppable(surface)) continue;
                    reachable.Add(surface.Level);
                    grew = true;
                }
                foreach (var ramp in report.Ramps)
                {
                    if (reachable.Contains(ramp.Upper) || !reachable.Contains(ramp.Below)) continue;
                    reachable.Add(ramp.Upper);
                    grew = true;
                }
            }
            foreach (var surface in report.Surfaces)
            {
                if (!reachable.Contains(surface.Level)) report.Islands.Add(surface.Name);
            }
        }

        private static ElevationRamp[] Ramps(Report report)
        {
            var ramps = new List<ElevationRamp>();
            foreach (var region in report.Ramps)
            {
                RampCorners corners = region.Ramp.WorldCorners();
                ramps.Add(new ElevationRamp
                {
                    id = region.Level,
                    name = region.Name,
                    lower = region.Below,
                    upper = region.Upper,
                    lowHeight = Round(region.Ramp.LowElevation),
                    highHeight = Round(region.Ramp.HighElevation),
                    sortBias = Round(region.Ramp.SurfaceSortBias),
                    stairs = region.Ramp.Style == RampStyle.Stairs,
                    steps = region.Ramp.Style == RampStyle.Stairs ? region.Ramp.Steps : 0,
                    polygon = MapLabWalkableBaker.Round((Vector2[])region.Outer.Clone()),
                    top = MapLabWalkableBaker.Round(new[] { corners.TopLeft, corners.TopRight }),
                    bottom = MapLabWalkableBaker.Round(new[] { corners.BottomLeft, corners.BottomRight })
                });
            }
            return ramps.ToArray();
        }

        private static ElevationOpening Opening(Region ramp)
        {
            RampCorners corners = ramp.Ramp.WorldCorners();
            Vector2[] edge = MapLabWalkableBaker.Round(new[] { corners.TopLeft, corners.TopRight });
            return new ElevationOpening(edge[0], edge[1], ramp.Level);
        }

        private static ElevationLevel[] Levels(Report report)
        {
            var levels = new List<ElevationLevel> { new ElevationLevel(0, GroundName, 0f, 0f, 0) };
            foreach (var surface in report.Surfaces)
            {
                levels.Add(new ElevationLevel(surface.Level, surface.Name, Round(surface.Shape.TopElevation), Round(surface.Shape.SurfaceSortBias), surface.Below));
            }
            return levels.ToArray();
        }

        private static ElevationCliff[] Cliffs(Report report)
        {
            var cliffs = new List<ElevationCliff>();
            foreach (var surface in report.Surfaces.Where(surface => !report.Steppable(surface)))
            {
                ElevationOpening[] openings = report.Ramps.Where(ramp => ramp.Upper == surface.Level).Select(Opening).ToArray();
                cliffs.Add(new ElevationCliff(surface.Level, false, MapLabWalkableBaker.Wind(MapLabWalkableBaker.Round((Vector2[])surface.Outer.Clone()), true), openings));
                if (surface.Inner != null) cliffs.Add(new ElevationCliff(surface.Level, true, MapLabWalkableBaker.Wind(MapLabWalkableBaker.Round((Vector2[])surface.Inner.Clone()), false), new ElevationOpening[0]));
            }
            return cliffs.ToArray();
        }

        private static byte[] Rasterize(MapLabMap map, Report report, WalkableElevationJson data)
        {
            List<Region> regions = report.Blockers.Concat(report.Surfaces).Concat(report.Ramps).OrderByDescending(region => region.Priority).ToList();
            var blocks = new Dictionary<Collider2D, (int Level, ExtrudedShape Owner)>();
            foreach (var collider in MapLabWalkableBaker.CollectObstacles(map)) blocks[collider] = BlockedLevel(report, collider);

            ContactFilter2D filter = MapLabWalkableBaker.ObstacleFilter(map.ObstacleLayers);
            Vector2 size = MapLabWalkableBaker.CellProbeSize(data.cellSize);
            var hits = new List<Collider2D>();
            var cells = new byte[data.CellCount];
            for (int row = 0; row < data.rows; row++)
            {
                for (int column = 0; column < data.columns; column++)
                {
                    Vector2 center = ElevationMap.CellCenter(data.bounds, data.cellSize, column, row);
                    Region top = regions.FirstOrDefault(region => region.Contains(center));
                    bool blocked = top != null && !top.Walkable;
                    int level = top != null && top.Walkable ? top.Level : 0;
                    int standsOn = top != null && top.Ramp != null ? top.Below : level;
                    ExtrudedShape climbed = top != null && top.Ramp != null ? top.Shape : null;
                    if (!blocked)
                    {
                        int count = Physics2D.OverlapBox(center, size, 0f, filter, hits);
                        for (int i = 0; i < count && !blocked; i++)
                        {
                            blocked = blocks.TryGetValue(hits[i], out var block) && block.Level == standsOn && (climbed == null || block.Owner != climbed);
                        }
                    }
                    cells[row * data.columns + column] = blocked ? WalkableElevationJson.Blocked : (byte)level;
                }
            }
            return cells;
        }

        private static (int Level, ExtrudedShape Owner) BlockedLevel(Report report, Collider2D collider)
        {
            Transform holder = collider.transform;
            if (holder.name == RampShape.CollisionName && holder.parent != null && holder.parent.GetComponent<RampShape>() != null) return (-1, null);
            ExtrudedShape owner = collider.GetComponent<ExtrudedShape>();
            if (owner == null && holder.name == ExtrudedShape.CollisionName && holder.parent != null) owner = holder.parent.GetComponent<ExtrudedShape>();
            if (owner == null)
            {
                Region floor = Floor(report, holder);
                return (floor != null ? floor.Level : 0, null);
            }
            if (report.ByShape.TryGetValue(owner, out Region region))
            {
                if (region.Walkable && report.Steppable(region)) return (-1, owner);
                return (region.Below, owner);
            }
            Region below = Floor(report, owner.transform.parent);
            return (below != null ? below.Level : 0, owner);
        }

        private static void FindOverlaps(Report report)
        {
            for (int a = 0; a < report.Surfaces.Count; a++)
            {
                for (int b = a + 1; b < report.Surfaces.Count; b++)
                {
                    Region first = report.Surfaces[a];
                    Region second = report.Surfaces[b];
                    if (first.Shape.transform.IsChildOf(second.Shape.transform) || second.Shape.transform.IsChildOf(first.Shape.transform)) continue;
                    if (!first.Box.Overlaps(second.Box)) continue;
                    if (first.Outer.Any(second.Contains) || second.Outer.Any(first.Contains)) report.Overlaps.Add($"{first.Name} and {second.Name}");
                }
            }
        }

        private static void WriteMask(string path, WalkableElevationJson data)
        {
            byte[] cells = DecodeLevels(data.cellLevel, data.CellCount);
            float highest = Mathf.Max(0.0001f, data.levels.Max(level => level.height));
            var shades = new Dictionary<int, byte>();
            foreach (var level in data.levels) shades[level.id] = level.id == 0 ? GroundShade : Shade(level.height, highest);
            var ramps = (data.ramps ?? new ElevationRamp[0]).ToDictionary(ramp => ramp.id);

            var pixels = new Color32[cells.Length];
            for (int row = 0; row < data.rows; row++)
            {
                for (int column = 0; column < data.columns; column++)
                {
                    int i = row * data.columns + column;
                    byte shade = shades.TryGetValue(cells[i], out byte value) ? value : (byte)0;
                    if (ramps.TryGetValue(cells[i], out ElevationRamp ramp)) shade = Shade(RampHeight(ramp, ElevationMap.CellCenter(data.bounds, data.cellSize, column, row)), highest);
                    pixels[i] = new Color32(shade, shade, shade, 255);
                }
            }
            var texture = new Texture2D(data.columns, data.rows, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static byte Shade(float height, float highest)
        {
            return height <= StepEpsilon ? GroundShade : (byte)Mathf.RoundToInt(Mathf.Lerp(LowestShade, 255f, Mathf.Clamp01(height / highest)));
        }

        private static Vector2[] World(Transform transform, IReadOnlyList<Vector2> local)
        {
            var world = new Vector2[local.Count];
            for (int i = 0; i < local.Count; i++) world[i] = transform.TransformPoint(local[i]);
            return world;
        }

        private static bool Inside(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y)
                    && point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static string PathName(Transform root, Transform transform)
        {
            var names = new List<string>();
            for (Transform current = transform; current != null && current != root; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static float Round(float value)
        {
            return Mathf.Round(value * 1000f) / 1000f;
        }

        private static string Number(float value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
        #endregion
    }
}
