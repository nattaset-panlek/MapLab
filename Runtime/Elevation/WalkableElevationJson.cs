using System;

namespace MapLab.Elevation
{
    [Serializable]
    public class WalkableElevationJson
    {
        #region [Variables/Fields]
        public const int CurrentVersion = 1;
        public const byte Blocked = 255;
        public const int MaxLevels = 254;
        public const string WalkableFile = "Walkable.json";

        public int version = CurrentVersion;
        public string walkable = WalkableFile;
        public ElevationBounds bounds;
        public float cellSize;
        public int columns;
        public int rows;
        public float stepHeight;
        public ElevationLevel[] levels;
        public string cellLevel;
        public ElevationCliff[] cliffs;
        public ElevationRamp[] ramps;
        #endregion

        #region [Properties]
        public int CellCount => columns * rows;
        #endregion
    }
}
