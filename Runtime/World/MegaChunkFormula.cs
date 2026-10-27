using System.Text.RegularExpressions;
using UnityEngine;

namespace MapLab
{
    public static class MegaChunkFormula
    {
        #region [Variables/Fields]
        public const int DefaultMegaPixels = 4096;
        public const int DefaultTilesPerSide = 64;
        public const string FolderPrefix = "M_";

        private static readonly Regex FolderPattern = new Regex(@"^M_(-?\d+)_(-?\d+)$");
        #endregion

        #region [Public Methods]
        public static float SnappedChunkSize(float chunkSize, int pixelsPerUnit)
        {
            return Mathf.Max(1f, Mathf.Round(chunkSize * pixelsPerUnit) / pixelsPerUnit);
        }

        public static int ChunkPixels(float chunkSize, int pixelsPerUnit)
        {
            return Mathf.RoundToInt(chunkSize * pixelsPerUnit);
        }

        public static int ChunksPerMega(int chunkPixels, int megaPixels)
        {
            return Mathf.Max(1, megaPixels / Mathf.Max(1, chunkPixels));
        }

        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0) return value;
            return value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
        }

        public static Vector2Int MegaOfChunk(Vector2Int chunk, int chunksPerMega)
        {
            return new Vector2Int(FloorDiv(chunk.x, chunksPerMega), FloorDiv(chunk.y, chunksPerMega));
        }

        public static RectInt ChunksOf(Vector2Int mega, int chunksPerMega)
        {
            return new RectInt(mega.x * chunksPerMega, mega.y * chunksPerMega, chunksPerMega, chunksPerMega);
        }

        public static Rect MegaRect(Vector2Int mega, float megaSize)
        {
            return new Rect(mega.x * megaSize, mega.y * megaSize, megaSize, megaSize);
        }

        public static Vector2Int MegaAt(Vector2 world, float megaSize)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / megaSize), Mathf.FloorToInt(world.y / megaSize));
        }

        public static Vector2Int CellAt(Vector2 world, Vector2Int mega, float megaSize, int tilesPerSide)
        {
            Vector2 local = (world - new Vector2(mega.x, mega.y) * megaSize) / megaSize * tilesPerSide;
            return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(local.x), 0, tilesPerSide - 1), Mathf.Clamp(Mathf.FloorToInt(local.y), 0, tilesPerSide - 1));
        }

        public static Vector2Int TileAt(Vector2 world, float tileSize)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / tileSize), Mathf.FloorToInt(world.y / tileSize));
        }

        public static Vector2Int MegaOfTile(Vector2Int tile, int tilesPerSide)
        {
            return new Vector2Int(FloorDiv(tile.x, tilesPerSide), FloorDiv(tile.y, tilesPerSide));
        }

        public static Vector2Int CellOfTile(Vector2Int tile, int tilesPerSide)
        {
            Vector2Int mega = MegaOfTile(tile, tilesPerSide);
            return tile - mega * tilesPerSide;
        }

        public static RectInt Union(RectInt a, RectInt b)
        {
            if (a.width <= 0 || a.height <= 0) return b;
            if (b.width <= 0 || b.height <= 0) return a;
            int xMin = Mathf.Min(a.xMin, b.xMin);
            int yMin = Mathf.Min(a.yMin, b.yMin);
            return new RectInt(xMin, yMin, Mathf.Max(a.xMax, b.xMax) - xMin, Mathf.Max(a.yMax, b.yMax) - yMin);
        }

        public static string FolderName(Vector2Int mega)
        {
            return $"{FolderPrefix}{mega.x}_{mega.y}";
        }

        public static bool TryParseFolder(string name, out Vector2Int mega)
        {
            mega = default;
            if (string.IsNullOrEmpty(name)) return false;
            Match match = FolderPattern.Match(name);
            if (!match.Success) return false;
            mega = new Vector2Int(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            return true;
        }
        #endregion
    }
}
