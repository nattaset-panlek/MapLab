using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLab.Editor
{
    public class MapLabWindow : EditorWindow
    {
        #region [Variables/Fields]
        public enum Page
        {
            AllObjects,
            Set,
            Unassigned,
            Walkable,
            Layers,
            MegaChunks,
            FloorTiles,
            Presets
        }

        private const float LeftWidth = 188f;
        private const float StatusHeight = 24f;
        private const float FallbackToolbarHeight = 21f;
        private const double RefreshDelay = 0.4;

        private readonly MapLabSetView _setView = new MapLabSetView();
        private readonly MapLabBakeView _bakeView = new MapLabBakeView();
        private readonly MapLabLayersView _layersView = new MapLabLayersView();
        private readonly MapLabMegaView _megaView = new MapLabMegaView();
        private readonly MapLabFloorTilesView _floorTilesView = new MapLabFloorTilesView();
        private readonly MapLabPresetsView _presetsView = new MapLabPresetsView();
        private List<MapLabMap> _maps = new List<MapLabMap>();
        private int _mapIndex;
        private Page _page = Page.AllObjects;
        private MapLabSet _set;
        private string _search = "";
        private SearchField _searchField;
        private Vector2 _leftScroll;
        private MapLabBakeStatus.Status _walkable;
        private MapLabBakeStatus.Status _ground;
        private double _refreshAt;
        #endregion

        #region [Properties]
        private MapLabMap Map => _mapIndex >= 0 && _mapIndex < _maps.Count ? _maps[_mapIndex] : null;
        private static float ToolbarHeight => EditorStyles.toolbar.fixedHeight > 0f ? EditorStyles.toolbar.fixedHeight : FallbackToolbarHeight;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            titleContent = new GUIContent("MapLab", MapLabIcons.Load(MapLabIcons.Map));
            minSize = new Vector2(560f, 380f);
            wantsMouseMove = true;
            _searchField = new SearchField();
            ObjectChangeEvents.changesPublished += OnChanges;
            Selection.selectionChanged += Repaint;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            Undo.undoRedoPerformed += OnUndoRedo;
            RefreshMaps();
            RefreshStatus();
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnChanges;
            Selection.selectionChanged -= Repaint;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorApplication.update -= RefreshWhenDue;
            Undo.undoRedoPerformed -= OnUndoRedo;
            _megaView.Dispose();
            _presetsView.Dispose();
            MapLabWalkableScene.Track(null);
        }

        private void OnFocus()
        {
            RefreshMaps();
            RefreshStatus();
        }

        private void OnHierarchyChange()
        {
            RefreshMaps();
            QueueRefresh();
            Repaint();
        }

        private void OnGUI()
        {
            float top = ToolbarHeight;
            GUILayout.BeginArea(new Rect(0f, 0f, position.width, top));
            DrawToolbar();
            GUILayout.EndArea();
            MapLabMap map = Map;
            if (Event.current.type == EventType.Layout && (map == null || _page != Page.Walkable)) MapLabWalkableScene.Track(null);
            if (map == null)
            {
                GUILayout.BeginArea(new Rect(0f, top, position.width, position.height - top));
                DrawNoMap();
                GUILayout.EndArea();
                return;
            }
            if (_page == Page.Set && _set == null) _page = Page.AllObjects;

            float height = position.height - top - StatusHeight;
            var left = new Rect(0f, top, LeftWidth, height);
            var right = new Rect(LeftWidth + 1f, top, position.width - LeftWidth - 1f, height);
            EditorGUI.DrawRect(left, MapLabWindowStyles.Pane);
            EditorGUI.DrawRect(new Rect(LeftWidth, top, 1f, height), MapLabWindowStyles.Line);
            DrawLeft(left, map);

            if (_page == Page.Walkable)
            {
                _bakeView.Draw(right, map, _walkable, RefreshStatus);
            }
            else if (_page == Page.Layers)
            {
                _layersView.Draw(right, map, _ground, RefreshStatus);
            }
            else if (_page == Page.MegaChunks)
            {
                _megaView.Draw(right, map, Repaint, RefreshStatus, true);
            }
            else if (_page == Page.FloorTiles)
            {
                _floorTilesView.Draw(right, map, RefreshStatus);
            }
            else if (_page == Page.Presets)
            {
                _presetsView.Draw(right, map, RefreshStatus);
            }
            else
            {
                _setView.Draw(right, map, _page, _set, _search, Navigate);
            }
            DrawStatusBar(new Rect(0f, position.height - StatusHeight, position.width, StatusHeight), map);
        }
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/MapLab Window", false, 0)]
        public static void OpenFromMenu()
        {
            Open();
        }

        public static MapLabWindow Open()
        {
            var window = GetWindow<MapLabWindow>();
            window.Show();
            return window;
        }

        public static void Open(MapLabMap map, Page page)
        {
            MapLabWindow window = Open();
            window.RefreshMaps();
            window._mapIndex = Mathf.Max(0, window._maps.IndexOf(map));
            window._page = page;
            window.RefreshStatus();
        }
        #endregion

        #region [Private Methods]
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string[] names = _maps.Count > 0 ? _maps.Select(MapLabel).ToArray() : new[] { "No map in the open scenes" };
                int chosen = EditorGUILayout.Popup(Mathf.Clamp(_mapIndex, 0, names.Length - 1), names, EditorStyles.toolbarPopup, GUILayout.Width(180f));
                if (chosen != _mapIndex && _maps.Count > 0)
                {
                    _mapIndex = chosen;
                    _page = Page.AllObjects;
                    _set = null;
                    RefreshStatus();
                }
                GUILayout.Space(6f);
                _search = _searchField.OnToolbarGUI(_search, GUILayout.MinWidth(80f));
                GUILayout.Space(6f);
                using (new EditorGUI.DisabledScope(Map == null))
                {
                    if (GUILayout.Button("New Set", EditorStyles.toolbarButton, GUILayout.Width(64f))) NewSet();
                }
            }
        }

        private void DrawNoMap()
        {
            GUILayout.Space(24f);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(20, 20, 0, 0) }))
            {
                GUILayout.Label("No MapLab map in the open scenes", MapLabWindowStyles.Title);
                GUILayout.Label("A map is one root GameObject that holds the shapes, roads and props of one area, and the settings for its bakes.", MapLabWindowStyles.Wrap);
                GUILayout.Space(8f);
                if (GUILayout.Button("Create Map", MapLabWindowStyles.PrimaryButton, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    MapLabMap map = ExtrudedShapeMenu.CreateMap(null);
                    RefreshMaps();
                    _mapIndex = Mathf.Max(0, _maps.IndexOf(map));
                    RefreshStatus();
                }
            }
        }

        private void DrawLeft(Rect area, MapLabMap map)
        {
            GUILayout.BeginArea(area);
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            Caption("SETS");
            List<MapLabSet> sets = MapLabSets.SetsOf(map);
            LeftRow(null, "All objects", MapLabSets.AllObjects(map).Count.ToString(), null, _page == Page.AllObjects, () => Navigate(Page.AllObjects, null));
            foreach (var set in sets)
            {
                MapLabSet target = set;
                Rect row = LeftRow(null, set.name, MapLabSets.Members(set).Count.ToString(), set.Color, _page == Page.Set && _set == set, () => Navigate(Page.Set, target));
                MapLabSetView.AcceptDrop(row, set);
                Event current = Event.current;
                if (current.type == EventType.ContextClick && row.Contains(current.mousePosition))
                {
                    MapLabSetView.ShowSetMenu(map, set, _setView);
                    current.Use();
                }
            }
            LeftRow(null, "Unassigned", MapLabSets.Unassigned(map).Count.ToString(), MapLabSets.Palette[MapLabSets.Palette.Length - 1], _page == Page.Unassigned, () => Navigate(Page.Unassigned, null), true);

            GUILayout.Space(12f);
            Caption("BAKES");
            BakeRow(MapLabIcons.Load(MapLabIcons.Map), "Walkable area", _walkable.State, Page.Walkable);
            BakeRow(EditorGUIUtility.IconContent("Texture Icon").image, "Layers", _ground.State, Page.Layers);

            GUILayout.Space(12f);
            Caption("WORLD");
            MapLabWorldData world = map.World;
            LeftRow(MapLabGlyphs.Get(MapLabGlyph.MegaChunks), "Mega chunks", world != null ? world.Count.ToString() : "", null, _page == Page.MegaChunks, () => Navigate(Page.MegaChunks, null));
            LeftRow(MapLabGlyphs.Get(MapLabGlyph.FloorTiles), "Floor tiles", world != null && world.Palette != null ? world.Palette.Count.ToString() : "", null, _page == Page.FloorTiles, () => Navigate(Page.FloorTiles, null));

            GUILayout.Space(12f);
            Caption("LIBRARY");
            LeftRow(MapLabGlyphs.Get(MapLabGlyph.Presets), "Presets", _presetsView.Count.ToString(), null, _page == Page.Presets, () =>
            {
                _presetsView.Refresh();
                Navigate(Page.Presets, null);
            });
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Caption(string title)
        {
            GUILayout.Space(6f);
            MapLabWidgets.Caption(title);
        }

        private static Rect LeftRow(Texture icon, string label, string count, Color? tag, bool selected, System.Action click, bool muted = false)
        {
            Rect row = MapLabWidgets.ListRow(selected);
            if (tag.HasValue) MapLabWindowStyles.Tag(new Rect(row.x + 10f, row.y, 3f, row.height), tag.Value);
            float x = row.x + 20f;
            if (icon != null)
            {
                MapLabWidgets.Icon(new Rect(row.x + 10f, row.y + 3f, 16f, 16f), icon);
                x = row.x + 32f;
            }
            else if (!tag.HasValue && label != "All objects")
            {
                x = row.x + 32f;
            }
            GUIStyle style = selected ? MapLabWindowStyles.RowBold : muted ? MapLabWindowStyles.Dim : MapLabWindowStyles.Row;
            GUI.Label(new Rect(x, row.y, row.width - x - 36f, row.height), label, style);
            GUI.Label(new Rect(row.xMax - 40f, row.y, 30f, row.height), count, MapLabWindowStyles.DimRight);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && row.Contains(current.mousePosition))
            {
                click();
                current.Use();
            }
            return row;
        }

        private void BakeRow(Texture icon, string label, MapLabBakeStatus.State state, Page page)
        {
            Rect row = LeftRow(icon, label, "", null, _page == page, () => Navigate(page, null));
            MapLabWindowStyles.Dot(new Rect(row.xMax - 18f, row.y, 7f, row.height), MapLabWindowStyles.StateColor(state));
        }

        private void DrawStatusBar(Rect area, MapLabMap map)
        {
            EditorGUI.DrawRect(area, MapLabWindowStyles.Pane);
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), MapLabWindowStyles.Line);
            string message = "Everything is up to date";
            MapLabBakeStatus.State state = MapLabBakeStatus.State.UpToDate;
            string action = null;
            System.Func<string> bake = null;
            if (_walkable.State != MapLabBakeStatus.State.UpToDate)
            {
                state = _walkable.State;
                message = state == MapLabBakeStatus.State.NotBaked ? "Walkable area is not baked yet" : $"Walkable area: {_walkable.Summary}";
                action = "Bake";
                bake = () => MapLabWalkableBaker.Bake(map);
            }
            else if (_ground.State == MapLabBakeStatus.State.OutOfDate)
            {
                state = _ground.State;
                message = $"Layers: {_ground.Summary}";
                action = "Rebake";
                bake = () => MapLabStaticBaker.RebakeOutOfDate(map);
            }

            Color color = MapLabWindowStyles.StateColor(state);
            MapLabWindowStyles.Dot(new Rect(area.x + 10f, area.y, 7f, area.height), color);
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color }, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            GUI.Label(new Rect(area.x + 22f, area.y, area.width - 110f, area.height), message, style);
            if (action != null && GUI.Button(new Rect(area.xMax - 80f, area.y + 3f, 72f, area.height - 6f), action, MapLabWindowStyles.PrimaryButton))
            {
                MapLabBakeView.Run(bake, RefreshStatus);
            }
        }

        private void Navigate(Page page, MapLabSet set)
        {
            _page = page;
            _set = set;
            Repaint();
        }

        private void NewSet()
        {
            MapLabSet set = MapLabSets.CreateSet(Map, "New Set");
            Navigate(Page.Set, set);
            _setView.BeginRename(set);
        }

        private void RefreshMaps()
        {
            MapLabMap current = Map;
            _maps = new List<MapLabMap>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) _maps.AddRange(MapLabWalkableBaker.FindMaps(scene));
            }
            int index = current != null ? _maps.IndexOf(current) : -1;
            _mapIndex = index >= 0 ? index : 0;
        }

        private void RefreshStatus()
        {
            MapLabMap map = Map;
            if (map == null) return;
            _walkable = MapLabBakeStatus.Walkable(map);
            _bakeView.Refresh(map);
            _ground = _layersView.Refresh(map);
            _megaView.Refresh(map);
            _presetsView.Refresh();
            Repaint();
        }

        private void OnUndoRedo()
        {
            MapLabMap map = Map;
            if (map != null && map.World != null) MapLabWorldEdits.RebuildFloor(map);
            if (map != null) MapLabDrawLayers.Sync(map);
            _presetsView.Invalidate();
            QueueRefresh();
            Repaint();
        }

        private void OnChanges(ref ObjectChangeEventStream stream)
        {
            QueueRefresh();
        }

        private void QueueRefresh()
        {
            _refreshAt = EditorApplication.timeSinceStartup + RefreshDelay;
            EditorApplication.update -= RefreshWhenDue;
            EditorApplication.update += RefreshWhenDue;
        }

        private void RefreshWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _refreshAt) return;
            EditorApplication.update -= RefreshWhenDue;
            RefreshStatus();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            RefreshMaps();
            RefreshStatus();
        }

        private void OnSceneClosed(Scene scene)
        {
            RefreshMaps();
            RefreshStatus();
        }

        private string MapLabel(MapLabMap map)
        {
            return SceneManager.sceneCount > 1 ? $"{map.name} ({map.gameObject.scene.name})" : map.name;
        }
        #endregion
    }
}
