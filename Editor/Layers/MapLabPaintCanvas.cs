using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MapLab.Editor
{
    public readonly struct MapLabPaintCanvas
    {
        #region [Variables/Fields]
        private const int LabelScale = 2;
        private const int LabelInset = 4;
        private static readonly Color32 GridColor = new Color32(140, 200, 255, 220);
        private static readonly Regex LabelPattern = new Regex(@"MapLab Grid \(origin (-?\d+) (-?\d+), (\d+)x(\d+) chunks of ([0-9.]+) u, (\d+) px per u\)", RegexOptions.IgnoreCase);
        private static readonly int[] Glyphs =
        {
            0x7B6F, 0x2C97, 0x73E7, 0x73CF, 0x5BC9, 0x79CF, 0x79EF, 0x7249, 0x7BEF, 0x7BCF, 0x01C0, 0x0014
        };

        public readonly Vector2Int Origin;
        public readonly Vector2Int Count;
        public readonly float ChunkSize;
        public readonly int PixelsPerUnit;
        #endregion

        #region [Properties]
        public int ChunkPixels => Mathf.RoundToInt(ChunkSize * PixelsPerUnit);
        public int Width => Count.x * ChunkPixels;
        public int Height => Count.y * ChunkPixels;
        public Rect WorldRect => new Rect(Origin.x * ChunkSize, Origin.y * ChunkSize, Count.x * ChunkSize, Count.y * ChunkSize);
        public RectInt Range => new RectInt(Origin, Count);
        public string Label => string.Format(CultureInfo.InvariantCulture, "MapLab Grid (origin {0} {1}, {2}x{3} chunks of {4:0.###} u, {5} px per u)", Origin.x, Origin.y, Count.x, Count.y, ChunkSize, PixelsPerUnit);
        #endregion

        #region [Public Methods]
        public MapLabPaintCanvas(Vector2Int origin, Vector2Int count, float chunkSize, int pixelsPerUnit)
        {
            Origin = origin;
            Count = new Vector2Int(Mathf.Max(1, count.x), Mathf.Max(1, count.y));
            ChunkSize = chunkSize;
            PixelsPerUnit = pixelsPerUnit;
        }

        public static bool TryParse(string text, out MapLabPaintCanvas canvas)
        {
            canvas = default;
            if (string.IsNullOrEmpty(text)) return false;
            Match match = LabelPattern.Match(text);
            if (!match.Success) return false;
            int Int(int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
            canvas = new MapLabPaintCanvas(new Vector2Int(Int(1), Int(2)), new Vector2Int(Int(3), Int(4)), float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture), Int(6));
            return true;
        }

        public bool Contains(Vector2Int chunk)
        {
            return Range.Contains(chunk);
        }

        public bool Paste(byte[] pixels, Vector2Int chunk, Color32[] image, int imageSize)
        {
            int size = ChunkPixels;
            if (imageSize != size || !Contains(chunk)) return false;
            int left = (chunk.x - Origin.x) * size;
            int top = (Origin.y + Count.y - 1 - chunk.y) * size;
            for (int row = 0; row < size; row++)
            {
                int target = ((top + size - 1 - row) * Width + left) * 4;
                for (int x = 0; x < size; x++)
                {
                    Color32 color = image[row * size + x];
                    int index = target + x * 4;
                    pixels[index] = color.r;
                    pixels[index + 1] = color.g;
                    pixels[index + 2] = color.b;
                    pixels[index + 3] = color.a;
                }
            }
            return true;
        }

        public Color32[] Cut(byte[] pixels, Vector2Int chunk, float chunkSize, out bool empty)
        {
            int size = Mathf.RoundToInt(chunkSize * PixelsPerUnit);
            var image = new Color32[size * size];
            Rect world = WorldRect;
            int left = Mathf.RoundToInt((chunk.x * chunkSize - world.xMin) * PixelsPerUnit);
            int top = Mathf.RoundToInt((world.yMax - (chunk.y + 1) * chunkSize) * PixelsPerUnit);
            empty = true;
            for (int row = 0; row < size; row++)
            {
                int y = top + size - 1 - row;
                if (y < 0 || y >= Height) continue;
                for (int x = 0; x < size; x++)
                {
                    int column = left + x;
                    if (column < 0 || column >= Width) continue;
                    int index = (y * Width + column) * 4;
                    if (pixels[index + 3] == 0) continue;
                    image[row * size + x] = new Color32(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
                    empty = false;
                }
            }
            return image;
        }

        public static void Over(byte[] destination, int width, int height, byte[] source, RectInt rect, int opacity)
        {
            if (source == null) return;
            for (int y = 0; y < rect.height; y++)
            {
                int canvasY = rect.y + y;
                if (canvasY < 0 || canvasY >= height) continue;
                for (int x = 0; x < rect.width; x++)
                {
                    int canvasX = rect.x + x;
                    if (canvasX < 0 || canvasX >= width) continue;
                    int s = (y * rect.width + x) * 4;
                    int sourceAlpha = source[s + 3] * opacity / 255;
                    if (sourceAlpha == 0) continue;
                    int d = (canvasY * width + canvasX) * 4;
                    int destinationAlpha = destination[d + 3];
                    if (sourceAlpha == 255 || destinationAlpha == 0)
                    {
                        destination[d] = source[s];
                        destination[d + 1] = source[s + 1];
                        destination[d + 2] = source[s + 2];
                        destination[d + 3] = (byte)sourceAlpha;
                        continue;
                    }
                    int below = destinationAlpha * (255 - sourceAlpha) / 255;
                    int alpha = sourceAlpha + below;
                    for (int c = 0; c < 3; c++) destination[d + c] = (byte)((source[s + c] * sourceAlpha + destination[d + c] * below + alpha / 2) / alpha);
                    destination[d + 3] = (byte)alpha;
                }
            }
        }

        public byte[] DrawGrid()
        {
            var pixels = new byte[Width * Height * 4];
            int size = ChunkPixels;
            for (int column = 0; column <= Count.x; column++)
            {
                int x = Mathf.Min(column * size, Width - 1);
                for (int y = 0; y < Height; y++) Plot(pixels, x, y);
            }
            for (int row = 0; row <= Count.y; row++)
            {
                int y = Mathf.Min(row * size, Height - 1);
                for (int x = 0; x < Width; x++) Plot(pixels, x, y);
            }
            for (int j = 0; j < Count.y; j++)
            {
                for (int i = 0; i < Count.x; i++)
                {
                    var chunk = new Vector2Int(Origin.x + i, Origin.y + j);
                    int top = (Count.y - 1 - j) * size;
                    Text(pixels, $"{chunk.x},{chunk.y}", i * size + LabelInset, top + LabelInset);
                }
            }
            return pixels;
        }
        #endregion

        #region [Private Methods]
        private void Text(byte[] pixels, string text, int left, int top)
        {
            int x = left;
            foreach (char c in text)
            {
                int glyph = c >= '0' && c <= '9' ? Glyphs[c - '0'] : c == '-' ? Glyphs[10] : c == ',' ? Glyphs[11] : 0;
                for (int row = 0; row < 5; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        if ((glyph >> (14 - row * 3 - column) & 1) == 0) continue;
                        for (int dy = 0; dy < LabelScale; dy++)
                        {
                            for (int dx = 0; dx < LabelScale; dx++) Plot(pixels, x + column * LabelScale + dx, top + row * LabelScale + dy);
                        }
                    }
                }
                x += 4 * LabelScale;
            }
        }

        private void Plot(byte[] pixels, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int index = (y * Width + x) * 4;
            pixels[index] = GridColor.r;
            pixels[index + 1] = GridColor.g;
            pixels[index + 2] = GridColor.b;
            pixels[index + 3] = GridColor.a;
        }
        #endregion
    }
}
