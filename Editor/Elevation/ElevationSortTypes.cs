using UnityEngine;

namespace MapLab.Editor
{
    public enum ElevationSortVariant
    {
        None,
        SnapToSlice,
        LikeATier
    }

    public enum ElevationSortMissKind
    {
        Hidden,
        ShowsThrough
    }

    public readonly struct ElevationSortMiss
    {
        public readonly string Standee;
        public readonly string Other;
        public readonly Vector2 Point;
        public readonly ElevationSortMissKind Kind;
        public readonly float DepthGap;

        public ElevationSortMiss(string standee, string other, Vector2 point, ElevationSortMissKind kind, float depthGap)
        {
            Standee = standee;
            Other = other;
            Point = point;
            Kind = kind;
            DepthGap = depthGap;
        }
    }
}
