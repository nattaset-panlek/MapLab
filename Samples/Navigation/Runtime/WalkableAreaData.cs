using System;
using UnityEngine;

namespace MapLab.Samples.Navigation
{
    [Serializable]
    public class WalkableAreaData
    {
        #region [Variables/Fields]
        public int version;
        public Area bounds;
        public float cellSize;
        public int columns;
        public int rows;
        public string cells;
        public Vector2[] outline;
        public Polygon[] holes;

        [NonSerialized] private byte[] _bits;

        [Serializable]
        public struct Area
        {
            public float x;
            public float y;
            public float width;
            public float height;
        }

        [Serializable]
        public struct Polygon
        {
            public Vector2[] points;
        }
        #endregion

        #region [Properties]
        public Rect Bounds => new Rect(bounds.x, bounds.y, bounds.width, bounds.height);
        #endregion

        #region [Public Methods]
        public static WalkableAreaData Parse(string json)
        {
            return JsonUtility.FromJson<WalkableAreaData>(json);
        }

        public bool IsWalkable(int column, int row)
        {
            if (column < 0 || row < 0 || column >= columns || row >= rows) return false;
            _bits ??= Convert.FromBase64String(cells ?? string.Empty);
            int index = row * columns + column;
            return (index >> 3) < _bits.Length && (_bits[index >> 3] & (1 << (index & 7))) != 0;
        }

        public bool IsWalkable(Vector2 position)
        {
            return IsWalkable(Mathf.FloorToInt((position.x - bounds.x) / cellSize), Mathf.FloorToInt((position.y - bounds.y) / cellSize));
        }

        public Vector2[][] HoleOutlines()
        {
            var outlines = new Vector2[holes?.Length ?? 0][];
            for (int i = 0; i < outlines.Length; i++)
            {
                outlines[i] = holes[i].points;
            }
            return outlines;
        }
        #endregion
    }
}
