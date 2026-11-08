using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace MapLab.Editor
{
    public static class MapLabArtPainter
    {
        #region [Variables/Fields]
        public const string Grass = "MapLab_GrassFill";
        public const string Dirt = "MapLab_DirtFill";
        public const string Cobble = "MapLab_CobbleFill";
        public const string PathEdge = "MapLab_PathEdge";
        public const string Moss = "MapLab_MossPatch";
        public const string Boulder = "MapLab_Boulder";
        public const string GrassTuft1 = "MapLab_GrassTuft_1";
        public const string GrassTuft2 = "MapLab_GrassTuft_2";
        public const string GrassTuft3 = "MapLab_GrassTuft_3";
        public const string TreeGreen1 = "MapLab_Tree_Green_1";
        public const string TreeGreen2 = "MapLab_Tree_Green_2";
        public const string TreeYellow1 = "MapLab_Tree_Yellow_1";
        public const string TreeYellow2 = "MapLab_Tree_Yellow_2";
        public static readonly string[] Trees = { TreeGreen1, TreeYellow1, TreeGreen2, TreeYellow2 };
        public static readonly string[] GrassTufts = { GrassTuft1, GrassTuft2, GrassTuft3 };
        public const string SoftShadow = "MapLab_SoftShadow";
        public const string FogPuff = "MapLab_FogPuff";
        public const string Dust = "MapLab_Dust";
        public const string WallStone = "MapLab_WallStone";
        public const string TopGrass = "MapLab_TopGrass";
        public const string TopStone = "MapLab_TopStone";
        public const float PixelsPerUnit = 128f;

        private static readonly Color GrassBase = new Color(0.56f, 0.8f, 0.42f);
        private static readonly Color GrassDark = new Color(0.47f, 0.71f, 0.36f);
        private static readonly Color GrassLight = new Color(0.66f, 0.87f, 0.5f);
        private static readonly Color DirtBase = new Color(0.89f, 0.77f, 0.56f);
        private static readonly Color DirtBorder = new Color(0.76f, 0.62f, 0.43f);
        private static readonly Color Pebble = new Color(0.8f, 0.68f, 0.48f);
        private static readonly Color[] CobbleTones = { new Color(0.82f, 0.81f, 0.87f), new Color(0.88f, 0.87f, 0.92f), new Color(0.77f, 0.76f, 0.83f) };
        private static readonly Color Mortar = new Color(0.62f, 0.6f, 0.69f);
        private static readonly Color StoneLight = new Color(0.9f, 0.88f, 0.94f);
        private static readonly Color StoneMid = new Color(0.78f, 0.76f, 0.85f);
        private static readonly Color StoneDark = new Color(0.64f, 0.62f, 0.73f);
        private static readonly Color StoneOutline = new Color(0.45f, 0.43f, 0.54f);
        private static readonly Vector2 LightFrom = new Vector2(-0.6f, 0.8f);

        private static readonly Dictionary<string, ArtSpec> Specs = new Dictionary<string, ArtSpec>
        {
            { Grass, new ArtSpec(512, 512, new Vector2(0.5f, 0.5f), true, PaintGrass) },
            { Dirt, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), true, PaintDirtFill) },
            { Cobble, new ArtSpec(512, 512, new Vector2(0.5f, 0.5f), true, PaintCobble) },
            { PathEdge, new ArtSpec(256, 192, new Vector2(0.5f, 0.5f), true, PaintPathEdge) },
            { Moss, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), false, PaintMoss) },
            { Boulder, new ArtSpec(160, 112, new Vector2(0.5f, 10f / 112f), false, PaintBoulder) },
            { SoftShadow, new ArtSpec(128, 128, new Vector2(0.5f, 0.5f), false, PaintShadow) },
            { FogPuff, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), false, () => Radial(256, 1.8f)) },
            { Dust, new ArtSpec(32, 32, new Vector2(0.5f, 0.5f), false, () => Radial(32, 2.2f)) },
            { WallStone, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), true, PaintWallStone) },
            { TopGrass, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), true, PaintTopGrass) },
            { TopStone, new ArtSpec(256, 256, new Vector2(0.5f, 0.5f), true, PaintTopStone) }
        };

        private readonly struct ArtSpec
        {
            public readonly int Width;
            public readonly int Height;
            public readonly Vector2 Pivot;
            public readonly bool Repeat;
            public readonly Func<Color[]> Paint;

            public ArtSpec(int width, int height, Vector2 pivot, bool repeat, Func<Color[]> paint)
            {
                Width = width;
                Height = height;
                Pivot = pivot;
                Repeat = repeat;
                Paint = paint;
            }
        }
        #endregion

        #region [Public Methods]
        public static Sprite Get(string folder, string name)
        {
            if (Trees.Contains(name) || GrassTufts.Contains(name)) return GetStock(name);

            if (!Specs.TryGetValue(name, out var spec)) throw new ArgumentException($"Unknown MapLab art '{name}'.");

            string path = $"{folder}/{name}.png";
            if (!File.Exists(path))
            {
                WritePng(path, spec);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Configure(path, spec);
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException($"MapLab art '{path}' did not import as a sprite.");
            return sprite;
        }
        #endregion

        #region [Private Methods]
        private static Sprite GetStock(string name)
        {
            string path = $"{MapLabSceneBuilder.ArtFolder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"MapLab art '{path}' is missing.");

            if (importer.textureType != TextureImporterType.Sprite || !Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit) || importer.spritePivot != new Vector2(0.5f, 0f))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.alphaIsTransparency = true;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0.5f, 0f);
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void WritePng(string path, ArtSpec spec)
        {
            var texture = new Texture2D(spec.Width, spec.Height, TextureFormat.RGBA32, false);
            texture.SetPixels(spec.Paint());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Configure(string path, ArtSpec spec)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = spec.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = spec.Pivot;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static Color[] PaintGrass()
        {
            const int size = 512;
            Color[] buffer = Solid(size, size, GrassBase);
            var rng = new Random(11);
            for (int i = 0; i < 240; i++)
            {
                int x = rng.Next(size), y = rng.Next(size);
                Color blade = rng.NextDouble() < 0.6 ? GrassDark : GrassLight;
                Line(buffer, size, size, x - 4, y, 7, 115f, blade);
                Line(buffer, size, size, x, y, 10, 90f, blade);
                Line(buffer, size, size, x + 4, y, 7, 65f, blade);
            }
            return buffer;
        }

        private static Color[] PaintDirtFill()
        {
            const int size = 256;
            Color[] buffer = Solid(size, size, DirtBase);
            var rng = new Random(31);
            for (int i = 0; i < 40; i++)
            {
                Disc(buffer, size, size, rng.Next(size), rng.Next(size), 2.5f + (float)rng.NextDouble() * 2.5f, Pebble, true);
            }
            return buffer;
        }

        private static Color[] PaintCobble()
        {
            const int size = 512;
            const int cells = 8;
            const int seed = 41;
            float cellSize = size / (float)cells;
            var buffer = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x / cellSize, py = y / cellSize;
                    int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
                    float nearest = float.MaxValue, second = float.MaxValue;
                    int bestX = 0, bestY = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int cx = ix + ox, cy = iy + oy;
                            int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                            float dx = (px - (cx + 0.15f + 0.7f * Hash(wx, wy, seed))) * 0.85f;
                            float dy = py - (cy + 0.15f + 0.7f * Hash(wx, wy, seed + 1));
                            float distance = Mathf.Sqrt(dx * dx + dy * dy);
                            if (distance < nearest)
                            {
                                second = nearest;
                                nearest = distance;
                                bestX = wx;
                                bestY = wy;
                            }
                            else if (distance < second)
                            {
                                second = distance;
                            }
                        }
                    }

                    float edge = (second - nearest) * cellSize;
                    Color stone = CobbleTones[(int)(Hash(bestX, bestY, seed + 3) * CobbleTones.Length) % CobbleTones.Length];
                    buffer[y * size + x] = Color.Lerp(Mortar, stone, Mathf.Clamp01(edge - 3f));
                }
            }
            return buffer;
        }

        private static Color[] PaintPathEdge()
        {
            const int width = 256, height = 192;
            const float border = 12f;
            float low = 0.14f * (height - 1), high = 0.86f * (height - 1);
            var buffer = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float inside = Mathf.Min(y - low, high - y);
                Color color = Color.Lerp(DirtBorder, DirtBase, Mathf.Clamp01(inside - border + 0.5f));
                color.a = Mathf.Clamp01(inside + 0.5f);
                for (int x = 0; x < width; x++)
                {
                    buffer[y * width + x] = color;
                }
            }

            var rng = new Random(59);
            for (int i = 0; i < 24; i++)
            {
                int y = (int)(low + border + 6f + rng.NextDouble() * (high - low - 2f * border - 12f));
                Disc(buffer, width, height, rng.Next(width), y, 2f + (float)rng.NextDouble() * 2f, Pebble, true);
            }
            return buffer;
        }

        private static Color[] PaintMoss()
        {
            const int size = 256;
            var buffer = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float angle = Mathf.Atan2(dy, dx);
                    float radius = half * (0.8f + 0.1f * Mathf.Sin(angle * 3f + 1f) + 0.06f * Mathf.Sin(angle * 5f + 2f));
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    buffer[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            return buffer;
        }

        private static Color[] PaintBoulder()
        {
            const int width = 160, height = 112;
            const int sectors = 9;
            const int seed = 97;
            var buffer = new Color[width * height];
            var center = new Vector2(80f, 46f);
            var light = LightFrom.normalized;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float scaleY = y + 0.5f > center.y ? 56f : 38f;
                    float dx = (x + 0.5f - center.x) / 72f;
                    float dy = (y + 0.5f - center.y) / scaleY;
                    float angle = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f) * sectors;
                    int sector = Mathf.FloorToInt(angle);
                    float radius = Mathf.Lerp(0.84f + 0.16f * Hash(sector, 0, seed), 0.84f + 0.16f * Hash((sector + 1) % sectors, 0, seed), angle - sector);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    float edge = (1f - distance) * scaleY;
                    if (edge <= -0.5f || y < 6)
                    {
                        buffer[y * width + x] = Color.clear;
                        continue;
                    }

                    float facing = Vector2.Dot(new Vector2(dx, dy).normalized, light);
                    Color color = dy > 0.15f && distance < 0.62f ? StoneLight : facing > 0.35f ? StoneLight : facing < -0.3f ? StoneDark : StoneMid;
                    if (edge < 2.5f) color = StoneOutline;
                    color.a = Mathf.Clamp01(edge + 0.5f);
                    buffer[y * width + x] = color;
                }
            }
            return buffer;
        }

        private static Color[] PaintShadow()
        {
            const int size = 128;
            var buffer = new Color[size * size];
            Disc(buffer, size, size, size / 2, size / 2, size * 0.5f - 2f, Color.white, false);
            return buffer;
        }

        private static Color[] PaintWallStone()
        {
            const int size = 256;
            const int rowHeight = 64;
            const int brickWidth = 128;
            const int mortar = 5;
            const int bevel = 6;
            const int seed = 131;
            var tones = new[] { 1f, 0.94f, 0.88f, 0.91f };
            var buffer = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                int row = y / rowHeight;
                int localY = y % rowHeight;
                for (int x = 0; x < size; x++)
                {
                    int shifted = (x + (row % 2 == 1 ? brickWidth / 2 : 0)) % size;
                    int brick = shifted / brickWidth;
                    int localX = shifted % brickWidth;
                    float tone;
                    if (localX < mortar || localY < mortar)
                    {
                        tone = 0.6f;
                    }
                    else
                    {
                        tone = tones[(int)(Hash(brick, row, seed) * tones.Length) % tones.Length];
                        if (localY >= rowHeight - bevel) tone = Mathf.Min(1f, tone + 0.07f);
                        else if (localY < mortar + bevel) tone -= 0.09f;
                    }
                    buffer[y * size + x] = new Color(tone, tone, tone, 1f);
                }
            }
            return buffer;
        }

        private static Color[] PaintTopGrass()
        {
            const int size = 256;
            Color[] buffer = Solid(size, size, new Color(0.94f, 0.94f, 0.94f));
            var dark = new Color(0.82f, 0.82f, 0.82f);
            var rng = new Random(23);
            for (int i = 0; i < 70; i++)
            {
                int x = rng.Next(size), y = rng.Next(size);
                Color blade = rng.NextDouble() < 0.6 ? dark : Color.white;
                Line(buffer, size, size, x - 4, y, 6, 115f, blade);
                Line(buffer, size, size, x, y, 9, 90f, blade);
                Line(buffer, size, size, x + 4, y, 6, 65f, blade);
            }
            return buffer;
        }

        private static Color[] PaintTopStone()
        {
            const int size = 256;
            const int slab = 64;
            const int mortar = 3;
            const int seed = 151;
            var tones = new[] { 1f, 0.95f, 0.9f, 0.97f };
            var buffer = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                int row = y / slab;
                int localY = y % slab;
                for (int x = 0; x < size; x++)
                {
                    int shifted = (x + (row % 2 == 1 ? slab / 2 : 0)) % size;
                    float tone = shifted % slab < mortar || localY < mortar
                        ? 0.74f
                        : tones[(int)(Hash(shifted / slab, row, seed) * tones.Length) % tones.Length];
                    buffer[y * size + x] = new Color(tone, tone, tone, 1f);
                }
            }
            return buffer;
        }

        private static Color[] Radial(int size, float power)
        {
            var buffer = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    buffer[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - r), power));
                }
            }
            return buffer;
        }

        private static Color[] Solid(int width, int height, Color color)
        {
            var buffer = new Color[width * height];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = color;
            return buffer;
        }

        private static void Line(Color[] buffer, int width, int height, int x0, int y0, int length, float angleDegrees, Color color)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            float dx = Mathf.Cos(radians), dy = Mathf.Sin(radians);
            for (int i = 0; i <= length * 2; i++)
            {
                float t = i * 0.5f;
                int x = Wrap(Mathf.RoundToInt(x0 + dx * t), width);
                int y = Wrap(Mathf.RoundToInt(y0 + dy * t), height);
                buffer[y * width + x] = color;
                buffer[y * width + Wrap(x + 1, width)] = color;
            }
        }

        private static void Disc(Color[] buffer, int width, int height, int cx, int cy, float radius, Color color, bool wrap)
        {
            int reach = Mathf.CeilToInt(radius) + 1;
            for (int oy = -reach; oy <= reach; oy++)
            {
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int x = cx + ox, y = cy + oy;
                    if (wrap)
                    {
                        x = Wrap(x, width);
                        y = Wrap(y, height);
                    }
                    else if (x < 0 || y < 0 || x >= width || y >= height)
                    {
                        continue;
                    }

                    float coverage = Mathf.Clamp01(radius - Mathf.Sqrt(ox * ox + oy * oy) + 0.5f);
                    if (coverage <= 0f) continue;
                    int index = y * width + x;
                    Color under = buffer[index];
                    Color over = Color.Lerp(under, color, under.a > 0f ? coverage : 1f);
                    over.a = Mathf.Max(under.a, coverage * color.a);
                    buffer[index] = over;
                }
            }
        }

        private static int Wrap(int value, int period)
        {
            return period > 0 ? ((value % period) + period) % period : value;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }
        #endregion
    }
}
