using System;
using UnityEngine;

namespace MapLab.Editor
{
    [Serializable]
    public struct WalkableBounds
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public WalkableBounds(Rect rect)
        {
            x = rect.x;
            y = rect.y;
            width = rect.width;
            height = rect.height;
        }
    }

    [Serializable]
    public struct WalkablePolygon
    {
        public Vector2[] points;

        public WalkablePolygon(Vector2[] points)
        {
            this.points = points;
        }
    }
}
