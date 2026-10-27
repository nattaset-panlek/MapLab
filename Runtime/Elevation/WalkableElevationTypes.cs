using System;
using UnityEngine;

namespace MapLab.Elevation
{
    [Serializable]
    public struct ElevationBounds
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public ElevationBounds(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    [Serializable]
    public struct ElevationLevel
    {
        public int id;
        public string name;
        public float height;
        public float sortBias;
        public int below;

        public ElevationLevel(int id, string name, float height, float sortBias, int below)
        {
            this.id = id;
            this.name = name;
            this.height = height;
            this.sortBias = sortBias;
            this.below = below;
        }
    }

    [Serializable]
    public struct ElevationCliff
    {
        public int level;
        public bool inner;
        public Vector2[] points;
        public ElevationOpening[] openings;

        public ElevationCliff(int level, bool inner, Vector2[] points, ElevationOpening[] openings)
        {
            this.level = level;
            this.inner = inner;
            this.points = points;
            this.openings = openings;
        }
    }

    [Serializable]
    public struct ElevationOpening
    {
        public Vector2 from;
        public Vector2 to;
        public int ramp;

        public ElevationOpening(Vector2 from, Vector2 to, int ramp)
        {
            this.from = from;
            this.to = to;
            this.ramp = ramp;
        }
    }

    [Serializable]
    public struct ElevationRamp
    {
        public int id;
        public string name;
        public int lower;
        public int upper;
        public float lowHeight;
        public float highHeight;
        public float sortBias;
        public bool stairs;
        public int steps;
        public Vector2[] polygon;
        public Vector2[] top;
        public Vector2[] bottom;
    }
}
