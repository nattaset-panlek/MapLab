using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabChunkCoverage
    {
        #region [Public Methods]
        public static Dictionary<Vector2Int, List<int>> Map(IReadOnlyList<Renderer> renderers, float chunkSize)
        {
            var map = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < renderers.Count; i++)
            {
                foreach (var index in Of(renderers[i], chunkSize))
                {
                    if (!map.TryGetValue(index, out List<int> list)) map[index] = list = new List<int>();
                    list.Add(i);
                }
            }
            return map;
        }

        public static HashSet<Vector2Int> Of(Renderer renderer, float chunkSize)
        {
            var chunks = new HashSet<Vector2Int>();
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.isReadable)
            {
                AddTriangles(chunks, filter.sharedMesh, renderer.transform, chunkSize);
                return chunks;
            }
            Bounds bounds = renderer.bounds;
            Add(chunks, Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y), chunkSize);
            return chunks;
        }

        public static HashSet<Vector2Int> Of(IEnumerable<Renderer> renderers, float chunkSize)
        {
            var chunks = new HashSet<Vector2Int>();
            foreach (var renderer in renderers) chunks.UnionWith(Of(renderer, chunkSize));
            return chunks;
        }
        #endregion

        #region [Private Methods]
        private static void AddTriangles(HashSet<Vector2Int> chunks, Mesh mesh, Transform transform, float chunkSize)
        {
            Vector3[] vertices = mesh.vertices;
            var world = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) world[i] = transform.TransformPoint(vertices[i]);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector2 a = world[indices[i]], b = world[indices[i + 1]], c = world[indices[i + 2]];
                    var box = Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.y, Mathf.Min(b.y, c.y)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    RectInt range = MapLabChunkLayer.ChunkRange(box, chunkSize);
                    if (range.width * range.height <= 1)
                    {
                        Add(chunks, box, chunkSize);
                        continue;
                    }
                    foreach (var index in range.allPositionsWithin)
                    {
                        if (Overlaps(a, b, c, MapLabChunkLayer.ChunkRect(index, chunkSize))) chunks.Add(index);
                    }
                }
            }
        }

        private static bool Overlaps(Vector2 a, Vector2 b, Vector2 c, Rect rect)
        {
            return !Separates(a, b, c, b - a, rect) && !Separates(a, b, c, c - b, rect) && !Separates(a, b, c, a - c, rect);
        }

        private static bool Separates(Vector2 a, Vector2 b, Vector2 c, Vector2 edge, Rect rect)
        {
            var axis = new Vector2(-edge.y, edge.x);
            if (axis.sqrMagnitude < 1e-12f) return false;
            float t0 = Vector2.Dot(a, axis), t1 = Vector2.Dot(b, axis), t2 = Vector2.Dot(c, axis);
            float tMin = Mathf.Min(t0, Mathf.Min(t1, t2)), tMax = Mathf.Max(t0, Mathf.Max(t1, t2));
            float r0 = Vector2.Dot(new Vector2(rect.xMin, rect.yMin), axis), r1 = Vector2.Dot(new Vector2(rect.xMax, rect.yMin), axis);
            float r2 = Vector2.Dot(new Vector2(rect.xMax, rect.yMax), axis), r3 = Vector2.Dot(new Vector2(rect.xMin, rect.yMax), axis);
            float rMin = Mathf.Min(Mathf.Min(r0, r1), Mathf.Min(r2, r3)), rMax = Mathf.Max(Mathf.Max(r0, r1), Mathf.Max(r2, r3));
            return tMax <= rMin || rMax <= tMin;
        }

        private static void Add(HashSet<Vector2Int> chunks, Rect area, float chunkSize)
        {
            if (area.width <= 0f && area.height <= 0f) return;
            RectInt range = MapLabChunkLayer.ChunkRange(area, chunkSize);
            if (range.width == 0 || range.height == 0) range = new RectInt(range.x, range.y, Mathf.Max(1, range.width), Mathf.Max(1, range.height));
            foreach (var index in range.allPositionsWithin) chunks.Add(index);
        }
        #endregion
    }
}
