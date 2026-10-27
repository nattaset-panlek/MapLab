using System.Collections.Generic;
using MapLab.Shapes;
using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabElevationSortCheck
    {
        #region [Variables/Fields]
        public const float SampleStep = 0.04f;
        public const float DepthTolerance = 0.02f;

        private const float ShadowAlpha = 0.9f;
        private const float AlphaCutoff = 0.5f;

        private readonly List<Standee> _standees = new List<Standee>();
        private readonly List<Face> _faces = new List<Face>();
        private int _samples;

        private readonly struct Standee
        {
            public readonly string Name;
            public readonly float GroundY;
            public readonly float SortY;
            public readonly SpriteRenderer Renderer;

            public Standee(string name, float groundY, float sortY, SpriteRenderer renderer)
            {
                Name = name;
                GroundY = groundY;
                SortY = sortY;
                Renderer = renderer;
            }

            public bool Covers(Vector2 point)
            {
                Sprite sprite = Renderer.sprite;
                if (sprite == null) return false;
                Vector2 local = Renderer.transform.InverseTransformPoint(point);
                Vector2 pixel = local * sprite.pixelsPerUnit + sprite.pivot;
                Rect rect = sprite.rect;
                if (pixel.x < 0f || pixel.y < 0f || pixel.x >= rect.width || pixel.y >= rect.height) return false;
                return sprite.texture.GetPixel((int)(rect.x + pixel.x), (int)(rect.y + pixel.y)).a >= AlphaCutoff;
            }
        }

        private readonly struct Face
        {
            public readonly string Name;
            public readonly float SortY;
            public readonly bool IsWall;
            private readonly Vector2 _a;
            private readonly Vector2 _b;
            private readonly Vector2 _c;
            private readonly Vector2 _baseFrom;
            private readonly Vector2 _baseTo;
            private readonly float _elevation;
            private readonly Rect _box;
            private readonly bool _interpolated;
            private readonly float _depthA;
            private readonly float _depthB;
            private readonly float _depthC;

            public Face(string name, float sortY, Vector2 a, Vector2 b, Vector2 c, float topElevation)
                : this(name, sortY, a, b, c, Vector2.zero, Vector2.zero, topElevation, false)
            {
            }

            public Face(string name, float sortY, Vector2 a, Vector2 b, Vector2 c, Vector2 baseFrom, Vector2 baseTo, float baseElevation)
                : this(name, sortY, a, b, c, baseFrom, baseTo, baseElevation, true)
            {
            }

            public Face(string name, float sortY, Vector2 a, Vector2 b, Vector2 c, float depthA, float depthB, float depthC, bool wall)
                : this(name, sortY, a, b, c, Vector2.zero, Vector2.zero, 0f, wall)
            {
                _interpolated = true;
                _depthA = depthA;
                _depthB = depthB;
                _depthC = depthC;
            }

            private Face(string name, float sortY, Vector2 a, Vector2 b, Vector2 c, Vector2 baseFrom, Vector2 baseTo, float elevation, bool wall)
            {
                Name = name;
                SortY = sortY;
                IsWall = wall;
                _a = a;
                _b = b;
                _c = c;
                _baseFrom = baseFrom;
                _baseTo = baseTo;
                _elevation = elevation;
                _interpolated = false;
                _depthA = 0f;
                _depthB = 0f;
                _depthC = 0f;
                Vector2 min = Vector2.Min(a, Vector2.Min(b, c));
                Vector2 max = Vector2.Max(a, Vector2.Max(b, c));
                _box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            public bool Contains(Vector2 point)
            {
                if (point.x < _box.xMin || point.x > _box.xMax || point.y < _box.yMin || point.y > _box.yMax) return false;
                float ab = Cross(point, _a, _b);
                float bc = Cross(point, _b, _c);
                float ca = Cross(point, _c, _a);
                bool negative = ab < 0f || bc < 0f || ca < 0f;
                bool positive = ab > 0f || bc > 0f || ca > 0f;
                return !(negative && positive);
            }

            public float Depth(Vector2 point)
            {
                if (_interpolated)
                {
                    float total = Cross(_c, _a, _b);
                    if (Mathf.Abs(total) < 0.000001f) return (_depthA + _depthB + _depthC) / 3f;
                    return (Cross(point, _b, _c) * _depthA + Cross(point, _c, _a) * _depthB + Cross(point, _a, _b) * _depthC) / total;
                }
                if (!IsWall) return point.y - _elevation;
                float span = _baseTo.x - _baseFrom.x;
                float t = Mathf.Abs(span) < 0.00001f ? 0f : Mathf.Clamp01((point.x - _baseFrom.x) / span);
                return Mathf.Lerp(_baseFrom.y, _baseTo.y, t) - _elevation;
            }
        }
        #endregion

        #region [Properties]
        public int Samples => _samples;
        #endregion

        #region [Public Methods]
        public void AddShape(ExtrudedShape shape)
        {
            Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
            if (visual == null) return;
            foreach (Transform piece in visual)
            {
                var filter = piece.GetComponent<MeshFilter>();
                var renderer = piece.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;

                Mesh mesh = filter.sharedMesh;
                Vector3[] local = mesh.vertices;
                var world = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++) world[i] = piece.TransformPoint(local[i]);
                Color[] colors = mesh.colors;
                string name = $"{shape.name}/{piece.name}";
                float sortY = piece.position.y;

                int[] tops = mesh.GetTriangles(ShapeMeshData.TopSubmesh);
                for (int i = 0; i + 2 < tops.Length; i += 3)
                {
                    _faces.Add(new Face(name, sortY, world[tops[i]], world[tops[i + 1]], world[tops[i + 2]], shape.TopElevation));
                }

                int[] walls = mesh.GetTriangles(ShapeMeshData.WallSubmesh);
                for (int i = 0; i + 2 < walls.Length; i += 3)
                {
                    int start = Mathf.Min(walls[i], Mathf.Min(walls[i + 1], walls[i + 2]));
                    if (start < colors.Length && colors[start].a < ShadowAlpha) continue;
                    _faces.Add(new Face(name, sortY, world[walls[i]], world[walls[i + 1]], world[walls[i + 2]], world[start], world[start + 1], shape.BaseElevation));
                }
            }
        }

        public void AddRamp(RampShape ramp)
        {
            Transform visual = ramp.transform.Find(RampShape.VisualName);
            if (visual == null || ramp.Parent == null) return;
            Transform space = ramp.Parent.transform;
            var grounds = new List<Vector2>();
            foreach (Transform piece in visual)
            {
                var filter = piece.GetComponent<MeshFilter>();
                var renderer = piece.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;

                Mesh mesh = filter.sharedMesh;
                Vector3[] local = mesh.vertices;
                mesh.GetUVs(ShapeMeshData.GroundChannel, grounds);
                if (grounds.Count != local.Length) continue;
                var world = new Vector2[local.Length];
                var depth = new float[local.Length];
                for (int i = 0; i < local.Length; i++)
                {
                    world[i] = piece.TransformPoint(local[i]);
                    depth[i] = space.TransformPoint(grounds[i]).y;
                }
                string name = $"{ramp.name}/{piece.name}";
                float sortY = piece.position.y;
                foreach (int submesh in new[] { ShapeMeshData.TopSubmesh, ShapeMeshData.WallSubmesh })
                {
                    int[] triangles = mesh.GetTriangles(submesh);
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        _faces.Add(new Face(name, sortY, world[a], world[b], world[c], depth[a], depth[b], depth[c], submesh == ShapeMeshData.WallSubmesh));
                    }
                }
            }
        }

        public List<string> Describe(Vector2 point)
        {
            var lines = new List<string>();
            foreach (var standee in _standees)
            {
                if (standee.Covers(point)) lines.Add($"{standee.Name}: depth {standee.GroundY:0.###}, sort {standee.SortY:0.###}");
            }
            foreach (var face in _faces)
            {
                if (face.Contains(point)) lines.Add($"{face.Name}{(face.IsWall ? " wall" : " top")}: depth {face.Depth(point):0.###}, sort {face.SortY:0.###}");
            }
            return lines;
        }

        public void AddStandee(string name, float groundY, float sortY, SpriteRenderer renderer)
        {
            _standees.Add(new Standee(name, groundY, sortY, renderer));
        }

        public List<ElevationSortMiss> Run()
        {
            var misses = new List<ElevationSortMiss>();
            _samples = 0;
            foreach (var standee in _standees)
            {
                Bounds bounds = standee.Renderer.bounds;
                for (float y = bounds.min.y + SampleStep * 0.5f; y < bounds.max.y; y += SampleStep)
                {
                    for (float x = bounds.min.x + SampleStep * 0.5f; x < bounds.max.x; x += SampleStep)
                    {
                        var point = new Vector2(x, y);
                        if (!standee.Covers(point)) continue;
                        _samples++;
                        if (WorstMiss(standee, point, out ElevationSortMiss miss)) misses.Add(miss);
                    }
                }
            }
            return misses;
        }

        public static bool TopPieceSortY(ExtrudedShape shape, Vector2 visualPoint, out float sortY)
        {
            var check = new MapLabElevationSortCheck();
            check.AddShape(shape);
            return check.TopFaceSortY(visualPoint, out sortY);
        }

        public static bool TopPieceSortY(RampShape ramp, Vector2 visualPoint, out float sortY)
        {
            var check = new MapLabElevationSortCheck();
            check.AddRamp(ramp);
            return check.TopFaceSortY(visualPoint, out sortY);
        }
        #endregion

        #region [Private Methods]
        private bool TopFaceSortY(Vector2 visualPoint, out float sortY)
        {
            foreach (var face in _faces)
            {
                if (face.IsWall || !face.Contains(visualPoint)) continue;
                sortY = face.SortY;
                return true;
            }
            sortY = 0f;
            return false;
        }

        private bool WorstMiss(Standee standee, Vector2 point, out ElevationSortMiss worst)
        {
            worst = default;
            float worstGap = 0f;
            foreach (var other in _standees)
            {
                if (ReferenceEquals(other.Renderer, standee.Renderer) || !other.Covers(point)) continue;
                Judge(standee, other.Name, other.GroundY, other.SortY, point, ref worst, ref worstGap);
            }
            foreach (var face in _faces)
            {
                if (!face.Contains(point)) continue;
                Judge(standee, face.Name, face.Depth(point), face.SortY, point, ref worst, ref worstGap);
            }
            return worstGap > 0f;
        }

        private static void Judge(Standee standee, string other, float depth, float sortY, Vector2 point, ref ElevationSortMiss worst, ref float worstGap)
        {
            float gap = depth - standee.GroundY;
            if (Mathf.Abs(gap) <= DepthTolerance) return;
            bool otherShouldCover = gap < 0f;
            bool otherCovers = sortY <= standee.SortY;
            if (otherShouldCover == otherCovers || Mathf.Abs(gap) <= worstGap) return;
            worstGap = Mathf.Abs(gap);
            worst = new ElevationSortMiss(standee.Name, other, point, otherCovers ? ElevationSortMissKind.Hidden : ElevationSortMissKind.ShowsThrough, worstGap);
        }

        private static float Cross(Vector2 point, Vector2 from, Vector2 to)
        {
            return (to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x);
        }
        #endregion
    }
}
