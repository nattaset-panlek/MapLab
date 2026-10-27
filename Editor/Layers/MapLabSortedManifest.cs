using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MapLab.Editor
{
    [Serializable]
    public class MapLabSortedManifest
    {
        #region [Variables/Fields]
        public const int CurrentVersion = 1;

        [Serializable]
        public class Piece
        {
            public string file;
            public int x;
            public int y;
            public int width;
            public int height;
            public Vector3 sortPoint;
            public string sortingLayer;
            public int sortingOrder;
        }

        public int version = CurrentVersion;
        public int originX;
        public int originY;
        public int countX;
        public int countY;
        public float chunkSize;
        public int pixelsPerUnit;
        public List<Piece> pieces = new List<Piece>();
        #endregion

        #region [Properties]
        public MapLabPaintCanvas Canvas => new MapLabPaintCanvas(new Vector2Int(originX, originY), new Vector2Int(countX, countY), chunkSize, pixelsPerUnit);
        #endregion

        #region [Public Methods]
        public static MapLabSortedManifest For(MapLabPaintCanvas canvas)
        {
            return new MapLabSortedManifest
            {
                originX = canvas.Origin.x,
                originY = canvas.Origin.y,
                countX = canvas.Count.x,
                countY = canvas.Count.y,
                chunkSize = canvas.ChunkSize,
                pixelsPerUnit = canvas.PixelsPerUnit
            };
        }

        public static MapLabSortedManifest Load(string path)
        {
            return File.Exists(path) ? JsonUtility.FromJson<MapLabSortedManifest>(File.ReadAllText(path)) : null;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        public Rect WorldRect(Piece piece)
        {
            Rect world = Canvas.WorldRect;
            return new Rect(world.xMin + (float)piece.x / pixelsPerUnit, world.yMax - (float)(piece.y + piece.height) / pixelsPerUnit, (float)piece.width / pixelsPerUnit, (float)piece.height / pixelsPerUnit);
        }

        public RectInt PixelRect(Piece piece)
        {
            return new RectInt(piece.x, piece.y, piece.width, piece.height);
        }
        #endregion
    }
}
