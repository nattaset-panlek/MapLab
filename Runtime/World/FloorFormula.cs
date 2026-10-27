using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    public static class FloorFormula
    {
        #region [Public Methods]
        public static List<RectInt> Rectangles(Func<int, int, byte> tileAt, int size, byte tile)
        {
            var rectangles = new List<RectInt>();
            var used = new bool[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (used[y * size + x] || tileAt(x, y) != tile) continue;
                    int width = 1;
                    while (x + width < size && !used[y * size + x + width] && tileAt(x + width, y) == tile) width++;
                    int height = 1;
                    while (y + height < size && RowMatches(tileAt, used, size, x, y + height, width, tile)) height++;
                    for (int row = y; row < y + height; row++)
                    {
                        for (int column = x; column < x + width; column++) used[row * size + column] = true;
                    }
                    rectangles.Add(new RectInt(x, y, width, height));
                }
            }
            return rectangles;
        }

        public static HashSet<byte> TilesUsed(Func<int, int, byte> tileAt, int size)
        {
            var tiles = new HashSet<byte>();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte tile = tileAt(x, y);
                    if (tile != 0) tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static List<Vector2Int> Brush(Vector2Int center, int size)
        {
            int side = Mathf.Max(1, size);
            int start = -(side - 1) / 2;
            var tiles = new List<Vector2Int>(side * side);
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++) tiles.Add(center + new Vector2Int(start + x, start + y));
            }
            return tiles;
        }
        #endregion

        #region [Private Methods]
        private static bool RowMatches(Func<int, int, byte> tileAt, bool[] used, int size, int x, int y, int width, byte tile)
        {
            for (int column = x; column < x + width; column++)
            {
                if (used[y * size + column] || tileAt(column, y) != tile) return false;
            }
            return true;
        }
        #endregion
    }
}
