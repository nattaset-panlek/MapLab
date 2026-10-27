using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Editor
{
    public enum MapLabGlyph
    {
        Select,
        Pan,
        Add,
        Remove,
        Paint,
        Fill,
        Erase,
        Pick,
        ZoomIn,
        ZoomOut,
        Fit,
        Frame,
        PopOut,
        MegaChunks,
        FloorTiles,
        Presets,
        Layers
    }

    public static class MapLabGlyphs
    {
        #region [Variables/Fields]
        private const int Scale = 2;
        private const int Size = 16 * Scale;
        private const float LineWidth = 1.3f;

        private static readonly Dictionary<MapLabGlyph, Texture2D> Cache = new Dictionary<MapLabGlyph, Texture2D>();

        private sealed class Shape
        {
            public readonly List<(Vector2[] Points, bool Closed, bool Dashed, float Width)> Lines = new List<(Vector2[], bool, bool, float)>();
            public readonly List<Vector2[]> Fills = new List<Vector2[]>();
            public readonly List<(Vector2 Center, float Radius)> Dots = new List<(Vector2, float)>();
            public Color? Tile;

            public Shape Line(bool closed, params float[] xy)
            {
                Lines.Add((Points(xy), closed, false, LineWidth));
                return this;
            }

            public Shape Thin(bool closed, params float[] xy)
            {
                Lines.Add((Points(xy), closed, false, 1f));
                return this;
            }

            public Shape Dashed(params float[] xy)
            {
                Lines.Add((Points(xy), true, true, LineWidth));
                return this;
            }

            public Shape Fill(params float[] xy)
            {
                Fills.Add(Points(xy));
                return this;
            }

            public Shape Dot(float x, float y, float radius)
            {
                Dots.Add((new Vector2(x, y), radius));
                return this;
            }

            public Shape Circle(float x, float y, float radius)
            {
                var points = new float[32];
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    points[i * 2] = x + Mathf.Cos(angle) * radius;
                    points[i * 2 + 1] = y + Mathf.Sin(angle) * radius;
                }
                return Line(true, points);
            }
        }
        #endregion

        #region [Public Methods]
        public static Texture2D Get(MapLabGlyph glyph)
        {
            if (Cache.TryGetValue(glyph, out Texture2D texture) && texture != null) return texture;
            texture = Rasterize(Define(glyph));
            Cache[glyph] = texture;
            return texture;
        }

        public static void Draw(Rect area, MapLabGlyph glyph, Color tint)
        {
            GUI.DrawTexture(area, Get(glyph), ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
        }

        public static bool IsTile(MapLabGlyph glyph)
        {
            return glyph >= MapLabGlyph.MegaChunks;
        }
        #endregion

        #region [Private Methods]
        private static Shape Define(MapLabGlyph glyph)
        {
            var shape = new Shape();
            switch (glyph)
            {
                case MapLabGlyph.Select:
                    return shape.Line(true, 4.5f, 2.5f, 4.5f, 12.5f, 7f, 10.2f, 8.8f, 13.8f, 10.5f, 13f, 8.7f, 9.5f, 12f, 9.5f);
                case MapLabGlyph.Pan:
                    return shape.Line(false, 8f, 2.5f, 8f, 13.5f).Line(false, 2.5f, 8f, 13.5f, 8f).Line(false, 6.2f, 4.3f, 8f, 2.5f, 9.8f, 4.3f)
                        .Line(false, 6.2f, 11.7f, 8f, 13.5f, 9.8f, 11.7f).Line(false, 4.3f, 6.2f, 2.5f, 8f, 4.3f, 9.8f).Line(false, 11.7f, 6.2f, 13.5f, 8f, 11.7f, 9.8f);
                case MapLabGlyph.Add:
                    return shape.Dashed(2.5f, 2.5f, 13.5f, 2.5f, 13.5f, 13.5f, 2.5f, 13.5f).Line(false, 8f, 5.5f, 8f, 10.5f).Line(false, 5.5f, 8f, 10.5f, 8f);
                case MapLabGlyph.Remove:
                    return shape.Line(true, 2.5f, 2.5f, 13.5f, 2.5f, 13.5f, 13.5f, 2.5f, 13.5f).Line(false, 5.5f, 8f, 10.5f, 8f);
                case MapLabGlyph.Paint:
                    return shape.Line(false, 7.2f, 9.2f, 12.8f, 3.4f).Line(true, 3f, 13.2f, 3.2f, 11.6f, 4.2f, 10.5f, 5.4f, 10.2f, 6.6f, 10.5f, 7.2f, 11.3f, 7f, 12.2f, 6f, 13f);
                case MapLabGlyph.Fill:
                    return shape.Line(true, 2.5f, 2.5f, 13.5f, 2.5f, 13.5f, 13.5f, 2.5f, 13.5f).Fill(4f, 12f, 12f, 4f, 12f, 12f);
                case MapLabGlyph.Erase:
                    return shape.Line(true, 3.6f, 10.6f, 9.4f, 4.8f, 12.6f, 8f, 7.6f, 13f, 5.4f, 13f, 3.6f, 11.2f).Line(false, 6.4f, 7.8f, 9.6f, 11f).Line(false, 8.5f, 13.2f, 13f, 13.2f);
                case MapLabGlyph.Pick:
                    return shape.Line(true, 10.4f, 2.9f, 13.1f, 5.6f, 11.5f, 7.2f, 8.8f, 4.5f).Line(false, 9.9f, 6.1f, 4.3f, 11.7f, 3.7f, 13.3f, 5.3f, 12.7f, 10.9f, 7.1f);
                case MapLabGlyph.ZoomIn:
                    return shape.Circle(7f, 7f, 4.3f).Line(false, 10.2f, 10.2f, 13.4f, 13.4f).Line(false, 7f, 5f, 7f, 9f).Line(false, 5f, 7f, 9f, 7f);
                case MapLabGlyph.ZoomOut:
                    return shape.Circle(7f, 7f, 4.3f).Line(false, 10.2f, 10.2f, 13.4f, 13.4f).Line(false, 5f, 7f, 9f, 7f);
                case MapLabGlyph.Fit:
                    return shape.Line(false, 2.5f, 6f, 2.5f, 2.5f, 6f, 2.5f).Line(false, 10f, 2.5f, 13.5f, 2.5f, 13.5f, 6f)
                        .Line(false, 13.5f, 10f, 13.5f, 13.5f, 10f, 13.5f).Line(false, 6f, 13.5f, 2.5f, 13.5f, 2.5f, 10f);
                case MapLabGlyph.Frame:
                    return shape.Line(false, 3.5f, 6.2f, 3.5f, 3.5f, 6.2f, 3.5f).Line(false, 9.8f, 3.5f, 12.5f, 3.5f, 12.5f, 6.2f)
                        .Line(false, 12.5f, 9.8f, 12.5f, 12.5f, 9.8f, 12.5f).Line(false, 6.2f, 12.5f, 3.5f, 12.5f, 3.5f, 9.8f).Dot(8f, 8f, 1.6f);
                case MapLabGlyph.PopOut:
                    return shape.Line(false, 7f, 3.5f, 3.5f, 3.5f, 3.5f, 12.5f, 12.5f, 12.5f, 12.5f, 9f).Line(false, 9.5f, 3f, 13f, 3f, 13f, 6.5f).Line(false, 13f, 3f, 8f, 8f);
                case MapLabGlyph.MegaChunks:
                    shape.Tile = new Color32(0x5B, 0x8D, 0xEF, 0xFF);
                    return shape.Line(true, 3.5f, 3.5f, 12.5f, 3.5f, 12.5f, 12.5f, 3.5f, 12.5f).Thin(false, 6.5f, 3.5f, 6.5f, 12.5f).Thin(false, 9.5f, 3.5f, 9.5f, 12.5f)
                        .Thin(false, 3.5f, 6.5f, 12.5f, 6.5f).Thin(false, 3.5f, 9.5f, 12.5f, 9.5f);
                case MapLabGlyph.FloorTiles:
                    shape.Tile = new Color32(0x4F, 0xB3, 0xA9, 0xFF);
                    return shape.Fill(3.5f, 3.5f, 7.8f, 3.5f, 7.8f, 7.8f, 3.5f, 7.8f).Fill(8.2f, 8.2f, 12.5f, 8.2f, 12.5f, 12.5f, 8.2f, 12.5f)
                        .Thin(true, 8.2f, 3.5f, 12.5f, 3.5f, 12.5f, 7.8f, 8.2f, 7.8f).Thin(true, 3.5f, 8.2f, 7.8f, 8.2f, 7.8f, 12.5f, 3.5f, 12.5f);
                case MapLabGlyph.Presets:
                    shape.Tile = new Color32(0x7B, 0x7F, 0xE6, 0xFF);
                    return shape.Line(true, 5f, 3.5f, 11f, 3.5f, 11f, 12.5f, 8f, 10.4f, 5f, 12.5f);
                default:
                    shape.Tile = new Color32(0x9C, 0x7B, 0xE0, 0xFF);
                    return shape.Line(true, 8f, 3.6f, 12.8f, 6.1f, 8f, 8.6f, 3.2f, 6.1f).Line(false, 3.2f, 9.2f, 8f, 11.7f, 12.8f, 9.2f);
            }
        }

        private static Texture2D Rasterize(Shape shape)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var segments = new List<(Vector2 A, Vector2 B, float Width)>();
            foreach (var line in shape.Lines) AddSegments(segments, line.Points, line.Closed, line.Dashed, line.Width);
            var pixels = new Color[Size * Size];
            for (int py = 0; py < Size; py++)
            {
                for (int px = 0; px < Size; px++)
                {
                    var point = new Vector2((px + 0.5f) / Scale, (py + 0.5f) / Scale);
                    float ink = 0f;
                    foreach (var segment in segments) ink = Mathf.Max(ink, Mathf.Clamp01((segment.Width * 0.5f - Distance(point, segment.A, segment.B)) * Scale + 0.5f));
                    foreach (var fill in shape.Fills)
                    {
                        if (Inside(point, fill)) ink = Mathf.Max(ink, shape.Tile.HasValue ? 1f : 0.75f);
                    }
                    foreach (var dot in shape.Dots) ink = Mathf.Max(ink, Mathf.Clamp01((dot.Radius - Vector2.Distance(point, dot.Center)) * Scale + 0.5f));
                    Color color;
                    if (shape.Tile.HasValue)
                    {
                        float box = Mathf.Clamp01((4f - RoundedBox(point, 8f, 4f)) * Scale + 0.5f);
                        color = Color.Lerp(shape.Tile.Value, Color.white, ink);
                        color.a = box;
                    }
                    else
                    {
                        color = new Color(1f, 1f, 1f, ink);
                    }
                    pixels[(Size - 1 - py) * Size + px] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void AddSegments(List<(Vector2, Vector2, float)> segments, Vector2[] points, bool closed, bool dashed, float width)
        {
            int count = closed ? points.Length : points.Length - 1;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Length];
                if (!dashed)
                {
                    segments.Add((a, b, width));
                    continue;
                }
                float length = Vector2.Distance(a, b);
                for (float t = 0f; t < length; t += 4f) segments.Add((Vector2.Lerp(a, b, t / length), Vector2.Lerp(a, b, Mathf.Min(length, t + 2.2f) / length), width));
            }
        }

        private static float Distance(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(point, a + ab * t);
        }

        private static bool Inside(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) && point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside;
            }
            return inside;
        }

        private static float RoundedBox(Vector2 point, float half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(point.x - half), Mathf.Abs(point.y - half)) - Vector2.one * (half - radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static Vector2[] Points(float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }
        #endregion
    }
}
