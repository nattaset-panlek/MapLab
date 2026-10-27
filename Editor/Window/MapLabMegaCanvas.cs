using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabMegaCanvas
    {
        #region [Variables/Fields]
        public const float RulerTop = 18f;
        public const float RulerLeft = 26f;
        public const float Bar = 13f;

        private const float MiniWidth = 150f;
        private const float MiniHeight = 112f;
        private const float LabelPixels = 56f;
        private const float SlotPixels = 24f;
        private const float ChunkGridPixels = 24f;
        private const float ChunkLabelPixels = 90f;
        private const float TileGridPixels = 6f;
        private const int MaxCells = 6000;

        private static readonly Color CanvasColor = new Color32(0x24, 0x27, 0x2C, 0xFF);
        private static readonly Color RulerColor = new Color32(0x2C, 0x2F, 0x34, 0xFF);
        private static readonly Color RulerLine = new Color32(0x1E, 0x20, 0x24, 0xFF);
        private static readonly Color TickColor = new Color32(0x4A, 0x4F, 0x57, 0xFF);
        private static readonly Color MegaTickColor = new Color32(0x8A, 0x90, 0x99, 0xFF);
        private static readonly Color GridColor = new Color32(0x2F, 0x33, 0x3A, 0xFF);
        private static readonly Color EmptyMega = new Color32(0x30, 0x34, 0x3A, 0xFF);
        private static readonly Color BorderColor = new Color32(0x0E, 0x10, 0x13, 0x8C);
        private static readonly Color ChipColor = new Color32(0x15, 0x18, 0x1C, 0xCC);
        private static readonly Color PanelColor = new Color32(0x15, 0x17, 0x1A, 0xF0);
        private static readonly Color PanelLine = new Color32(0x4A, 0x4F, 0x57, 0xFF);
        private static readonly Color SlotLine = new Color32(0x4E, 0x54, 0x5C, 0xFF);
        private static readonly Color SlotPlus = new Color32(0x6E, 0x75, 0x7E, 0xFF);
        private static readonly Color Blue = new Color32(0x5B, 0x8D, 0xEF, 0xFF);
        private static readonly Color BlueFill = new Color32(0x3D, 0x6F, 0xD6, 0x40);
        private static readonly Color PickedFill = new Color32(0x3D, 0x6F, 0xD6, 0x70);
        private static readonly Color ChunkLine = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color TileLine = new Color(1f, 1f, 1f, 0.06f);

        private enum Drag
        {
            None,
            Pan,
            Box,
            Add,
            Paint,
            Mini
        }

        private Drag _drag;
        private Vector2 _dragStart;
        private Vector2 _dragNow;
        private Vector2 _lastPaint;
        private int _undoGroup;
        private Rect _content;
        private GUIStyle _chip;
        private GUIStyle _small;
        private GUIStyle _ruler;
        private GUIStyle _tipTitle;
        private GUIStyle _tipText;
        private GUIStyle _hint;
        private GUIStyle _center;
        #endregion

        #region [Public Methods]
        public void Draw(Rect area, MapLabMegaState state, Action repaint, Action changed)
        {
            BuildStyles();
            _content = new Rect(area.x + RulerLeft, area.y + RulerTop, area.width - RulerLeft - Bar, area.height - RulerTop - Bar);
            if (_content.width < 60f || _content.height < 60f) return;
            state.Camera.Screen = new Rect(0f, 0f, _content.width, _content.height);
            if (!state.Framed)
            {
                state.FrameAll();
                state.Framed = true;
            }
            DrawScrollbars(state);
            DrawRulers(area, state);
            GUI.BeginClip(_content);
            HandleInput(state, repaint, changed);
            if (Event.current.type == EventType.Repaint) Paint(state);
            GUI.EndClip();
        }
        #endregion

        #region [Private Methods]
        private void HandleInput(MapLabMegaState state, Action repaint, Action changed)
        {
            Event current = Event.current;
            MapLabViewportCamera camera = state.Camera;
            MapLabMap map = state.Map;
            Rect view = camera.Screen;
            Vector2 mouse = current.mousePosition;
            bool inside = view.Contains(mouse);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            switch (current.GetTypeForControl(control))
            {
                case EventType.MouseMove:
                    if (!inside) break;
                    Hover(state, mouse);
                    repaint();
                    break;
                case EventType.ScrollWheel:
                    if (!inside) break;
                    camera.ZoomAt(mouse, Mathf.Exp(-current.delta.y * 0.06f), state.MinScale, state.MaxScale);
                    Hover(state, mouse);
                    current.Use();
                    repaint();
                    break;
                case EventType.MouseDown:
                    if (!inside) break;
                    GUIUtility.keyboardControl = 0;
                    if (current.button == 0 && MiniRect(view).Contains(mouse))
                    {
                        Begin(control, Drag.Mini, mouse);
                        JumpMini(state, mouse);
                    }
                    else if (current.button == 2 || (current.button == 0 && (state.Tool == MapLabMegaTool.Pan || (current.alt && !state.IsFloorTool))))
                    {
                        Begin(control, Drag.Pan, mouse);
                    }
                    else if (current.button == 0)
                    {
                        Press(state, control, current, mouse, changed);
                    }
                    else
                    {
                        break;
                    }
                    current.Use();
                    repaint();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control) break;
                    _dragNow = mouse;
                    if (_drag == Drag.Pan) camera.Pan(current.delta);
                    else if (_drag == Drag.Mini) JumpMini(state, mouse);
                    else if (_drag == Drag.Paint) PaintStroke(state, camera.ScreenToWorld(mouse), current.shift || state.Tool == MapLabMegaTool.Erase);
                    Hover(state, mouse);
                    current.Use();
                    repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != control) break;
                    GUIUtility.hotControl = 0;
                    Release(state, changed);
                    _drag = Drag.None;
                    current.Use();
                    repaint();
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != 0) break;
                    if (Key(state, current.keyCode, changed))
                    {
                        current.Use();
                        repaint();
                    }
                    break;
            }
            if (map.World == null) return;
            if (current.type == EventType.Repaint && inside && _drag == Drag.None) Hover(state, mouse);
            if (current.type == EventType.MouseLeaveWindow)
            {
                state.HoverMega = null;
                state.HoverWorld = null;
                repaint();
            }
        }

        private void Begin(int control, Drag drag, Vector2 mouse)
        {
            GUIUtility.hotControl = control;
            _drag = drag;
            _dragStart = mouse;
            _dragNow = mouse;
        }

        private void Press(MapLabMegaState state, int control, Event current, Vector2 mouse, Action changed)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            Vector2 point = state.Camera.ScreenToWorld(mouse);
            Vector2Int mega = MegaChunkFormula.MegaAt(point, map.MegaChunkSize);
            bool exists = world != null && world.Contains(mega);
            switch (state.Tool)
            {
                case MapLabMegaTool.Select:
                    if (exists && current.clickCount == 2)
                    {
                        state.Selection.Clear();
                        state.Selection.Add(mega);
                        state.FrameSelection();
                        return;
                    }
                    if (exists)
                    {
                        if (current.shift || EditorGUI.actionKey)
                        {
                            if (!state.Selection.Remove(mega)) state.Selection.Add(mega);
                        }
                        else if (!state.Selection.Contains(mega))
                        {
                            state.Selection.Clear();
                            state.Selection.Add(mega);
                        }
                        Begin(control, Drag.None, mouse);
                        return;
                    }
                    if (world != null && (world.IsEdge(mega) || world.Count == 0))
                    {
                        Begin(control, Drag.Add, mouse);
                        return;
                    }
                    if (!current.shift) state.Selection.Clear();
                    Begin(control, Drag.Box, mouse);
                    return;
                case MapLabMegaTool.Add:
                    Begin(control, Drag.Add, mouse);
                    return;
                case MapLabMegaTool.Remove:
                    if (!exists) return;
                    var targets = state.Selection.Contains(mega) ? state.Selection.ToList() : new List<Vector2Int> { mega };
                    RemoveWithConfirm(state, targets, changed);
                    return;
                case MapLabMegaTool.Pick:
                    PickAt(state, point);
                    return;
                case MapLabMegaTool.Fill:
                    if (world == null) return;
                    if (current.alt)
                    {
                        PickAt(state, point);
                        return;
                    }
                    if (current.shift)
                    {
                        var fill = state.Selection.Contains(mega) ? state.Selection.ToList() : new List<Vector2Int> { mega };
                        MapLabWorldEdits.FillMegas(map, fill, state.BrushTile);
                    }
                    else
                    {
                        MapLabWorldEdits.FillChunk(map, point, state.BrushTile);
                    }
                    changed();
                    return;
                default:
                    if (world == null) return;
                    if (current.alt)
                    {
                        PickAt(state, point);
                        return;
                    }
                    Undo.IncrementCurrentGroup();
                    _undoGroup = Undo.GetCurrentGroup();
                    Begin(control, Drag.Paint, mouse);
                    _lastPaint = point;
                    PaintStroke(state, point, current.shift || state.Tool == MapLabMegaTool.Erase);
                    return;
            }
        }

        private void Release(MapLabMegaState state, Action changed)
        {
            MapLabMap map = state.Map;
            switch (_drag)
            {
                case Drag.Box:
                    if (Vector2.Distance(_dragStart, _dragNow) < 3f) return;
                    Rect box = WorldBox(state);
                    foreach (var mega in map.World != null ? map.World.Megas : (IReadOnlyList<MapLabMegaChunk>)new List<MapLabMegaChunk>())
                    {
                        if (MegaChunkFormula.MegaRect(mega.Index, map.MegaChunkSize).Overlaps(box)) state.Selection.Add(mega.Index);
                    }
                    return;
                case Drag.Add:
                    List<Vector2Int> cells = AddCells(state);
                    if (cells.Count == 0) return;
                    MapLabWorldEdits.AddMegas(map, cells, state.NewMegaTile);
                    state.Selection.Clear();
                    state.Selection.UnionWith(cells);
                    changed();
                    return;
                case Drag.Paint:
                    Undo.CollapseUndoOperations(_undoGroup);
                    changed();
                    return;
            }
        }

        private bool Key(MapLabMegaState state, KeyCode key, Action changed)
        {
            switch (key)
            {
                case KeyCode.F:
                    state.FrameSelection();
                    return true;
                case KeyCode.Home:
                    state.FrameAll();
                    return true;
                case KeyCode.LeftBracket:
                    state.BrushSize = Mathf.Max(1, state.BrushSize - 1);
                    return true;
                case KeyCode.RightBracket:
                    state.BrushSize = Mathf.Min(MapLabMegaState.MaxBrush, state.BrushSize + 1);
                    return true;
                case KeyCode.Escape:
                    if (_drag != Drag.None)
                    {
                        _drag = Drag.None;
                        GUIUtility.hotControl = 0;
                    }
                    else
                    {
                        state.Selection.Clear();
                    }
                    return true;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (state.Selection.Count == 0) return false;
                    RemoveWithConfirm(state, state.Selection.ToList(), changed);
                    return true;
                case KeyCode.V:
                    state.Tool = MapLabMegaTool.Select;
                    return true;
                case KeyCode.H:
                    state.Tool = MapLabMegaTool.Pan;
                    return true;
                case KeyCode.A:
                    state.Tool = MapLabMegaTool.Add;
                    return true;
                case KeyCode.B:
                    state.Tool = MapLabMegaTool.Paint;
                    return true;
                case KeyCode.G:
                    state.Tool = MapLabMegaTool.Fill;
                    return true;
                case KeyCode.E:
                    state.Tool = MapLabMegaTool.Erase;
                    return true;
                case KeyCode.I:
                    state.Tool = MapLabMegaTool.Pick;
                    return true;
                default:
                    return false;
            }
        }

        private static void RemoveWithConfirm(MapLabMegaState state, List<Vector2Int> targets, Action changed)
        {
            string what = targets.Count == 1 ? $"mega chunk {targets[0].x},{targets[0].y}" : $"{targets.Count} mega chunks";
            if (!EditorUtility.DisplayDialog("MapLab", $"Remove {what}? Their floor tiles go away (Undo brings them back). Baked images and paint stay on disk.", "Remove", "Cancel")) return;
            MapLabWorldEdits.RemoveMegas(state.Map, targets);
            foreach (var target in targets) state.Selection.Remove(target);
            changed();
            GUIUtility.ExitGUI();
        }

        private static void PickAt(MapLabMegaState state, Vector2 point)
        {
            byte tile = MapLabWorldEdits.Pick(state.Map, point);
            if (tile != 0) state.BrushTile = tile;
            if (state.Tool == MapLabMegaTool.Pick) state.Tool = MapLabMegaTool.Paint;
        }

        private void PaintStroke(MapLabMegaState state, Vector2 point, bool erase)
        {
            MapLabMap map = state.Map;
            float tileSize = MapLabWorldEdits.TileSize(map);
            var tiles = new HashSet<Vector2Int>();
            float distance = Vector2.Distance(_lastPaint, point);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (tileSize * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 sample = Vector2.Lerp(_lastPaint, point, i / (float)steps);
                tiles.UnionWith(FloorFormula.Brush(MegaChunkFormula.TileAt(sample, tileSize), state.BrushSize));
            }
            _lastPaint = point;
            MapLabWorldEdits.SetTiles(map, tiles, erase ? (byte)0 : state.BrushTile, erase ? "Erase Floor" : "Paint Floor");
        }

        private static void Hover(MapLabMegaState state, Vector2 mouse)
        {
            Vector2 point = state.Camera.ScreenToWorld(mouse);
            state.HoverWorld = point;
            state.HoverMega = MegaChunkFormula.MegaAt(point, state.Map.MegaChunkSize);
        }

        private Rect WorldBox(MapLabMegaState state)
        {
            Vector2 a = state.Camera.ScreenToWorld(_dragStart);
            Vector2 b = state.Camera.ScreenToWorld(_dragNow);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private List<Vector2Int> AddCells(MapLabMegaState state)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            float size = map.MegaChunkSize;
            Vector2Int from = MegaChunkFormula.MegaAt(state.Camera.ScreenToWorld(_dragStart), size);
            Vector2Int to = MegaChunkFormula.MegaAt(state.Camera.ScreenToWorld(_dragNow), size);
            var cells = new List<Vector2Int>();
            for (int y = Mathf.Min(from.y, to.y); y <= Mathf.Max(from.y, to.y); y++)
            {
                for (int x = Mathf.Min(from.x, to.x); x <= Mathf.Max(from.x, to.x); x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (world == null || !world.Contains(cell)) cells.Add(cell);
                    if (cells.Count > MaxCells) return new List<Vector2Int>();
                }
            }
            if (world == null || world.Count == 0) return cells;
            return cells.Any(world.IsEdge) ? cells : new List<Vector2Int>();
        }

        private void DrawScrollbars(MapLabMegaState state)
        {
            MapLabViewportCamera camera = state.Camera;
            Rect visible = camera.Visible;
            Rect extent = state.WorldExtent(2f);
            extent = extent.width > 0f ? Rect.MinMaxRect(Mathf.Min(extent.xMin, visible.xMin), Mathf.Min(extent.yMin, visible.yMin), Mathf.Max(extent.xMax, visible.xMax), Mathf.Max(extent.yMax, visible.yMax)) : visible;
            float x = GUI.HorizontalScrollbar(new Rect(_content.x, _content.yMax, _content.width, Bar), visible.xMin, visible.width, extent.xMin, extent.xMax);
            if (Mathf.Abs(x - visible.xMin) > 0.0001f) camera.Center = new Vector2(x + visible.width * 0.5f, camera.Center.y);
            float top = extent.yMax - visible.yMax;
            float y = GUI.VerticalScrollbar(new Rect(_content.xMax, _content.y, Bar, _content.height), top, visible.height, 0f, extent.height);
            if (Mathf.Abs(y - top) > 0.0001f) camera.Center = new Vector2(camera.Center.x, extent.yMax - y - visible.height * 0.5f);
            EditorGUI.DrawRect(new Rect(_content.xMax, _content.yMax, Bar, Bar), RulerColor);
        }

        private void DrawRulers(Rect area, MapLabMegaState state)
        {
            if (Event.current.type != EventType.Repaint) return;
            MapLabMap map = state.Map;
            MapLabViewportCamera camera = state.Camera;
            var top = new Rect(_content.x, area.y, _content.width, RulerTop);
            var left = new Rect(area.x, _content.y, RulerLeft, _content.height);
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, RulerTop), RulerColor);
            EditorGUI.DrawRect(left, RulerColor);
            EditorGUI.DrawRect(new Rect(area.x, area.y + RulerTop - 1f, area.width, 1f), RulerLine);
            EditorGUI.DrawRect(new Rect(area.x + RulerLeft - 1f, _content.y, 1f, _content.height), RulerLine);

            bool chunks = map.ChunkSize * camera.Scale >= 40f;
            float step = chunks ? map.ChunkSize : map.MegaChunkSize;
            int every = Mathf.Max(1, Mathf.CeilToInt(22f / (step * camera.Scale)));
            int perMega = chunks ? map.ChunksPerMega : 1;
            GUI.Label(new Rect(area.x, area.y, RulerLeft, RulerTop), chunks ? "C" : "M", _ruler);
            Rect visible = camera.Visible;
            int first = Mathf.FloorToInt(visible.xMin / step);
            int last = Mathf.CeilToInt(visible.xMax / step);
            for (int i = first; i <= last && last - first < 4000; i++)
            {
                float sx = _content.x + camera.WorldToScreen(new Vector2(i * step, 0f)).x;
                bool mega = i % perMega == 0;
                if (sx >= _content.x && sx <= _content.xMax) EditorGUI.DrawRect(new Rect(sx, top.y + (mega && chunks ? 2f : 10f), 1f, mega && chunks ? 16f : 8f), mega && chunks ? MegaTickColor : TickColor);
                if (i % every != 0) continue;
                var label = new Rect(sx, top.y, step * camera.Scale * every, RulerTop - 2f);
                if (label.center.x > _content.x + 8f && label.center.x < _content.xMax - 8f) GUI.Label(new Rect(label.center.x - 30f, top.y, 60f, RulerTop), i.ToString(), _ruler);
            }
            first = Mathf.FloorToInt(visible.yMin / step);
            last = Mathf.CeilToInt(visible.yMax / step);
            for (int i = first; i <= last && last - first < 4000; i++)
            {
                float sy = _content.y + camera.WorldToScreen(new Vector2(0f, i * step)).y;
                bool mega = i % perMega == 0;
                if (sy >= _content.y && sy <= _content.yMax) EditorGUI.DrawRect(new Rect(left.x + (mega && chunks ? 2f : 18f), sy, mega && chunks ? 24f : 8f, 1f), mega && chunks ? MegaTickColor : TickColor);
                if (i % every != 0) continue;
                float center = sy - step * camera.Scale * every * 0.5f;
                if (center > _content.y + 8f && center < _content.yMax - 8f) GUI.Label(new Rect(left.x, center - 8f, RulerLeft - 2f, 16f), i.ToString(), _ruler);
            }
        }

        private void Paint(MapLabMegaState state)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            MapLabViewportCamera camera = state.Camera;
            Rect view = camera.Screen;
            EditorGUI.DrawRect(view, CanvasColor);
            float megaSize = map.MegaChunkSize;
            float megaPixels = megaSize * camera.Scale;
            Rect visible = camera.Visible;
            var range = new RectInt(Mathf.FloorToInt(visible.xMin / megaSize), Mathf.FloorToInt(visible.yMin / megaSize), Mathf.CeilToInt(visible.width / megaSize) + 2, Mathf.CeilToInt(visible.height / megaSize) + 2);
            bool cellsFit = range.width * range.height <= MaxCells;

            if (state.ShowGrid && megaPixels >= 10f && cellsFit)
            {
                for (int x = range.xMin; x <= range.xMax; x++) EditorGUI.DrawRect(new Rect(camera.WorldToScreen(new Vector2(x * megaSize, 0f)).x, 0f, 1f, view.height), GridColor);
                for (int y = range.yMin; y <= range.yMax; y++) EditorGUI.DrawRect(new Rect(0f, camera.WorldToScreen(new Vector2(0f, y * megaSize)).y, view.width, 1f), GridColor);
            }
            if (world == null) return;

            var onScreen = new List<(MapLabMegaChunk Mega, Rect Rect)>();
            foreach (var mega in world.Megas)
            {
                Rect rect = camera.WorldToScreen(MegaChunkFormula.MegaRect(mega.Index, megaSize));
                if (!rect.Overlaps(view)) continue;
                onScreen.Add((mega, rect));
                EditorGUI.DrawRect(rect, EmptyMega);
                GUI.DrawTexture(rect, state.Thumbnails.Get(world, mega), ScaleMode.StretchToFill, true);
            }

            float chunkPixels = map.ChunkSize * camera.Scale;
            float tilePixels = megaPixels / world.TilesPerSide;
            foreach (var (mega, rect) in onScreen)
            {
                if (state.ShowGrid && tilePixels >= TileGridPixels) GridLines(rect, world.TilesPerSide, TileLine);
                if (state.ShowGrid && chunkPixels >= ChunkGridPixels) GridLines(rect, map.ChunksPerMega, ChunkLine);
                if (megaPixels >= 3f) Outline(rect, BorderColor, 1f);
            }

            if (state.ShowLabels && chunkPixels >= ChunkLabelPixels)
            {
                int perMega = map.ChunksPerMega;
                foreach (var (mega, rect) in onScreen)
                {
                    for (int j = 0; j < perMega; j++)
                    {
                        for (int i = 0; i < perMega; i++)
                        {
                            var chunk = new Rect(rect.x + i * chunkPixels, rect.y + (perMega - 1 - j) * chunkPixels, chunkPixels, chunkPixels);
                            if (!chunk.Overlaps(view)) continue;
                            float inset = i == 0 && j == perMega - 1 && state.ShowLabels ? 52f : 5f;
                            GUI.Label(new Rect(chunk.x + inset, chunk.y + 4f, 80f, 14f), $"{mega.Index.x * perMega + i},{mega.Index.y * perMega + j}", _small);
                        }
                    }
                }
            }

            foreach (var (mega, rect) in onScreen)
            {
                bool selected = state.Selection.Contains(mega.Index);
                if (selected) Outline(rect, Blue, 2f);
                if (state.ShowLabels && megaPixels >= LabelPixels) Chip(new Vector2(rect.x + 5f, rect.y + 5f), $"{mega.Index.x},{mega.Index.y}", selected);
                if (state.ShowStates && megaPixels >= SlotPixels)
                {
                    Color color = MapLabWindowStyles.StateColor(MapLabMegaStatus.Of(mega, state.Baked));
                    var dot = new Rect(rect.xMax - 14f, rect.y + 9f, 8f, 8f);
                    GUI.DrawTexture(dot, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, color, 0f, 4f);
                }
            }

            PaintSlots(state, range, cellsFit, megaPixels);
            PaintDrag(state);
            PaintBrush(state);
            PaintMini(state, MiniRect(view));
            PaintHint(state, view);
            PaintTooltip(state, view);
        }

        private void PaintSlots(MapLabMegaState state, RectInt range, bool cellsFit, float megaPixels)
        {
            MapLabWorldData world = state.Map.World;
            if (!cellsFit || megaPixels < SlotPixels || (state.Tool != MapLabMegaTool.Select && state.Tool != MapLabMegaTool.Add)) return;
            HashSet<Vector2Int> picked = _drag == Drag.Add ? new HashSet<Vector2Int>(AddCells(state)) : new HashSet<Vector2Int>();
            HashSet<Vector2Int> existing = state.Existing();
            float size = state.Map.MegaChunkSize;
            for (int y = range.yMin; y <= range.yMax; y++)
            {
                for (int x = range.xMin; x <= range.xMax; x++)
                {
                    var cell = new Vector2Int(x, y);
                    bool isPicked = picked.Contains(cell);
                    bool slot = world.Count == 0 ? cell == Vector2Int.zero : IsEdge(existing, cell);
                    if (!slot && !isPicked) continue;
                    Rect rect = state.Camera.WorldToScreen(MegaChunkFormula.MegaRect(cell, size));
                    var inner = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
                    bool hover = _drag == Drag.None && state.HoverMega == cell;
                    if (isPicked) EditorGUI.DrawRect(inner, PickedFill);
                    else if (hover) EditorGUI.DrawRect(inner, BlueFill);
                    MapLabWidgets.DashedBox(inner, isPicked || hover ? Blue : SlotLine, 5f, 4f);
                    float plus = Mathf.Min(16f, inner.width * 0.4f);
                    bool label = (hover || isPicked) && inner.height >= 50f;
                    var center = new Vector2(inner.center.x, inner.center.y - (label ? 7f : 0f));
                    MapLabGlyphs.Draw(new Rect(center.x - plus * 0.5f, center.y - plus * 0.5f, plus, plus), MapLabGlyph.Add, isPicked ? Color.white : hover ? Blue : SlotPlus);
                    if (label) GUI.Label(new Rect(inner.x, center.y + plus * 0.5f, inner.width, 14f), hover && !isPicked ? $"Add {x},{y}" : "Add", _center);
                }
            }
        }

        private void PaintDrag(MapLabMegaState state)
        {
            if (_drag != Drag.Box || Vector2.Distance(_dragStart, _dragNow) < 3f) return;
            Rect box = Rect.MinMaxRect(Mathf.Min(_dragStart.x, _dragNow.x), Mathf.Min(_dragStart.y, _dragNow.y), Mathf.Max(_dragStart.x, _dragNow.x), Mathf.Max(_dragStart.y, _dragNow.y));
            EditorGUI.DrawRect(box, new Color(Blue.r, Blue.g, Blue.b, 0.1f));
            Outline(box, Blue, 1f);
        }

        private void PaintBrush(MapLabMegaState state)
        {
            if (!state.IsFloorTool || !state.HoverWorld.HasValue || state.Map.World == null || _drag == Drag.Pan) return;
            MapLabMap map = state.Map;
            Vector2 point = state.HoverWorld.Value;
            Rect world;
            if (state.Tool == MapLabMegaTool.Fill)
            {
                if (Event.current.shift)
                {
                    world = MegaChunkFormula.MegaRect(MegaChunkFormula.MegaAt(point, map.MegaChunkSize), map.MegaChunkSize);
                }
                else
                {
                    float chunk = map.ChunkSize;
                    world = new Rect(Mathf.Floor(point.x / chunk) * chunk, Mathf.Floor(point.y / chunk) * chunk, chunk, chunk);
                }
            }
            else
            {
                float tile = MapLabWorldEdits.TileSize(map);
                int size = state.Tool == MapLabMegaTool.Pick ? 1 : state.BrushSize;
                Vector2Int center = MegaChunkFormula.TileAt(point, tile);
                Vector2Int min = center + Vector2Int.one * (-(size - 1) / 2);
                world = new Rect(min.x * tile, min.y * tile, size * tile, size * tile);
            }
            Rect rect = state.Camera.WorldToScreen(world);
            EditorGUI.DrawRect(rect, new Color(Blue.r, Blue.g, Blue.b, 0.2f));
            Outline(rect, Blue, 1.5f);
        }

        private void PaintMini(MapLabMegaState state, Rect mini)
        {
            MapLabWorldData world = state.Map.World;
            if (world == null || world.Count == 0) return;
            EditorGUI.DrawRect(mini, PanelColor);
            Outline(mini, PanelLine, 1f);
            RectInt extent = world.Extent();
            GUI.Label(new Rect(mini.x + 8f, mini.y + 2f, mini.width - 16f, 14f), $"MAP  {extent.width} x {extent.height}", _small);
            Rect inner = MiniInner(mini);
            float cell = Mathf.Min(inner.width / extent.width, inner.height / extent.height);
            Vector2 origin = inner.center - new Vector2(extent.width, extent.height) * cell * 0.5f;
            foreach (var mega in world.Megas)
            {
                var rect = new Rect(origin.x + (mega.Index.x - extent.xMin) * cell, origin.y + (extent.yMax - 1 - mega.Index.y) * cell, cell, cell);
                Color color = state.Thumbnails.TileColor(world.Palette, mega.BaseTile);
                if (color.a <= 0f) color = EmptyMega;
                EditorGUI.DrawRect(rect, color);
            }
            float size = state.Map.MegaChunkSize;
            Rect visible = state.Camera.Visible;
            var view = Rect.MinMaxRect(origin.x + (visible.xMin / size - extent.xMin) * cell, origin.y + (extent.yMax - visible.yMax / size) * cell,
                origin.x + (visible.xMax / size - extent.xMin) * cell, origin.y + (extent.yMax - visible.yMin / size) * cell);
            view = Rect.MinMaxRect(Mathf.Max(view.xMin, mini.x + 1f), Mathf.Max(view.yMin, mini.y + 1f), Mathf.Min(view.xMax, mini.xMax - 1f), Mathf.Min(view.yMax, mini.yMax - 1f));
            if (view.width > 0f && view.height > 0f) Outline(view, Color.white, 1f);
        }

        private void JumpMini(MapLabMegaState state, Vector2 mouse)
        {
            MapLabWorldData world = state.Map.World;
            if (world == null || world.Count == 0) return;
            Rect inner = MiniInner(MiniRect(state.Camera.Screen));
            RectInt extent = world.Extent();
            float cell = Mathf.Min(inner.width / extent.width, inner.height / extent.height);
            Vector2 origin = inner.center - new Vector2(extent.width, extent.height) * cell * 0.5f;
            float size = state.Map.MegaChunkSize;
            state.Camera.Center = new Vector2((extent.xMin + (mouse.x - origin.x) / cell) * size, (extent.yMax - (mouse.y - origin.y) / cell) * size);
        }

        private void PaintHint(MapLabMegaState state, Rect view)
        {
            string hint = Hint(state);
            var content = new GUIContent(hint);
            float width = _hint.CalcSize(content).x + 20f;
            var rect = new Rect(8f, view.height - 30f, Mathf.Min(width, view.width - MiniWidth - 30f), 22f);
            if (rect.width < 60f) return;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, PanelColor, 0f, 11f);
            GUI.Label(rect, content, _hint);
        }

        private void PaintTooltip(MapLabMegaState state, Rect view)
        {
            if (!state.HoverWorld.HasValue || !state.HoverMega.HasValue) return;
            Vector2 mouse = Event.current.mousePosition;
            if (!view.Contains(mouse) || MiniRect(view).Contains(mouse)) return;
            (string title, string detail) = Tooltip(state);
            if (title == null) return;
            var titleContent = new GUIContent(title);
            var detailContent = new GUIContent(detail ?? "");
            float width = Mathf.Max(_tipTitle.CalcSize(titleContent).x, detail != null ? _tipText.CalcSize(detailContent).x : 0f) + 16f;
            float height = detail != null ? 36f : 22f;
            var rect = new Rect(mouse.x + 18f, mouse.y + 14f, width, height);
            if (rect.xMax > view.width - 4f) rect.x = mouse.x - width - 12f;
            if (rect.yMax > view.height - 4f) rect.y = mouse.y - height - 10f;
            EditorGUI.DrawRect(rect, PanelColor);
            Outline(rect, PanelLine, 1f);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, width, 16f), titleContent, _tipTitle);
            if (detail != null) GUI.Label(new Rect(rect.x + 8f, rect.y + 18f, width, 14f), detailContent, _tipText);
        }

        private (string, string) Tooltip(MapLabMegaState state)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            if (world == null) return (null, null);
            Vector2Int mega = state.HoverMega.Value;
            bool exists = world.Contains(mega);
            string tile = world.Palette != null ? world.Palette.NameOf(state.BrushTile) : "Tile";
            if (_drag == Drag.Add)
            {
                int count = AddCells(state).Count;
                return count > 0 ? ($"Release to add {count} mega chunk{(count == 1 ? "" : "s")}", "They start with the base tile set in the inspector") : ("Touch the map edge to add", null);
            }
            switch (state.Tool)
            {
                case MapLabMegaTool.Select:
                case MapLabMegaTool.Add:
                    if (exists || !(world.IsEdge(mega) || (world.Count == 0 && mega == Vector2Int.zero))) return (null, null);
                    return ($"Add mega chunk {mega.x},{mega.y}", "Click to add, or drag across + to add several");
                case MapLabMegaTool.Remove:
                    return exists ? ($"Remove mega chunk {mega.x},{mega.y}", "Its baked files stay on disk") : (null, null);
                case MapLabMegaTool.Paint:
                    return exists ? ($"{tile}  ·  {state.BrushSize} x {state.BrushSize} tiles", "Shift: erase   Alt: pick tile   [ ]: size") : ("Outside the map", "Add a mega chunk here first");
                case MapLabMegaTool.Erase:
                    return exists ? ($"Erase  ·  {state.BrushSize} x {state.BrushSize} tiles", "Tiles go back to the base tile") : (null, null);
                case MapLabMegaTool.Fill:
                    return exists ? (Event.current.shift ? $"Fill mega chunk {mega.x},{mega.y} with {tile}" : $"Fill chunk with {tile}", "Shift-click fills the whole mega chunk") : (null, null);
                case MapLabMegaTool.Pick:
                    if (!exists) return (null, null);
                    byte under = MapLabWorldEdits.Pick(map, state.HoverWorld.Value);
                    return ($"Pick {(world.Palette != null ? world.Palette.NameOf(under) : "tile")}", "Click to use it in the brush");
                default:
                    return (null, null);
            }
        }

        private static string Hint(MapLabMegaState state)
        {
            switch (state.Tool)
            {
                case MapLabMegaTool.Pan: return "Drag: pan   Wheel: zoom   Home: show all";
                case MapLabMegaTool.Add: return "Click +: add one   Drag across +: add a block   Esc: cancel";
                case MapLabMegaTool.Remove: return "Click a mega chunk to remove it   Its files stay on disk";
                case MapLabMegaTool.Paint: return "Click: place   Shift: erase   Alt: pick   [ ]: brush size";
                case MapLabMegaTool.Fill: return "Click: fill a chunk   Shift-click: fill the mega chunk";
                case MapLabMegaTool.Erase: return "Drag: erase back to the base tile   [ ]: brush size";
                case MapLabMegaTool.Pick: return "Click a tile to use it in the brush";
                default: return "Wheel: zoom   Middle drag: pan   Double-click: zoom to a mega chunk";
            }
        }

        private void Chip(Vector2 position, string text, bool selected)
        {
            var content = new GUIContent(text);
            float width = _chip.CalcSize(content).x + 10f;
            var rect = new Rect(position.x, position.y, width, 16f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, selected ? MapLabWindowStyles.Primary : ChipColor, 0f, 3f);
            GUI.Label(rect, content, _chip);
        }

        private static bool IsEdge(HashSet<Vector2Int> existing, Vector2Int cell)
        {
            if (existing.Contains(cell)) return false;
            return existing.Contains(cell + Vector2Int.right) || existing.Contains(cell + Vector2Int.left) || existing.Contains(cell + Vector2Int.up) || existing.Contains(cell + Vector2Int.down);
        }

        private static void GridLines(Rect rect, int count, Color color)
        {
            float step = rect.width / count;
            for (int i = 1; i < count; i++)
            {
                EditorGUI.DrawRect(new Rect(rect.x + i * step, rect.y, 1f, rect.height), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + i * step, rect.width, 1f), color);
            }
        }

        private static void Outline(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static Rect MiniRect(Rect view)
        {
            return new Rect(view.width - MiniWidth - 8f, view.height - MiniHeight - 8f, MiniWidth, MiniHeight);
        }

        private static Rect MiniInner(Rect mini)
        {
            return new Rect(mini.x + 8f, mini.y + 18f, mini.width - 16f, mini.height - 26f);
        }

        private void BuildStyles()
        {
            if (_chip != null) return;
            _chip = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, fontSize = 10, padding = new RectOffset(0, 0, 0, 0) };
            _small = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }, fontSize = 9, clipping = TextClipping.Clip };
            _ruler = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color32(0x80, 0x86, 0x8F, 0xFF) }, fontSize = 9 };
            _tipTitle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color32(0xED, 0xED, 0xED, 0xFF) }, fontSize = 11, padding = new RectOffset(0, 0, 0, 0) };
            _tipText = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color32(0x95, 0x95, 0x95, 0xFF) }, fontSize = 10, padding = new RectOffset(0, 0, 0, 0) };
            _hint = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color32(0xAE, 0xB4, 0xBC, 0xFF) }, fontSize = 11, clipping = TextClipping.Clip };
            _center = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperCenter, normal = { textColor = new Color32(0xC9, 0xD8, 0xEA, 0xFF) }, fontSize = 10 };
        }
        #endregion
    }
}
