using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabWalkableBaker
    {
        #region [Variables/Fields]

        private const int CircleSegments = 16;
        private const float SkinInsets = 3f;
        private const float Precision = 1000f;
        private static readonly Color32 WalkableColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 BlockedColor = new Color32(0, 0, 0, 255);
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Bake Walkable Area (Open Scenes)", false, 20)]
        public static void BakeOpenScenesFromMenu()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var map in FindMaps(scene))
                {
                    Debug.Log(Bake(map));
                    count++;
                }
            }
            if (count == 0) Debug.LogWarning("[MapLab] No MapLabMap found in the open scenes.");
        }

        public static string Bake(MapLabMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            WalkableAreaJson data = Compute(map);
            string jsonPath = MapLabOutputPaths.WalkableJson(map);
            string maskPath = MapLabOutputPaths.WalkableMask(map);
            MapLabOutputPaths.EnsureFolder(MapLabOutputPaths.MapFolder(map));

            File.WriteAllText(jsonPath, JsonUtility.ToJson(data, true));
            WriteMask(maskPath, data);
            AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureMask(maskPath);

            var serialized = new SerializedObject(map);
            serialized.FindProperty("_walkableData").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            serialized.FindProperty("_walkableMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            string elevation = MapLabElevationBaker.Bake(map, data);
            MapLabBakeStatus.RecordWalkableStamp(map);
            MapLabWalkableChanges.Save(map, MapLabWalkableChanges.Take(map));
            MapLabWalkableScene.Invalidate();
            if (map.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);

            int walkable = DecodeCells(data.cells, data.CellCount).Count(cell => cell);
            string summary = $"[MapLab] Baked {jsonPath}: {data.columns}x{data.rows} cells of {data.cellSize}, {walkable * 100f / data.CellCount:0.#}% walkable, {data.holes.Length} obstacle outlines";
            return elevation == null ? summary : $"{summary}\n{elevation}";
        }

        public static WalkableAreaJson Compute(MapLabMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Physics2D.SyncTransforms();
            Rect bounds = map.Bounds;
            float cellSize = map.CellSize;
            List<Collider2D> obstacles = CollectObstacles(map);
            Vector2Int cells = MapLabWalkableArea.Cells(bounds, cellSize);

            var data = new WalkableAreaJson
            {
                bounds = new WalkableBounds(bounds),
                cellSize = cellSize,
                columns = cells.x,
                rows = cells.y,
                outline = Wind(Round(new[] { bounds.min, new Vector2(bounds.xMax, bounds.yMin), bounds.max, new Vector2(bounds.xMin, bounds.yMax) }), true)
            };
            data.cells = EncodeCells(Rasterize(data, obstacles, map.ObstacleLayers));
            data.holes = BuildHoles(obstacles);
            return data;
        }

        public static string EncodeCells(bool[] walkable)
        {
            var bytes = new byte[(walkable.Length + 7) / 8];
            for (int i = 0; i < walkable.Length; i++)
            {
                if (walkable[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
            }
            return Convert.ToBase64String(bytes);
        }

        public static bool[] DecodeCells(string cells, int count)
        {
            byte[] bytes = Convert.FromBase64String(cells ?? string.Empty);
            var walkable = new bool[count];
            for (int i = 0; i < count && (i >> 3) < bytes.Length; i++)
            {
                walkable[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            }
            return walkable;
        }

        public static float SignedArea(IReadOnlyList<Vector2> points)
        {
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        public static List<MapLabMap> FindMaps(Scene scene)
        {
            var maps = new List<MapLabMap>();
            foreach (var root in scene.GetRootGameObjects())
            {
                maps.AddRange(root.GetComponentsInChildren<MapLabMap>(true));
            }
            return maps;
        }

        public static ContactFilter2D ObstacleFilter(LayerMask layers)
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(layers);
            filter.useTriggers = false;
            return filter;
        }

        public static Vector2 CellProbeSize(float cellSize)
        {
            float inset = SkinInsets * Physics2D.defaultContactOffset;
            float side = Mathf.Max(cellSize * 0.5f, cellSize - 2f * inset);
            return new Vector2(side, side);
        }

        public static Vector2 CellCenter(WalkableBounds bounds, float cellSize, int column, int row)
        {
            return new Vector2(bounds.x + (column + 0.5f) * cellSize, bounds.y + (row + 0.5f) * cellSize);
        }

        public static Vector2[] Round(Vector2[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new Vector2(Mathf.Round(points[i].x * Precision) / Precision, Mathf.Round(points[i].y * Precision) / Precision);
            }
            return points;
        }

        public static Vector2[] Wind(Vector2[] points, bool counterClockwise)
        {
            if ((SignedArea(points) > 0f) != counterClockwise) Array.Reverse(points);
            return points;
        }

        public static void ConfigureMask(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        #endregion

        #region [Private Methods]
        public static List<Collider2D> CollectObstacles(MapLabMap map)
        {
            return MapLabWalkableObstacles.Scan(map).Blocking;
        }

        private static bool[] Rasterize(WalkableAreaJson data, List<Collider2D> obstacles, LayerMask layers)
        {
            var walkable = new bool[data.CellCount];
            var obstacleSet = new HashSet<Collider2D>(obstacles);
            ContactFilter2D filter = ObstacleFilter(layers);
            var hits = new List<Collider2D>();
            Vector2 size = CellProbeSize(data.cellSize);

            for (int row = 0; row < data.rows; row++)
            {
                for (int column = 0; column < data.columns; column++)
                {
                    Vector2 center = CellCenter(data.bounds, data.cellSize, column, row);
                    int count = Physics2D.OverlapBox(center, size, 0f, filter, hits);
                    bool blocked = false;
                    for (int i = 0; i < count && !blocked; i++)
                    {
                        blocked = obstacleSet.Contains(hits[i]);
                    }
                    walkable[row * data.columns + column] = !blocked;
                }
            }
            return walkable;
        }

        private static WalkablePolygon[] BuildHoles(List<Collider2D> obstacles)
        {
            var holes = new List<WalkablePolygon>();
            foreach (var collider in obstacles)
            {
                foreach (var points in Outlines(collider))
                {
                    if (points.Length >= 3) holes.Add(new WalkablePolygon(Wind(Round(points), false)));
                }
            }
            return holes.ToArray();
        }

        private static IEnumerable<Vector2[]> Outlines(Collider2D collider)
        {
            Transform transform = collider.transform;
            switch (collider)
            {
                case PolygonCollider2D polygon when !HasNestedPaths(polygon):
                    for (int i = 0; i < polygon.pathCount; i++)
                    {
                        yield return ToWorld(transform, polygon.GetPath(i), polygon.offset);
                    }
                    yield break;
                case BoxCollider2D box:
                    Vector2 half = box.size * 0.5f;
                    yield return ToWorld(transform, new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(half.x, half.y), new Vector2(-half.x, half.y) }, box.offset);
                    yield break;
                case CircleCollider2D circle:
                    yield return Circle(transform, circle);
                    yield break;
                case EdgeCollider2D _:
                    yield break;
            }

            foreach (var triangle in Triangles(collider))
            {
                yield return triangle;
            }
        }

        private static bool HasNestedPaths(PolygonCollider2D polygon)
        {
            for (int outer = 0; outer < polygon.pathCount; outer++)
            {
                Vector2[] container = polygon.GetPath(outer);
                for (int inner = 0; inner < polygon.pathCount; inner++)
                {
                    if (inner == outer) continue;
                    Vector2[] candidate = polygon.GetPath(inner);
                    if (candidate.Length > 0 && Contains(container, candidate[0])) return true;
                }
            }
            return false;
        }

        private static bool Contains(Vector2[] polygon, Vector2 point)
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

        private static Vector2[] ToWorld(Transform transform, Vector2[] local, Vector2 offset)
        {
            var world = new Vector2[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                world[i] = transform.TransformPoint(local[i] + offset);
            }
            return world;
        }

        private static Vector2[] Circle(Transform transform, CircleCollider2D circle)
        {
            Vector2 center = transform.TransformPoint(circle.offset);
            Vector3 scale = transform.lossyScale;
            float radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) / Mathf.Cos(Mathf.PI / CircleSegments);
            var points = new Vector2[CircleSegments];
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        private static IEnumerable<Vector2[]> Triangles(Collider2D collider)
        {
            Mesh mesh = collider.CreateMesh(true, true);
            if (mesh == null) yield break;
            try
            {
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    yield return new Vector2[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void WriteMask(string path, WalkableAreaJson data)
        {
            bool[] walkable = DecodeCells(data.cells, data.CellCount);
            var pixels = new Color32[walkable.Length];
            for (int i = 0; i < walkable.Length; i++)
            {
                pixels[i] = walkable[i] ? WalkableColor : BlockedColor;
            }

            var texture = new Texture2D(data.columns, data.rows, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
        #endregion
    }
}
