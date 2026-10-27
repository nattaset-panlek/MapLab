using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MapLab.Samples.Navigation
{
    public static class NavMesh2DFactory
    {
        #region [Variables/Fields]
        public const int WalkableArea = 0;
        public const int NotWalkableArea = 1;
        public static readonly Quaternion SurfaceRotation = Quaternion.Euler(-90f, 0f, 0f);

        private const float SurfaceThickness = 0.2f;
        private const float ObstacleLift = 0.5f;
        private const float BoundsDepth = 4f;
        private const float AgentHeight = 1f;
        private const float AgentClimb = 0.2f;
        private const float CollinearEpsilon = 1e-7f;
        #endregion

        #region [Public Methods]
        public static NavMeshData Build(Rect walkableArea, IReadOnlyList<Vector2[]> obstacles, float agentRadius, int agentTypeId = 0)
        {
            var sources = new List<NavMeshBuildSource>();
            var meshes = new List<Mesh>();
            try
            {
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(walkableArea.width, SurfaceThickness, walkableArea.height),
                    transform = Matrix4x4.TRS(new Vector3(walkableArea.center.x, walkableArea.center.y, SurfaceThickness * 0.5f), SurfaceRotation, Vector3.one),
                    area = WalkableArea
                });

                if (obstacles != null)
                {
                    foreach (var outline in obstacles)
                    {
                        AddObstacle(outline, sources, meshes);
                    }
                }

                NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeId);
                settings.agentRadius = agentRadius;
                settings.agentHeight = AgentHeight;
                settings.agentClimb = AgentClimb;
                settings.agentSlope = 45f;
                settings.overrideVoxelSize = true;
                settings.voxelSize = Mathf.Max(0.02f, agentRadius / 3f);

                var localBounds = new Bounds(
                    new Vector3(walkableArea.center.x, 0f, walkableArea.center.y),
                    new Vector3(walkableArea.width + 2f, BoundsDepth, walkableArea.height + 2f));
                return NavMeshBuilder.BuildNavMeshData(settings, sources, localBounds, Vector3.zero, SurfaceRotation);
            }
            finally
            {
                foreach (var mesh in meshes)
                {
                    if (Application.isPlaying) Object.Destroy(mesh);
                    else Object.DestroyImmediate(mesh);
                }
            }
        }
        #endregion

        #region [Private Methods]
        private static void AddObstacle(Vector2[] outline, List<NavMeshBuildSource> sources, List<Mesh> meshes)
        {
            if (outline == null || outline.Length < 3) return;

            var points = new List<Vector2>(outline);
            if (SignedArea(points) < 0f) points.Reverse();
            List<int> triangles = Triangulate(points);
            if (triangles.Count == 0) return;

            var vertices = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                vertices[i] = points[i];
            }

            var mesh = new Mesh { vertices = vertices, triangles = triangles.ToArray() };
            meshes.Add(mesh);
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = mesh,
                transform = Matrix4x4.Translate(new Vector3(0f, 0f, -ObstacleLift)),
                area = NotWalkableArea
            });
        }

        private static List<int> Triangulate(List<Vector2> points)
        {
            var remaining = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                remaining.Add(i);
            }

            var triangles = new List<int>(Mathf.Max(0, points.Count - 2) * 3);
            while (remaining.Count > 3 && ClipEar(points, remaining, triangles)) { }
            if (remaining.Count == 3) triangles.AddRange(remaining);
            return triangles;
        }

        private static bool ClipEar(List<Vector2> points, List<int> remaining, List<int> triangles)
        {
            int count = remaining.Count;
            for (int i = 0; i < count; i++)
            {
                int a = remaining[(i + count - 1) % count];
                int b = remaining[i];
                int c = remaining[(i + 1) % count];
                float cross = Cross(points[b] - points[a], points[c] - points[b]);
                if (Mathf.Abs(cross) < CollinearEpsilon)
                {
                    remaining.RemoveAt(i);
                    return true;
                }
                if (cross < 0f || ContainsOther(points, remaining, a, b, c)) continue;

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                remaining.RemoveAt(i);
                return true;
            }
            return false;
        }

        private static bool ContainsOther(List<Vector2> points, List<int> remaining, int a, int b, int c)
        {
            foreach (int index in remaining)
            {
                if (index == a || index == b || index == c) continue;
                Vector2 p = points[index];
                if (Cross(points[b] - points[a], p - points[a]) >= 0f
                    && Cross(points[c] - points[b], p - points[b]) >= 0f
                    && Cross(points[a] - points[c], p - points[c]) >= 0f) return true;
            }
            return false;
        }

        private static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }

        private static float SignedArea(List<Vector2> points)
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
        #endregion
    }
}
