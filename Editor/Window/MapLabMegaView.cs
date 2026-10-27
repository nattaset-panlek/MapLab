using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabMegaView
    {
        #region [Variables/Fields]
        public const float ToolbarHeight = 30f;

        private const float InspectorWidth = 290f;
        private const float WideEnough = 700f;
        private const float ToolWidth = 26f;
        private const float ToolHeight = 22f;

        private static readonly (MapLabMegaTool Tool, MapLabGlyph Glyph, string Tip)[] Tools =
        {
            (MapLabMegaTool.Select, MapLabGlyph.Select, "Select (V): click a mega chunk, Shift adds, drag a box"),
            (MapLabMegaTool.Pan, MapLabGlyph.Pan, "Pan (H): drag to move. Middle drag pans with any tool"),
            (MapLabMegaTool.Add, MapLabGlyph.Add, "Add (A): click a + next to the map, or drag across several"),
            (MapLabMegaTool.Remove, MapLabGlyph.Remove, "Remove: click a mega chunk; its files stay on disk"),
            (MapLabMegaTool.Paint, MapLabGlyph.Paint, "Paint floor (B): Shift erases, Alt picks"),
            (MapLabMegaTool.Fill, MapLabGlyph.Fill, "Fill (G): click fills a chunk, Shift-click the mega chunk"),
            (MapLabMegaTool.Erase, MapLabGlyph.Erase, "Erase (E): tiles go back to the base tile"),
            (MapLabMegaTool.Pick, MapLabGlyph.Pick, "Pick (I): take the tile under the cursor")
        };

        private static readonly float[] ZoomSteps = { 1f, 5f, 10f, 25f, 50f, 100f, 200f, 400f };

        private readonly MapLabMegaState _state = new MapLabMegaState();
        private readonly MapLabMegaCanvas _canvas = new MapLabMegaCanvas();
        private readonly MapLabMegaInspector _inspector = new MapLabMegaInspector();
        #endregion

        #region [Properties]
        public MapLabMegaState State => _state;
        #endregion

        #region [Public Methods]
        public void Refresh(MapLabMap map)
        {
            _state.Refresh(map);
        }

        public void Draw(Rect area, MapLabMap map, Action repaint, Action changed, bool canPopOut)
        {
            if (!ReferenceEquals(_state.Map, map) || _state.Map == null) _state.Refresh(map);
            var header = new Rect(area.x, area.y, area.width, MapLabWidgets.HeaderHeight);
            DrawHeader(header, map, changed, canPopOut);
            var body = new Rect(area.x, header.yMax, area.width, area.height - header.height);
            if (map.World == null)
            {
                DrawStart(body, map, changed);
                return;
            }
            var toolbar = new Rect(body.x, body.y, body.width, ToolbarHeight);
            DrawToolbar(toolbar);
            float inspector = body.width >= WideEnough ? InspectorWidth : 0f;
            var canvas = new Rect(body.x, toolbar.yMax + 1f, body.width - inspector - (inspector > 0f ? 1f : 0f), body.height - ToolbarHeight - 1f);
            EditorGUI.DrawRect(new Rect(body.x, toolbar.yMax, body.width, 1f), MapLabWindowStyles.Line);
            _canvas.Draw(canvas, _state, repaint, changed);
            if (inspector <= 0f) return;
            EditorGUI.DrawRect(new Rect(canvas.xMax, canvas.y, 1f, canvas.height), MapLabWindowStyles.Line);
            _inspector.Draw(new Rect(canvas.xMax + 1f, canvas.y, inspector, canvas.height), _state, changed);
        }

        public void Dispose()
        {
            _state.Dispose();
        }
        #endregion

        #region [Private Methods]
        private void DrawHeader(Rect header, MapLabMap map, Action changed, bool canPopOut)
        {
            EditorGUI.DrawRect(header, MapLabWindowStyles.Header);
            EditorGUI.DrawRect(new Rect(header.x, header.yMax - 1f, header.width, 1f), MapLabWindowStyles.Line);
            float right = header.xMax - 8f;
            if (canPopOut)
            {
                right -= ToolWidth;
                if (ToolButton(new Rect(right, header.y + 6f, ToolWidth, ToolHeight), MapLabGlyph.PopOut, "Open the viewport in its own window (for a second screen)", false))
                {
                    MapLabWorldWindow.Open(map);
                }
                right -= 6f;
            }
            MapLabWorldData world = map.World;
            if (world != null)
            {
                const float exportWidth = 168f;
                right -= exportWidth;
                if (GUI.Button(new Rect(right, header.y + 8f, exportWidth, 18f), new GUIContent("Export PSD per Mega Chunk", "One PSD file per mega chunk, so Photoshop opens a 4096 px canvas instead of the whole map"), EditorStyles.miniButton))
                {
                    MapLabMegaInspector.ExportPsds(map, null, changed);
                }
                List<Vector2Int> stale = _state.WithState(MapLabBakeStatus.State.OutOfDate);
                const float bakeWidth = 132f;
                right -= bakeWidth + 4f;
                using (new EditorGUI.DisabledScope(stale.Count == 0))
                {
                    if (GUI.Button(new Rect(right, header.y + 8f, bakeWidth, 18f), new GUIContent($"Bake Out of Date ({stale.Count})", "Rebake only the mega chunks whose floor changed since their last bake"), EditorStyles.miniButton))
                    {
                        MapLabBakeView.Run(() => MapLabMegaStatus.Bake(map, stale), changed);
                    }
                }
            }
            float x = header.x + 12f;
            MapLabGlyphs.Draw(new Rect(x, header.y + 9f, 16f, 16f), MapLabGlyph.MegaChunks, Color.white);
            x = MapLabWidgets.Title(header, x + 24f, "Mega chunks", Mathf.Max(0f, right - x - 30f));
            if (world == null || x + 60f > right - 8f) return;
            RectInt extent = world.Extent();
            int pixels = map.ChunkPixels * map.ChunksPerMega;
            GUI.Label(new Rect(x, header.y, right - x - 8f, header.height), $"{world.Count} mega chunks  ·  {extent.width} x {extent.height}  ·  {pixels} x {pixels} px each", MapLabWindowStyles.Dim);
        }

        private void DrawToolbar(Rect bar)
        {
            EditorGUI.DrawRect(bar, MapLabWindowStyles.Toolbar);
            float x = bar.x + 8f;
            float y = bar.y + 4f;
            for (int i = 0; i < Tools.Length; i++)
            {
                if (i == 2 || i == 4)
                {
                    EditorGUI.DrawRect(new Rect(x + 4f, y + 3f, 1f, 16f), MapLabWindowStyles.Line);
                    x += 11f;
                }
                var (tool, glyph, tip) = Tools[i];
                if (ToolButton(new Rect(x, y, ToolWidth, ToolHeight), glyph, tip, _state.Tool == tool)) _state.Tool = tool;
                x += ToolWidth + 2f;
            }
            EditorGUI.DrawRect(new Rect(x + 4f, y + 3f, 1f, 16f), MapLabWindowStyles.Line);
            x += 11f;
            _state.ShowGrid = Toggle(ref x, y, "Grid", "Mega, chunk and tile grid lines", _state.ShowGrid);
            _state.ShowLabels = Toggle(ref x, y, "Labels", "Mega chunk and chunk coordinates", _state.ShowLabels);
            _state.ShowStates = Toggle(ref x, y, "States", "Bake state dot on each mega chunk", _state.ShowStates);

            float right = bar.xMax - 8f;
            right -= ToolWidth;
            if (ToolButton(new Rect(right, y, ToolWidth, ToolHeight), MapLabGlyph.Frame, "Frame the picked mega chunks (F)", false)) _state.FrameSelection();
            right -= ToolWidth + 2f;
            if (ToolButton(new Rect(right, y, ToolWidth, ToolHeight), MapLabGlyph.Fit, "Show the whole map (Home)", false)) _state.FrameAll();
            right -= ToolWidth + 8f;
            if (ToolButton(new Rect(right, y, ToolWidth, ToolHeight), MapLabGlyph.ZoomIn, "Zoom in", false)) _state.Camera.ZoomAt(_state.Camera.Screen.center, 1.5f, _state.MinScale, _state.MaxScale);
            right -= 56f;
            var zoom = new Rect(right, y, 54f, ToolHeight);
            if (right > x + 4f && GUI.Button(zoom, new GUIContent(Percent(_state.ZoomPercent), "Zoom: 100% shows the baked pixels 1:1"), MapLabWindowStyles.ZoomBox)) ZoomMenu().DropDown(zoom);
            right -= ToolWidth + 2f;
            if (right > x + 4f && ToolButton(new Rect(right, y, ToolWidth, ToolHeight), MapLabGlyph.ZoomOut, "Zoom out", false)) _state.Camera.ZoomAt(_state.Camera.Screen.center, 1f / 1.5f, _state.MinScale, _state.MaxScale);
        }

        private void DrawStart(Rect body, MapLabMap map, Action changed)
        {
            EditorGUI.DrawRect(body, MapLabWindowStyles.Window);
            GUILayout.BeginArea(new Rect(body.x + 20f, body.y + 20f, Mathf.Min(520f, body.width - 40f), body.height - 40f));
            GUILayout.Label("This map has no mega chunks yet", MapLabWindowStyles.Title);
            int pixels = map.ChunkPixels * map.ChunksPerMega;
            GUILayout.Label($"A mega chunk groups {map.ChunksPerMega} x {map.ChunksPerMega} chunks into one {pixels} x {pixels} px area. Start a world to grow the map one mega chunk at a time (like Terrain neighbours), lay floor tiles on it, and bake one folder and one PSD per mega chunk so Photoshop never opens the whole map at once.", MapLabWindowStyles.Wrap);
            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(!MapLabLayerStack.Saved(map)))
            {
                if (GUILayout.Button(new GUIContent("Create World", "Adds the world data, a floor palette and mega chunks over the map Bounds"), MapLabWindowStyles.PrimaryButton, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    MapLabMegaInspector.Run(() =>
                    {
                        MapLabWorldEdits.EnsureWorld(map);
                        string layer = MapLabWorldEdits.EnsureFloorLayer(map);
                        int added = MapLabWorldEdits.AddOverBounds(map, 1);
                        _state.Framed = false;
                        return $"[MapLab] Created the world of '{map.name}' with {added} mega chunks over its Bounds{(layer != null ? $". {layer}" : "")}";
                    }, changed, false);
                }
            }
            if (!MapLabLayerStack.Saved(map)) GUILayout.Label("Save the scene first: the world data lives in a folder next to it.", MapLabWindowStyles.Dim);
            GUILayout.EndArea();
        }

        private static bool ToolButton(Rect rect, MapLabGlyph glyph, string tip, bool active)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, active ? MapLabWindowStyles.SelectedRow : MapLabWindowStyles.ToolIdle, 0f, 3f);
                MapLabGlyphs.Draw(new Rect(rect.center.x - 8f, rect.center.y - 8f, 16f, 16f), glyph, active ? Color.white : MapLabWindowStyles.ToolGlyph);
            }
            return GUI.Button(rect, new GUIContent("", tip), GUIStyle.none);
        }

        private static bool Toggle(ref float x, float y, string label, string tip, bool on)
        {
            var content = new GUIContent(label, tip);
            float width = EditorStyles.miniLabel.CalcSize(content).x + 16f;
            var rect = new Rect(x, y + 1f, width, 20f);
            x += width + 2f;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, on ? MapLabWindowStyles.SelectedRow : MapLabWindowStyles.ToolIdle, 0f, 3f);
                GUI.Label(rect, content, on ? MapLabWindowStyles.ToggleOn : MapLabWindowStyles.ToggleOff);
            }
            return GUI.Button(rect, new GUIContent("", tip), GUIStyle.none) ? !on : on;
        }

        private GenericMenu ZoomMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Fit the whole map"), false, _state.FrameAll);
            menu.AddSeparator("");
            foreach (var step in ZoomSteps)
            {
                float value = step;
                menu.AddItem(new GUIContent($"{step:0}%"), Mathf.Abs(_state.ZoomPercent - step) < 0.5f, () => _state.SetZoomPercent(value));
            }
            return menu;
        }

        private static string Percent(float value)
        {
            if (value >= 10f) return $"{value:0}%";
            if (value >= 1f) return $"{value:0.#}%";
            return $"{value:0.##}%";
        }
        #endregion
    }
}
