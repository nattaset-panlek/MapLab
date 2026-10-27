using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabPieceOwners
    {
        #region [Variables/Fields]
        public const byte OpaqueAlpha = 128;

        public readonly struct Triangle
        {
            public readonly Vector2 A;
            public readonly Vector2 B;
            public readonly Vector2 C;
            public readonly bool Opaque;

            public Triangle(Vector2 a, Vector2 b, Vector2 c, bool opaque)
            {
                A = a;
                B = b;
                C = c;
                Opaque = opaque;
            }
        }

        public readonly struct SortKey
        {
            public readonly int LayerValue;
            public readonly int Order;
            public readonly float Depth;

            public SortKey(int layerValue, int order, float depth)
            {
                LayerValue = layerValue;
                Order = order;
                Depth = depth;
            }
        }
        #endregion

        #region [Public Methods]
        public static List<int> DrawOrder(IReadOnlyList<SortKey> keys)
        {
            return Enumerable.Range(0, keys.Count)
                .OrderBy(i => keys[i].LayerValue)
                .ThenBy(i => keys[i].Order)
                .ThenByDescending(i => keys[i].Depth)
                .ThenBy(i => i)
                .ToList();
        }

        public static int[] Owners(MapLabPaintCanvas canvas, IReadOnlyList<List<Triangle>> pieces, IReadOnlyList<int> drawOrder, byte[] composite)
        {
            int width = canvas.Width;
            int height = canvas.Height;
            var opaque = new int[width * height];
            var any = new int[width * height];
            foreach (int index in drawOrder)
            {
                foreach (var triangle in pieces[index])
                {
                    Rasterize(canvas, triangle, any, index + 1);
                    if (triangle.Opaque) Rasterize(canvas, triangle, opaque, index + 1);
                }
            }
            var owners = new int[width * height];
            for (int i = 0; i < owners.Length; i++)
            {
                owners[i] = opaque[i] != 0 ? opaque[i] : composite[i * 4 + 3] > 0 ? any[i] : 0;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (owners[i] != 0 || composite[i * 4 + 3] == 0) continue;
                    if (x > 0 && owners[i - 1] != 0) owners[i] = owners[i - 1];
                    else if (y > 0 && owners[i - width] != 0) owners[i] = owners[i - width];
                    else if (x < width - 1 && opaque[i + 1] != 0) owners[i] = opaque[i + 1];
                    else if (y < height - 1 && opaque[i + width] != 0) owners[i] = opaque[i + width];
                }
            }
            return owners;
        }

        public static RectInt[] PieceRects(int[] owners, int width, int height, int pieceCount)
        {
            var xMin = Enumerable.Repeat(int.MaxValue, pieceCount + 1).ToArray();
            var yMin = Enumerable.Repeat(int.MaxValue, pieceCount + 1).ToArray();
            var xMax = Enumerable.Repeat(-1, pieceCount + 1).ToArray();
            var yMax = Enumerable.Repeat(-1, pieceCount + 1).ToArray();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = owners[y * width + x];
                    if (id == 0) continue;
                    if (x < xMin[id]) xMin[id] = x;
                    if (x > xMax[id]) xMax[id] = x;
                    if (y < yMin[id]) yMin[id] = y;
                    if (y > yMax[id]) yMax[id] = y;
                }
            }
            var rects = new RectInt[pieceCount];
            for (int index = 0; index < pieceCount; index++)
            {
                int id = index + 1;
                if (xMax[id] < 0)
                {
                    rects[index] = new RectInt(0, 0, 0, 0);
                    continue;
                }
                int left = Mathf.Max(0, xMin[id] - 1);
                int top = Mathf.Max(0, yMin[id] - 1);
                int right = Mathf.Min(width - 1, xMax[id] + 1);
                int bottom = Mathf.Min(height - 1, yMax[id] + 1);
                rects[index] = new RectInt(left, top, right - left + 1, bottom - top + 1);
            }
            return rects;
        }

        public static byte[] Cut(int[] owners, byte[] source, int width, RectInt rect, int id, out bool empty)
        {
            int height = owners.Length / width;
            var image = new byte[rect.width * rect.height * 4];
            empty = true;
            for (int y = 0; y < rect.height; y++)
            {
                int canvasY = rect.y + y;
                for (int x = 0; x < rect.width; x++)
                {
                    int canvasX = rect.x + x;
                    int i = canvasY * width + canvasX;
                    int owner = owners[i];
                    if (owner != id && (owner == 0 || !Touches(owners, width, height, canvasX, canvasY, id))) continue;
                    int s = i * 4;
                    if (source[s + 3] == 0) continue;
                    int d = (y * rect.width + x) * 4;
                    image[d] = source[s];
                    image[d + 1] = source[s + 1];
                    image[d + 2] = source[s + 2];
                    image[d + 3] = source[s + 3];
                    empty = false;
                }
            }
            return image;
        }

        public static byte[] EncodeOwners(int[] owners)
        {
            var pixels = new byte[owners.Length * 4];
            for (int i = 0; i < owners.Length; i++)
            {
                pixels[i * 4] = (byte)(owners[i] & 0xFF);
                pixels[i * 4 + 1] = (byte)(owners[i] >> 8 & 0xFF);
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }

        public static int[] DecodeOwners(byte[] pixels)
        {
            var owners = new int[pixels.Length / 4];
            for (int i = 0; i < owners.Length; i++) owners[i] = pixels[i * 4] | pixels[i * 4 + 1] << 8;
            return owners;
        }
        #endregion

        #region [Private Methods]
        private static bool Touches(int[] owners, int width, int height, int x, int y, int id)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    if ((dx != 0 || dy != 0) && nx >= 0 && nx < width && owners[ny * width + nx] == id) return true;
                }
            }
            return false;
        }

        private static void Rasterize(MapLabPaintCanvas canvas, Triangle triangle, int[] map, int id)
        {
            Rect world = canvas.WorldRect;
            int ppu = canvas.PixelsPerUnit;
            Vector2 a = ToCanvas(triangle.A, world, ppu);
            Vector2 b = ToCanvas(triangle.B, world, ppu);
            Vector2 c = ToCanvas(triangle.C, world, ppu);
            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-6f) return;
            if (area < 0f)
            {
                Vector2 swap = b;
                b = c;
                c = swap;
            }
            int width = canvas.Width;
            int height = canvas.Height;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int xMax = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int yMax = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (Edge(a, b, p) >= 0f && Edge(b, c, p) >= 0f && Edge(c, a, p) >= 0f) map[y * width + x] = id;
                }
            }
        }

        private static Vector2 ToCanvas(Vector2 point, Rect world, int ppu)
        {
            return new Vector2((point.x - world.xMin) * ppu, (world.yMax - point.y) * ppu);
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 p)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }
        #endregion
    }
}
