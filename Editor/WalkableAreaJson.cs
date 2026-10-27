using System;
using UnityEngine;

namespace MapLab.Editor
{
    [Serializable]
    public class WalkableAreaJson
    {
        #region [Variables/Fields]
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public WalkableBounds bounds;
        public float cellSize;
        public int columns;
        public int rows;
        public string cells;
        public Vector2[] outline;
        public WalkablePolygon[] holes;
        #endregion

        #region [Properties]
        public Rect Rect => new Rect(bounds.x, bounds.y, bounds.width, bounds.height);
        public int CellCount => columns * rows;
        #endregion
    }
}
