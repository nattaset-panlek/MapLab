using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapLab.Editor
{
    public enum MapLabMegaTool
    {
        Select,
        Pan,
        Add,
        Remove,
        Paint,
        Fill,
        Erase,
        Pick
    }

    public class MapLabMegaState
    {
        #region [Variables/Fields]
        public const int MaxBrush = 16;

        public readonly MapLabViewportCamera Camera = new MapLabViewportCamera();
        public readonly HashSet<Vector2Int> Selection = new HashSet<Vector2Int>();
        public readonly MapLabMegaThumbnails Thumbnails = new MapLabMegaThumbnails();
        public HashSet<Vector2Int> Baked = new HashSet<Vector2Int>();
        public Dictionary<string, HashSet<Vector2Int>> LayerMegas = new Dictionary<string, HashSet<Vector2Int>>();
        public MapLabMap Map;
        public MapLabMegaTool Tool = MapLabMegaTool.Select;
        public byte BrushTile = 1;
        public byte NewMegaTile = 1;
        public int BrushSize = 3;
        public bool ShowGrid = true;
        public bool ShowLabels = true;
        public bool ShowStates = true;
        public bool Framed;
        public Vector2Int? HoverMega;
        public Vector2? HoverWorld;
        #endregion

        #region [Properties]
        public bool IsFloorTool => Tool == MapLabMegaTool.Paint || Tool == MapLabMegaTool.Fill || Tool == MapLabMegaTool.Erase || Tool == MapLabMegaTool.Pick;
        public float MinScale => 0.02f;
        public float MaxScale => Map != null ? Map.BakePixelsPerUnit * 8f : 512f;
        public float ZoomPercent => Map != null ? Camera.Scale / Map.BakePixelsPerUnit * 100f : 100f;
        #endregion

        #region [Public Methods]
        public void Refresh(MapLabMap map)
        {
            if (!ReferenceEquals(Map, map))
            {
                Map = map;
                Selection.Clear();
                Thumbnails.Clear();
                Framed = false;
            }
            LayerMegas = map != null ? MapLabMegaStatus.LayerMegas(map) : new Dictionary<string, HashSet<Vector2Int>>();
            Baked = new HashSet<Vector2Int>(LayerMegas.Values.SelectMany(megas => megas));
            if (map != null && map.World != null) Selection.RemoveWhere(index => !map.World.Contains(index));
        }

        public List<Vector2Int> WithState(MapLabBakeStatus.State state)
        {
            if (Map == null || Map.World == null) return new List<Vector2Int>();
            return Map.World.Megas.Where(mega => MapLabMegaStatus.Of(mega, Baked) == state).Select(mega => mega.Index).ToList();
        }

        public HashSet<Vector2Int> Existing()
        {
            return Map != null && Map.World != null ? new HashSet<Vector2Int>(Map.World.Megas.Select(mega => mega.Index)) : new HashSet<Vector2Int>();
        }

        public void SetZoomPercent(float percent)
        {
            if (Map == null) return;
            Camera.ZoomAt(Camera.Screen.center, percent / 100f * Map.BakePixelsPerUnit / Camera.Scale, MinScale, MaxScale);
        }

        public void FrameAll()
        {
            if (Map == null) return;
            Rect world = WorldExtent(0f);
            Camera.Frame(world.width > 0f ? world : new Rect(-Map.MegaChunkSize * 0.5f, -Map.MegaChunkSize * 0.5f, Map.MegaChunkSize, Map.MegaChunkSize), 24f, MinScale, MaxScale);
        }

        public void FrameSelection()
        {
            if (Map == null || Selection.Count == 0)
            {
                FrameAll();
                return;
            }
            Rect area = default;
            bool first = true;
            foreach (var index in Selection)
            {
                Rect rect = MegaChunkFormula.MegaRect(index, Map.MegaChunkSize);
                area = first ? rect : Rect.MinMaxRect(Mathf.Min(area.xMin, rect.xMin), Mathf.Min(area.yMin, rect.yMin), Mathf.Max(area.xMax, rect.xMax), Mathf.Max(area.yMax, rect.yMax));
                first = false;
            }
            Camera.Frame(area, 24f, MinScale, MaxScale);
        }

        public Rect WorldExtent(float marginMegas)
        {
            if (Map == null || Map.World == null || Map.World.Count == 0) return new Rect(0f, 0f, 0f, 0f);
            RectInt extent = Map.World.Extent();
            float size = Map.MegaChunkSize;
            return new Rect((extent.xMin - marginMegas) * size, (extent.yMin - marginMegas) * size, (extent.width + marginMegas * 2f) * size, (extent.height + marginMegas * 2f) * size);
        }

        public void Dispose()
        {
            Thumbnails.Clear();
        }
        #endregion
    }
}
