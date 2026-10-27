using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Shapes
{
    public static class ExtrudeFormula
    {
        #region [Variables/Fields]
        public const float FacingEpsilon = 0.0001f;
        public static readonly Vector2 WallLight = new Vector2(-0.6f, -0.8f);

        private const float MinMiter = 0.35f;
        private const float CollinearEpsilon = 1e-7f;
        private const float DarkestWall = 0.68f;
        #endregion

        #region [Public Methods]
        public static List<Vector2> SampleClosed(IReadOnlyList<Vector2> points, int stepsPerSpan)
        {
            return Sample(points, null, stepsPerSpan, true);
        }

        public static List<Vector2> Sample(IReadOnlyList<Vector2> points, IReadOnlyList<bool> sharp, int stepsPerSpan, bool closed)
        {
            int count = points.Count;
            var samples = new List<Vector2>(count * Mathf.Max(1, stepsPerSpan) + 1);
            if (count < (closed ? 3 : 2))
            {
                for (int i = 0; i < count; i++) samples.Add(points[i]);
                return samples;
            }

            int spans = closed ? count : count - 1;
            for (int span = 0; span < spans; span++)
            {
                bool straight = IsSharp(sharp, span) && IsSharp(sharp, (span + 1) % count);
                int steps = straight ? 1 : Mathf.Max(1, stepsPerSpan);
                for (int step = 0; step < steps; step++)
                {
                    samples.Add(Evaluate(points, sharp, closed, span, step / (float)steps));
                }
            }
            if (!closed) samples.Add(points[count - 1]);
            return samples;
        }

        public static Vector2 Evaluate(IReadOnlyList<Vector2> points, IReadOnlyList<bool> sharp, bool closed, int span, float t)
        {
            int count = points.Count;
            int next = (span + 1) % count;
            Vector2 p0 = points[span];
            Vector2 p3 = points[next];
            Vector2 p1 = p0 + Tangent(points, sharp, closed, span);
            Vector2 p2 = p3 - Tangent(points, sharp, closed, next);
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        public static bool IsSharp(IReadOnlyList<bool> sharp, int index)
        {
            return sharp != null && index >= 0 && index < sharp.Count && sharp[index];
        }

        public static List<Vector2> ClipBand(IReadOnlyList<Vector2> polygon, float yMin, float yMax)
        {
            List<Vector2> above = ClipHalfPlane(polygon, yMin, true);
            return above.Count < 3 ? above : ClipHalfPlane(above, yMax, false);
        }

        public static void Strip(IReadOnlyList<Vector2> line, float width, List<Vector2> left, List<Vector2> right)
        {
            int count = line.Count;
            left.Clear();
            right.Clear();
            float half = width * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = line[Mathf.Max(0, i - 1)];
                Vector2 next = line[Mathf.Min(count - 1, i + 1)];
                Vector2 direction = (next - previous).normalized;
                var normal = new Vector2(-direction.y, direction.x);
                float miter = 1f;
                if (i > 0 && i < count - 1)
                {
                    Vector2 segment = (line[i + 1] - line[i]).normalized;
                    miter = Mathf.Max(MinMiter, Vector2.Dot(normal, new Vector2(-segment.y, segment.x)));
                }
                left.Add(line[i] + normal * (half / miter));
                right.Add(line[i] - normal * (half / miter));
            }
        }

        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        public static void MakeCounterClockwise(List<Vector2> polygon)
        {
            if (SignedArea(polygon) < 0f) polygon.Reverse();
        }

        public static Vector2 EdgeNormal(Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            return new Vector2(direction.y, -direction.x).normalized;
        }

        public static List<Vector2> Offset(IReadOnlyList<Vector2> polygon, float distance)
        {
            int count = polygon.Count;
            var offset = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = EdgeNormal(polygon[(i - 1 + count) % count], polygon[i]);
                Vector2 next = EdgeNormal(polygon[i], polygon[(i + 1) % count]);
                Vector2 normal = previous + next;
                normal = normal.sqrMagnitude < 0.0001f ? next : normal.normalized;
                float miter = Mathf.Max(MinMiter, Vector2.Dot(normal, next));
                offset.Add(polygon[i] + normal * (distance / miter));
            }
            return offset;
        }

        public static List<WallRun> WallRuns(IReadOnlyList<Vector2> boundary)
        {
            int count = boundary.Count;
            var runs = new List<WallRun>();
            if (count < 3) return runs;

            var normals = new Vector2[count];
            var facing = new bool[count];
            var distanceAt = new float[count];
            int firstHidden = -1;
            for (int i = 0; i < count; i++)
            {
                normals[i] = EdgeNormal(boundary[i], boundary[(i + 1) % count]);
                facing[i] = normals[i].y < -FacingEpsilon;
                if (!facing[i] && firstHidden < 0) firstHidden = i;
                if (i > 0) distanceAt[i] = distanceAt[i - 1] + Vector2.Distance(boundary[i - 1], boundary[i]);
            }
            if (firstHidden < 0) return runs;

            int edge = (firstHidden + 1) % count;
            int visited = 0;
            while (visited < count)
            {
                if (!facing[edge])
                {
                    edge = (edge + 1) % count;
                    visited++;
                    continue;
                }

                var points = new List<Vector2> { boundary[edge] };
                var pointNormals = new List<Vector2> { normals[edge] };
                float distance = distanceAt[edge];
                var distances = new List<float> { distance };
                while (visited < count && facing[edge])
                {
                    int next = (edge + 1) % count;
                    distance += Vector2.Distance(boundary[edge], boundary[next]);
                    points.Add(boundary[next]);
                    pointNormals.Add(facing[next] ? (normals[edge] + normals[next]).normalized : normals[edge]);
                    distances.Add(distance);
                    edge = next;
                    visited++;
                }
                runs.Add(new WallRun(points.ToArray(), pointNormals.ToArray(), distances.ToArray()));
            }

            runs.Sort((a, b) => b.AverageY.CompareTo(a.AverageY));
            return runs;
        }

        public static bool[] CastingEdges(IReadOnlyList<Vector2> loop, Vector2 cast)
        {
            int count = loop.Count;
            var casts = new bool[count];
            for (int i = 0; i < count; i++) casts[i] = Vector2.Dot(EdgeNormal(loop[i], loop[(i + 1) % count]), cast) > FacingEpsilon;
            return casts;
        }

        public static Vector2[] Translate(IReadOnlyList<Vector2> loop, Vector2 offset)
        {
            var moved = new Vector2[loop.Count];
            for (int i = 0; i < moved.Length; i++) moved[i] = loop[i] + offset;
            return moved;
        }

        public static void ClampInside(IReadOnlyList<Vector2> from, Vector2[] reach, IReadOnlyList<Vector2> polygon)
        {
            int count = polygon.Count;
            for (int i = 0; i < from.Count; i++)
            {
                float nearest = 1f;
                for (int j = 0; j < count; j++)
                {
                    if (SegmentHit(from[i], reach[i], polygon[j], polygon[(j + 1) % count], out float t)) nearest = Mathf.Min(nearest, t);
                }
                reach[i] = Vector2.Lerp(from[i], reach[i], nearest);
            }
        }

        public static float WallShade(Vector2 normal)
        {
            float facingLight = Vector2.Dot(normal, WallLight.normalized);
            return Mathf.Lerp(DarkestWall, 1f, Mathf.Clamp01(0.5f + 0.5f * facingLight));
        }

        public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
        {
            var remaining = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
            {
                remaining.Add(i);
            }

            var triangles = new List<int>(Mathf.Max(0, polygon.Count - 2) * 3);
            while (remaining.Count > 3 && ClipEar(polygon, remaining, triangles)) { }
            if (remaining.Count == 3) triangles.AddRange(remaining);
            return triangles;
        }

        public static float TriangleArea(IReadOnlyList<Vector2> points, IReadOnlyList<int> triangles)
        {
            float area = 0f;
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                area += Mathf.Abs(Cross(points[triangles[i + 1]] - points[triangles[i]], points[triangles[i + 2]] - points[triangles[i]])) * 0.5f;
            }
            return area;
        }
        #endregion

        #region [Private Methods]
        private static bool SegmentHit(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            t = 0f;
            Vector2 r = b - a;
            Vector2 s = d - c;
            float cross = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(cross) < CollinearEpsilon) return false;
            Vector2 offset = c - a;
            t = (offset.x * s.y - offset.y * s.x) / cross;
            float u = (offset.x * r.y - offset.y * r.x) / cross;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static Vector2 Tangent(IReadOnlyList<Vector2> points, IReadOnlyList<bool> sharp, bool closed, int index)
        {
            if (IsSharp(sharp, index)) return Vector2.zero;
            int count = points.Count;
            Vector2 previous = closed ? points[(index - 1 + count) % count] : points[Mathf.Max(0, index - 1)];
            Vector2 next = closed ? points[(index + 1) % count] : points[Mathf.Min(count - 1, index + 1)];
            return (next - previous) / 6f;
        }

        private static List<Vector2> ClipHalfPlane(IReadOnlyList<Vector2> polygon, float y, bool keepAbove)
        {
            var clipped = new List<Vector2>(polygon.Count + 2);
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                bool currentIn = keepAbove ? current.y >= y : current.y <= y;
                bool nextIn = keepAbove ? next.y >= y : next.y <= y;
                if (currentIn) clipped.Add(current);
                if (currentIn != nextIn)
                {
                    float t = (y - current.y) / (next.y - current.y);
                    clipped.Add(new Vector2(Mathf.Lerp(current.x, next.x, t), y));
                }
            }
            return clipped;
        }

        private static bool ClipEar(IReadOnlyList<Vector2> polygon, List<int> remaining, List<int> triangles)
        {
            int count = remaining.Count;
            for (int i = 0; i < count; i++)
            {
                int a = remaining[(i + count - 1) % count];
                int b = remaining[i];
                int c = remaining[(i + 1) % count];
                float cross = Cross(polygon[b] - polygon[a], polygon[c] - polygon[b]);
                if (Mathf.Abs(cross) < CollinearEpsilon)
                {
                    remaining.RemoveAt(i);
                    return true;
                }
                if (cross < 0f || ContainsOther(polygon, remaining, a, b, c)) continue;

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                remaining.RemoveAt(i);
                return true;
            }
            return false;
        }

        private static bool ContainsOther(IReadOnlyList<Vector2> polygon, List<int> remaining, int a, int b, int c)
        {
            foreach (int index in remaining)
            {
                if (index == a || index == b || index == c) continue;
                Vector2 p = polygon[index];
                if (Cross(polygon[b] - polygon[a], p - polygon[a]) >= 0f
                    && Cross(polygon[c] - polygon[b], p - polygon[b]) >= 0f
                    && Cross(polygon[a] - polygon[c], p - polygon[c]) >= 0f) return true;
            }
            return false;
        }

        private static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }
        #endregion
    }
}
