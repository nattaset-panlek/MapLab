using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabBakeView
    {
        #region [Variables/Fields]
        private enum Step
        {
            Done,
            Todo,
            Waiting
        }

        private const float CardRadius = 6f;
        private const float BadgeSize = 20f;
        private const float ActionWidth = 104f;
        private const float ActionHeight = 22f;
        private const float ChipHeight = 18f;
        private const int ChipsShown = 5;
        private const float FieldLabelWidth = 72f;

        private Vector2 _scroll;
        private MapLabMap _scanned;
        private MapLabWalkableObstacles.Report _report;
        private MapLabElevationBaker.Report _heights;
        private bool _exact;
        private GUIStyle _card;
        private GUIStyle _badge;
        private GUIStyle _chip;
        private GUIStyle _stepTitle;
        #endregion

        #region [Public Methods]
        public void Refresh(MapLabMap map)
        {
            _scanned = map;
            _report = map != null ? MapLabWalkableObstacles.Scan(map) : null;
            _heights = map != null ? MapLabElevationBaker.Scan(map) : null;
        }

        public void Draw(Rect area, MapLabMap map, MapLabBakeStatus.Status status, Action changed)
        {
            if (!ReferenceEquals(_scanned, map) || _scanned == null || _report == null || _heights == null) Refresh(map);
            if (Event.current.type == EventType.Layout) MapLabWalkableScene.Track(map);
            BuildStyles();

            GUILayout.BeginArea(area);
            Rect header = MapLabWidgets.HeaderBar();
            MapLabWidgets.Icon(new Rect(header.x + 12f, header.y + 8f, 18f, 18f), MapLabIcons.Load(MapLabIcons.Map));
            float x = MapLabWidgets.Title(header, header.x + 38f, "Walkable area");
            MapLabWidgets.Status(new Rect(x, header.y, 140f, header.height), status.State);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 12, 10, 12) }))
            {
                DrawArea(map, changed);
                DrawObstacles(map);
                DrawHeights(map);
                DrawBake(map, status, changed);
                DrawGame(map);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public static void Run(Func<string> bake, Action changed)
        {
            try
            {
                Debug.Log(bake());
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException)
            {
                EditorUtility.DisplayDialog("MapLab", e.Message, "OK");
            }
            changed?.Invoke();
            GUIUtility.ExitGUI();
        }

        public static void Section(string title, string path)
        {
            GUILayout.Space(12f);
            MapLabWidgets.Caption(title, path);
        }

        public static void Settings(MapLabMap map, string[] properties)
        {
            if (map == null) return;
            var serialized = new SerializedObject(map);
            serialized.Update();
            foreach (var property in properties) EditorGUILayout.PropertyField(serialized.FindProperty(property));
            serialized.ApplyModifiedProperties();
        }
        #endregion

        #region [Private Methods]
        private void DrawArea(MapLabMap map, Action changed)
        {
            Rect bounds = map.Bounds;
            int outside = _report.Outside.Count;
            bool empty = bounds.width <= 0f || bounds.height <= 0f;
            if (BeginCard(1, "Area", empty || outside > 0 ? Step.Todo : Step.Done, "Fit to Map", false))
            {
                if (!MapLabWalkableArea.FitToMap(map)) EditorUtility.DisplayDialog("MapLab", "Nothing under this map to fit yet. Add shapes, sprites or colliders first.", "OK");
                Refresh(map);
                changed?.Invoke();
                GUIUtility.ExitGUI();
            }
            Vector2Int cells = MapLabWalkableArea.Cells(bounds, map.CellSize);
            GUILayout.Label($"{Range(bounds.xMin, bounds.xMax)} x {Range(bounds.yMin, bounds.yMax)} units, cell {map.CellSize:0.###} = {cells.x} x {cells.y} cells", MapLabWindowStyles.Row);
            if (outside > 0)
            {
                Warn($"{outside} {MapLabWalkableObstacles.Plural(outside, "collider")} reach outside the area. Press Fit to Map, or drag the blue frame in the Scene view.");
            }
            else
            {
                GUILayout.Label(MapLabWalkableScene.Show ? "Drag the blue frame in the Scene view to change it." : "Tick \"Show in the Scene view\" in step 4 to drag the frame.", MapLabWindowStyles.Wrap);
            }
            GUILayout.Space(4f);
            Fields(map, ("_cellSize", "Cell size"));
            _exact = EditorGUILayout.Foldout(_exact, "Exact numbers", true);
            if (_exact) Fields(map, ("_bounds", "Area"));
            EndCard();
        }

        private void DrawObstacles(MapLabMap map)
        {
            int count = _report.Blocking.Count;
            if (BeginCard(2, "What blocks walking", count > 0 ? Step.Done : Step.Todo, count > 0 ? "Select All" : null, false))
            {
                Selection.objects = _report.Blocking.Select(collider => (Object)collider.gameObject).Distinct().ToArray();
            }
            if (count == 0)
            {
                Warn("Nothing blocks walking yet. Add a Collider2D that is not a trigger under the map. Plateau and Ring shapes add one for you.");
            }
            else
            {
                GUILayout.Label($"{count} {MapLabWalkableObstacles.Plural(count, "collider")} under this map", MapLabWindowStyles.Row);
                Chips(_report.KindsByCount());
            }
            GUILayout.Space(4f);
            Fields(map, ("_obstacleLayers", "Layers"));
            GUILayout.Label(MapLabWalkableObstacles.Ignored(_report), MapLabWindowStyles.Wrap);
            EndCard();
        }

        private void DrawHeights(MapLabMap map)
        {
            int count = _heights.Surfaces.Count;
            int ramps = _heights.Ramps.Count;
            Step step = _heights.Overlaps.Count > 0 || _heights.DeadRamps.Count > 0 ? Step.Todo : Step.Done;
            if (BeginCard(3, "Walk on top", step, count > 0 ? "Select All" : null, false))
            {
                Selection.objects = _heights.Surfaces.Select(surface => (Object)surface.Shape.gameObject).ToArray();
            }
            if (count == 0)
            {
                GUILayout.Label("Optional. Tick Walk On Top on a Plateau to let characters stand on it. Until then the game sees every shape as a wall.", MapLabWindowStyles.Wrap);
            }
            else
            {
                string rampText = ramps == 0 ? "no ramps" : $"{ramps} {MapLabWalkableObstacles.Plural(ramps, "ramp")}";
                GUILayout.Label($"{count} {MapLabWalkableObstacles.Plural(count, "surface")} to stand on, {rampText}", MapLabWindowStyles.Row);
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var surface in _heights.Surfaces.Take(ChipsShown)) Chip($"{surface.Shape.name} {surface.Shape.TopElevation:0.##}");
                    if (count > ChipsShown) Chip($"+{count - ChipsShown}");
                    GUILayout.FlexibleSpace();
                }
                if (_heights.Overlaps.Count > 0) Warn($"Overlapping: {string.Join("; ", _heights.Overlaps.Take(2))}. The higher one wins where they overlap. Move them apart or make one a tier of the other.");
                if (_heights.Islands.Count > 0) GUILayout.Label($"Nothing leads up to {MapLabWalkableChanges.Names(_heights.Islands.Select(Leaf).ToList())} yet: it rises more than the step height and has no ramp. Select it, press Add Ramp in its Inspector and click an edge.", MapLabWindowStyles.Wrap);
            }
            if (_heights.DeadRamps.Count > 0) Warn($"{MapLabWalkableChanges.Names(_heights.DeadRamps.Select(Leaf).ToList())} {(_heights.DeadRamps.Count == 1 ? "leads" : "lead")} to a top that is not Walk On Top. Tick Walk On Top on its shape.");
            GUILayout.Space(4f);
            Fields(map, ("_stepHeight", "Step height"));
            GUILayout.Label("Rises up to the step height are walkable without a ramp.", MapLabWindowStyles.Wrap);
            EndCard();
        }

        private void DrawBake(MapLabMap map, MapLabBakeStatus.Status status, Action changed)
        {
            Step step = status.State == MapLabBakeStatus.State.UpToDate ? Step.Done : Step.Todo;
            string action = status.State == MapLabBakeStatus.State.NotBaked ? "Bake" : "Bake Again";
            if (BeginCard(4, "Bake", step, action, step == Step.Todo)) Run(() => MapLabWalkableBaker.Bake(map), changed);
            var summary = new GUIStyle(MapLabWindowStyles.Wrap) { normal = { textColor = MapLabWindowStyles.StateColor(status.State) } };
            GUILayout.Label(status.Summary ?? "", summary);
            GUILayout.Space(2f);
            bool show = EditorGUILayout.ToggleLeft("Show in the Scene view: the area frame and blocked cells in red", MapLabWalkableScene.Show, EditorStyles.miniLabel);
            if (show != MapLabWalkableScene.Show)
            {
                MapLabWalkableScene.Show = show;
                MapLabWalkableScene.Track(map);
            }
            EndCard();
        }

        private void DrawGame(MapLabMap map)
        {
            bool saved = !string.IsNullOrEmpty(map.gameObject.scene.path);
            BeginCard(5, "Use it in the game", map.WalkableData != null ? Step.Done : Step.Waiting, null, false);
            if (!saved) Warn("Save the scene first: the files go into a folder next to it.");
            FileLine(map.WalkableData, "TextAsset Icon");
            FileLine(map.WalkableMask, "Texture Icon");
            if (map.ElevationData != null || _heights.Any)
            {
                FileLine(map.ElevationData, "TextAsset Icon");
                FileLine(map.ElevationMask, "Texture Icon");
            }
            if (saved) GUILayout.Label(MapLabOutputPaths.MapFolder(map), MapLabWindowStyles.Dim);
            GUILayout.Label("Your game reads Walkable.json, a TextAsset with the cells, the outline and the obstacle holes. It does not need MapLab code. White in the mask is walkable.", MapLabWindowStyles.Wrap);
            if (map.ElevationData != null || _heights.Any)
            {
                GUILayout.Label("Walkable_Elevation.json adds the surface of every cell, its height, the cliffs and the sort bias of each surface. A game that ignores it keeps working with Walkable.json alone.", MapLabWindowStyles.Wrap);
            }
            EndCard();
        }

        private bool BeginCard(int number, string title, Step step, string action, bool primary)
        {
            Rect card = EditorGUILayout.BeginVertical(_card);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(card, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, MapLabWindowStyles.CardLine, 0f, CardRadius);
                GUI.DrawTexture(new RectOffset(1, 1, 1, 1).Remove(card), EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, MapLabWindowStyles.Card, 0f, CardRadius - 1f);
            }
            bool pressed = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect badge = GUILayoutUtility.GetRect(BadgeSize, BadgeSize, GUILayout.Width(BadgeSize), GUILayout.Height(BadgeSize));
                GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, StepColor(step), 0f, BadgeSize * 0.5f);
                GUI.Label(badge, number.ToString(), _badge);
                GUILayout.Space(6f);
                GUILayout.Label(title, _stepTitle, GUILayout.Height(BadgeSize));
                GUILayout.FlexibleSpace();
                if (action != null)
                {
                    GUIStyle style = primary ? MapLabWindowStyles.PrimaryButton : GUI.skin.button;
                    pressed = GUILayout.Button(action, style, GUILayout.Width(ActionWidth), GUILayout.Height(ActionHeight));
                }
            }
            GUILayout.Space(4f);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset((int)BadgeSize + 6, 0, 0, 0) });
            return pressed;
        }

        private static void EndCard()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void Chips(List<KeyValuePair<string, int>> kinds)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var kind in kinds.Take(ChipsShown)) Chip($"{kind.Key} {kind.Value}");
                int rest = kinds.Skip(ChipsShown).Sum(kind => kind.Value);
                if (rest > 0) Chip($"+{rest}");
                GUILayout.FlexibleSpace();
            }
        }

        private void Chip(string label)
        {
            var content = new GUIContent(label);
            float width = _chip.CalcSize(content).x + 14f;
            Rect chip = GUILayoutUtility.GetRect(width, ChipHeight, GUILayout.Width(width), GUILayout.Height(ChipHeight));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(chip, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, MapLabWindowStyles.Chip, 0f, ChipHeight * 0.5f);
            GUI.Label(chip, content, _chip);
            GUILayout.Space(4f);
        }

        private void Fields(MapLabMap map, params (string Property, string Label)[] fields)
        {
            var serialized = new SerializedObject(map);
            serialized.Update();
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FieldLabelWidth;
            foreach (var (property, label) in fields) EditorGUILayout.PropertyField(serialized.FindProperty(property), new GUIContent(label));
            EditorGUIUtility.labelWidth = labelWidth;
            if (serialized.ApplyModifiedProperties()) Refresh(map);
        }

        private static void Warn(string message)
        {
            GUILayout.Label(message, new GUIStyle(MapLabWindowStyles.Wrap) { normal = { textColor = MapLabWindowStyles.OutOfDate } });
        }

        private static Color StepColor(Step step)
        {
            switch (step)
            {
                case Step.Done:
                    return MapLabWindowStyles.UpToDate;
                case Step.Todo:
                    return MapLabWindowStyles.OutOfDate;
                default:
                    return MapLabWindowStyles.NotBaked;
            }
        }

        private static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        private static string Range(float min, float max)
        {
            return $"{min:0.##}..{max:0.##}";
        }

        private void BuildStyles()
        {
            _card ??= new GUIStyle { padding = new RectOffset(10, 10, 8, 10) };
            _badge ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color32(0x1B, 0x1B, 0x1B, 0xFF) } };
            _chip ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            _stepTitle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        }

        private static void FileLine(Object asset, string iconName)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            MapLabWidgets.FileRow(asset, iconName, Path.GetFileName(path ?? ""), MapLabOutputPaths.Size(MapLabOutputPaths.SizeOnDisk(path)));
        }
        #endregion
    }
}
