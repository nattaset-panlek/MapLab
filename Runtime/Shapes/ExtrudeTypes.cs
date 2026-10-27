using UnityEngine;

namespace MapLab.Shapes
{
    public enum ExtrudedShapeType
    {
        Plateau,
        Void,
        Ring
    }

    public enum RampStyle
    {
        Ramp,
        Stairs
    }

    public readonly struct RampCorners
    {
        public readonly Vector2 TopLeft;
        public readonly Vector2 TopRight;
        public readonly Vector2 BottomRight;
        public readonly Vector2 BottomLeft;
        public readonly Vector2 Direction;
        public readonly Vector2 Outward;

        public RampCorners(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft, Vector2 direction, Vector2 outward)
        {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
            Direction = direction;
            Outward = outward;
        }

        public Vector2 TopCenter => (TopLeft + TopRight) * 0.5f;
        public Vector2 BottomCenter => (BottomLeft + BottomRight) * 0.5f;

        public Vector2[] Footprint()
        {
            var points = new[] { TopLeft, TopRight, BottomRight, BottomLeft };
            if (ExtrudeFormula.SignedArea(points) < 0f) System.Array.Reverse(points);
            return points;
        }
    }

    public readonly struct WallRun
    {
        public readonly Vector2[] Points;
        public readonly Vector2[] Normals;
        public readonly float[] Distances;

        public WallRun(Vector2[] points, Vector2[] normals, float[] distances)
        {
            Points = points;
            Normals = normals;
            Distances = distances;
        }

        public float AverageY
        {
            get
            {
                float sum = 0f;
                foreach (var point in Points) sum += point.y;
                return Points.Length > 0 ? sum / Points.Length : 0f;
            }
        }
    }

    public readonly struct WallQuad
    {
        public readonly Vector2 From;
        public readonly Vector2 To;
        public readonly float UFrom;
        public readonly float UTo;
        public readonly float ShadeFrom;
        public readonly float ShadeTo;

        public WallQuad(Vector2 from, Vector2 to, float uFrom, float uTo, float shadeFrom, float shadeTo)
        {
            From = from;
            To = to;
            UFrom = uFrom;
            UTo = uTo;
            ShadeFrom = shadeFrom;
            ShadeTo = shadeTo;
        }

        public float MaxBaseY => Mathf.Max(From.y, To.y);
    }

    public readonly struct ShadowLoop
    {
        public readonly Vector2[] Base;
        public readonly Vector2[] Reach;
        public readonly bool[] Casts;

        public ShadowLoop(Vector2[] baseLine, Vector2[] reach, bool[] casts)
        {
            Base = baseLine;
            Reach = reach;
            Casts = casts;
        }
    }

    public readonly struct ShapeTriangle
    {
        public readonly Vector2 A;
        public readonly Vector2 B;
        public readonly Vector2 C;
        public readonly Color Color;
        public readonly bool Flat;

        public ShapeTriangle(Vector2 a, Vector2 b, Vector2 c, Color color, bool flat)
        {
            A = a;
            B = b;
            C = c;
            Color = color;
            Flat = flat;
        }

        public float MinY => Mathf.Min(A.y, Mathf.Min(B.y, C.y));
        public float MaxY => Mathf.Max(A.y, Mathf.Max(B.y, C.y));
    }
}
