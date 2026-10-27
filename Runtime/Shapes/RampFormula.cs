using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Shapes
{
    public static class RampFormula
    {
        #region [Variables/Fields]
        public const float MaxAngle = 60f;
        private const float MinSpan = 0.0001f;
        #endregion

        #region [Public Methods]
        public static float Perimeter(IReadOnlyList<Vector2> outline)
        {
            float length = 0f;
            for (int i = 0; i < outline.Count; i++) length += Vector2.Distance(outline[i], outline[(i + 1) % outline.Count]);
            return length;
        }

        public static Vector2 PointAt(IReadOnlyList<Vector2> outline, float distance)
        {
            float perimeter = Perimeter(outline);
            if (perimeter < MinSpan) return outline.Count > 0 ? outline[0] : Vector2.zero;
            distance = Mathf.Repeat(distance, perimeter);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 from = outline[i];
                Vector2 to = outline[(i + 1) % outline.Count];
                float span = Vector2.Distance(from, to);
                if (distance <= span) return span < MinSpan ? from : Vector2.Lerp(from, to, distance / span);
                distance -= span;
            }
            return outline[0];
        }

        public static RampCorners Corners(IReadOnlyList<Vector2> outline, float position, float width, float length, float angle)
        {
            float perimeter = Perimeter(outline);
            float center = Mathf.Repeat(position, 1f) * perimeter;
            float half = Mathf.Min(width * 0.5f, perimeter * 0.25f);
            Vector2 left = PointAt(outline, center - half);
            Vector2 right = PointAt(outline, center + half);
            Vector2 tangent = right - left;
            tangent = tangent.sqrMagnitude < MinSpan ? Vector2.right : tangent.normalized;
            Vector2 outward = new Vector2(tangent.y, -tangent.x);
            Vector2 middle = (left + right) * 0.5f;
            Vector2 topLeft = middle - tangent * (width * 0.5f);
            Vector2 topRight = middle + tangent * (width * 0.5f);
            Vector2 direction = Rotate(outward, Mathf.Clamp(angle, -MaxAngle, MaxAngle));
            Vector2 run = direction * Mathf.Max(MinSpan, length);
            return new RampCorners(topLeft, topRight, topRight + run, topLeft + run, direction, outward);
        }

        public static float Project(IReadOnlyList<Vector2> outline, Vector2 point)
        {
            float perimeter = Perimeter(outline);
            if (perimeter < MinSpan) return 0f;
            float best = float.MaxValue;
            float bestDistance = 0f;
            float walked = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 from = outline[i];
                Vector2 to = outline[(i + 1) % outline.Count];
                Vector2 span = to - from;
                float spanLength = span.magnitude;
                float t = spanLength < MinSpan ? 0f : Mathf.Clamp01(Vector2.Dot(point - from, span) / (spanLength * spanLength));
                float gap = (from + span * t - point).sqrMagnitude;
                if (gap < best)
                {
                    best = gap;
                    bestDistance = walked + spanLength * t;
                }
                walked += spanLength;
            }
            return bestDistance / perimeter;
        }

        public static Vector2 Parameters(RampCorners corners, Vector2 point)
        {
            Vector2 across = corners.TopRight - corners.TopLeft;
            Vector2 down = corners.BottomLeft - corners.TopLeft;
            float determinant = across.x * down.y - across.y * down.x;
            if (Mathf.Abs(determinant) < MinSpan) return Vector2.zero;
            Vector2 offset = point - corners.TopLeft;
            float u = (offset.x * down.y - offset.y * down.x) / determinant;
            float v = (across.x * offset.y - across.y * offset.x) / determinant;
            return new Vector2(u, v);
        }

        public static float Height(RampCorners corners, float low, float high, Vector2 point)
        {
            return Mathf.Lerp(high, low, Mathf.Clamp01(Parameters(corners, point).y));
        }

        public static float Height(RampCorners corners, float low, float high, int steps, Vector2 point)
        {
            if (steps < 2) return Height(corners, low, high, point);
            int step = Mathf.Clamp(Mathf.FloorToInt(Parameters(corners, point).y * steps), 0, steps - 1);
            return Mathf.Lerp(high, low, step / (float)steps);
        }

        public static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }

        public static float SlopeDegrees(float rise, float length)
        {
            return Mathf.Atan2(Mathf.Max(0f, rise), Mathf.Max(MinSpan, length)) * Mathf.Rad2Deg;
        }
        #endregion
    }
}
