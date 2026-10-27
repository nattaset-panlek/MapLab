using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabWalkableArea
    {
        #region [Variables/Fields]
        public const float Margin = 0f;
        private const string BoundsProperty = "_bounds";
        private const float Epsilon = 0.0001f;
        #endregion

        #region [Public Methods]
        public static Vector2Int Cells(Rect area, float cellSize)
        {
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(area.width / cellSize - Epsilon)), Mathf.Max(1, Mathf.CeilToInt(area.height / cellSize - Epsilon)));
        }

        public static Rect SnapOutward(Rect area, float cellSize)
        {
            float xMin = Mathf.Floor(area.xMin / cellSize + Epsilon) * cellSize;
            float yMin = Mathf.Floor(area.yMin / cellSize + Epsilon) * cellSize;
            float xMax = Mathf.Ceil(area.xMax / cellSize - Epsilon) * cellSize;
            float yMax = Mathf.Ceil(area.yMax / cellSize - Epsilon) * cellSize;
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMax, xMin + cellSize), Mathf.Max(yMax, yMin + cellSize));
        }

        public static Rect SnapNearest(Rect area, float cellSize)
        {
            float xMin = Mathf.Round(area.xMin / cellSize) * cellSize;
            float yMin = Mathf.Round(area.yMin / cellSize) * cellSize;
            float xMax = Mathf.Round(area.xMax / cellSize) * cellSize;
            float yMax = Mathf.Round(area.yMax / cellSize) * cellSize;
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMax, xMin + cellSize), Mathf.Max(yMax, yMin + cellSize));
        }

        public static bool TryFit(IEnumerable<Bounds> parts, float cellSize, float margin, out Rect area)
        {
            bool any = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;
            foreach (var part in parts)
            {
                min = any ? Vector2.Min(min, part.min) : (Vector2)part.min;
                max = any ? Vector2.Max(max, part.max) : (Vector2)part.max;
                any = true;
            }
            area = any ? SnapOutward(Rect.MinMaxRect(min.x - margin, min.y - margin, max.x + margin, max.y + margin), cellSize) : default;
            return any;
        }

        public static bool Covers(Rect area, Bounds part)
        {
            return part.min.x >= area.xMin - Epsilon && part.min.y >= area.yMin - Epsilon && part.max.x <= area.xMax + Epsilon && part.max.y <= area.yMax + Epsilon;
        }

        public static List<Bounds> Content(MapLabMap map)
        {
            Physics2D.SyncTransforms();
            var parts = new List<Bounds>();
            foreach (var renderer in map.GetComponentsInChildren<Renderer>())
            {
                if (renderer.GetComponentInParent<MapLabBakedLayer>(true) != null) continue;
                if (TryMeshBounds(renderer, out Bounds bounds) || TryRendererBounds(renderer, out bounds)) parts.Add(bounds);
            }
            foreach (var collider in map.GetComponentsInChildren<Collider2D>())
            {
                if (collider.enabled) parts.Add(collider.bounds);
            }
            return parts;
        }

        public static bool FitToMap(MapLabMap map)
        {
            if (!TryFit(Content(map), map.CellSize, Margin, out Rect area)) return false;
            SetBounds(map, area, "Fit Walkable Area to Map");
            return true;
        }

        public static void SetBounds(MapLabMap map, Rect area, string undoName)
        {
            var serialized = new SerializedObject(map);
            serialized.FindProperty(BoundsProperty).rectValue = area;
            if (serialized.ApplyModifiedProperties()) Undo.SetCurrentGroupName(undoName);
        }
        #endregion

        #region [Private Methods]
        private static bool TryMeshBounds(Renderer renderer, out Bounds bounds)
        {
            bounds = default;
            if (!(renderer is MeshRenderer) || !renderer.TryGetComponent(out MeshFilter filter)) return false;
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return false;
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            return true;
        }

        private static bool TryRendererBounds(Renderer renderer, out Bounds bounds)
        {
            bounds = renderer.bounds;
            return bounds.size.x > Epsilon || bounds.size.y > Epsilon;
        }
        #endregion
    }
}
