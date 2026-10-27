using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabMegaThumbnails
    {
        #region [Variables/Fields]
        private readonly Dictionary<Vector2Int, (int Version, Texture2D Texture)> _cache = new Dictionary<Vector2Int, (int, Texture2D)>();
        private readonly Dictionary<Texture2D, Color> _averages = new Dictionary<Texture2D, Color>();
        private MapLabFloorPalette _palette;
        private int _paletteHash;
        private int _tilesPerSide;
        #endregion

        #region [Public Methods]
        public Texture2D Get(MapLabWorldData world, MapLabMegaChunk mega)
        {
            Validate(world);
            if (_cache.TryGetValue(mega.Index, out var entry) && entry.Version == mega.Version && entry.Texture != null) return entry.Texture;
            int size = world.TilesPerSide;
            Texture2D texture = entry.Texture;
            if (texture == null || texture.width != size)
            {
                if (texture != null) Object.DestroyImmediate(texture);
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            var pixels = new Color32[size * size];
            var colors = new Dictionary<byte, Color32>();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte id = mega.TileAt(x, y, size);
                    if (!colors.TryGetValue(id, out Color32 color))
                    {
                        color = TileColor(world.Palette, id);
                        colors[id] = color;
                    }
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            _cache[mega.Index] = (mega.Version, texture);
            return texture;
        }

        public Color TileColor(MapLabFloorPalette palette, byte id)
        {
            MapLabFloorTile tile = palette != null ? palette.Tile(id) : null;
            if (tile == null) return new Color(0f, 0f, 0f, 0f);
            Color color = tile.Color;
            if (tile.Texture == null) return color;
            if (!_averages.TryGetValue(tile.Texture, out Color average))
            {
                average = Average(tile.Texture);
                _averages[tile.Texture] = average;
            }
            return color * average;
        }

        public void Clear()
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.Texture != null) Object.DestroyImmediate(entry.Texture);
            }
            _cache.Clear();
            _averages.Clear();
        }
        #endregion

        #region [Private Methods]
        private void Validate(MapLabWorldData world)
        {
            int hash = PaletteHash(world.Palette);
            if (world.Palette == _palette && hash == _paletteHash && world.TilesPerSide == _tilesPerSide) return;
            Clear();
            _palette = world.Palette;
            _paletteHash = hash;
            _tilesPerSide = world.TilesPerSide;
        }

        private static int PaletteHash(MapLabFloorPalette palette)
        {
            if (palette == null) return 0;
            unchecked
            {
                int hash = palette.Count;
                foreach (var tile in palette.Tiles)
                {
                    hash = hash * 31 + tile.Color.GetHashCode();
                    hash = hash * 31 + (tile.Texture != null ? tile.Texture.GetHashCode() : 0);
                }
                return hash;
            }
        }

        private static Color Average(Texture2D texture)
        {
            var target = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var reader = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                reader.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                reader.Apply();
                Color sum = Color.clear;
                foreach (var pixel in reader.GetPixels()) sum += pixel;
                Color average = sum / 16f;
                average.a = 1f;
                return average;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(reader);
            }
        }
        #endregion
    }
}
