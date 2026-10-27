using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabPresetsView
    {
        #region [Variables/Fields]
        private const float FieldLabel = 84f;
        private const float ColumnGap = 14f;

        private static readonly (string Property, string Label)[] ShapeFields =
        {
            ("_height", "Height"), ("_rimWidth", "Rim Width"), ("_topColor", "Top Colour"), ("_wallColor", "Wall Colour"), ("_topTexture", "Top Texture"),
            ("_topTextureSize", "Top Size"), ("_wallTexture", "Wall Texture"), ("_wallTextureSize", "Wall Size"), ("_borderWidth", "Border"), ("_borderColor", "Border Colour"),
            ("_sortSlice", "Sort Slice"), ("_actorHeight", "Actor Height"), ("_castShadow", "Shadow"), ("_shadowColor", "Shadow Colour"), ("_shadowLength", "Shadow Length"),
            ("_sortingOrder", "Order"), ("_ySort", "Y Sort"), ("_generateCollision", "Collider"), ("_smoothness", "Smoothness"), ("_material", "Material")
        };

        private static readonly (string Property, string Label)[] PathFields =
        {
            ("_width", "Width"), ("_color", "Colour"), ("_texture", "Texture"), ("_textureLength", "Texture Length"), ("_sortingOrder", "Order"), ("_smoothness", "Smoothness"), ("_material", "Material")
        };

        private static readonly (string Property, string Label)[] MapFields =
        {
            ("_cellSize", "Cell Size"), ("_obstacleLayers", "Obstacles"), ("_actorSortingLayer", "Actor Layer"), ("_actorSortingOrder", "Actor Order"),
            ("_bakeChunkSize", "Chunk Size"), ("_bakePixelsPerUnit", "Pixels / Unit"), ("_megaChunkPixels", "Mega Chunk px")
        };

        private Vector2 _scroll;
        private MapLabPreset _selected;
        private Component _template;
        private MapLabPreset _templateFor;
        private string _nameText;
        private List<MapLabPreset> _presets = new List<MapLabPreset>();
        private Dictionary<MapLabPreset, List<Component>> _users = new Dictionary<MapLabPreset, List<Component>>();
        #endregion

        #region [Properties]
        public int Count => _presets.Count;
        #endregion

        #region [Public Methods]
        public void Refresh()
        {
            _presets = MapLabPresets.All();
            _users = _presets.ToDictionary(preset => preset, MapLabPresets.Users);
            if (_selected == null || !_presets.Contains(_selected)) _selected = _presets.FirstOrDefault();
        }

        public void Invalidate()
        {
            ReleaseTemplate();
        }

        public void Draw(Rect area, MapLabMap map, Action changed)
        {
            if (_presets.Any(preset => preset == null)) Refresh();
            GUILayout.BeginArea(area);
            DrawHeader(changed);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(0, 0, 4, 12) }))
            {
                if (_presets.Count == 0) DrawEmpty();
                foreach (var preset in _presets) DrawRow(preset);
                if (_selected != null)
                {
                    GUILayout.Space(8f);
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true)), MapLabWindowStyles.Line);
                    using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 12, 10, 4) })) DrawDetails(map, changed);
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public void Dispose()
        {
            ReleaseTemplate();
        }
        #endregion

        #region [Private Methods]
        private void DrawHeader(Action changed)
        {
            Rect header = MapLabWidgets.HeaderBar();
            var fromSelection = new Rect(header.xMax - 112f, header.y + 8f, 104f, 18f);
            if (GUI.Button(fromSelection, new GUIContent("From Selection", "Make a preset from the selected plateau, ring, box, flat shape, path or map"), EditorStyles.miniButton)) CreateFromSelection(changed);
            var folder = new Rect(fromSelection.x - 64f, header.y + 8f, 60f, 18f);
            if (GUI.Button(folder, new GUIContent("Folder", $"New presets go to {MapLabPresets.Folder}"), EditorStyles.miniButton)) PickFolder();
            MapLabGlyphs.Draw(new Rect(header.x + 12f, header.y + 9f, 16f, 16f), MapLabGlyph.Presets, Color.white);
            float x = MapLabWidgets.Title(header, header.x + 36f, "Presets", Mathf.Max(0f, folder.x - header.x - 60f));
            if (x + 30f < folder.x) GUI.Label(new Rect(x, header.y, 40f, header.height), _presets.Count.ToString(), MapLabWindowStyles.Dim);
        }

        private static void DrawEmpty()
        {
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 12, 8, 8) }))
            {
                GUILayout.Label("No presets yet", MapLabWindowStyles.Title);
                GUILayout.Label("Set up one plateau, ring, box, flat plaza, road or map the way you like, select it, and press From Selection. The preset remembers its settings (not its shape), so new objects start the same, and changing the preset can update every object made from it.", MapLabWindowStyles.Wrap);
            }
        }

        private void DrawRow(MapLabPreset preset)
        {
            bool selected = preset == _selected;
            Rect row = MapLabWidgets.ListRow(selected);
            MapLabWidgets.Icon(new Rect(row.x + 10f, row.y + 3f, 16f, 16f), KindIcon(preset.Kind));
            GUI.Label(new Rect(row.x + 32f, row.y, row.width - 120f, row.height), preset.name, selected ? MapLabWindowStyles.RowBold : MapLabWindowStyles.Row);
            GUI.Label(new Rect(row.xMax - 130f, row.y, 80f, row.height), preset.Kind.ToString(), MapLabWindowStyles.DimRight);
            GUI.Label(new Rect(row.xMax - 44f, row.y, 34f, row.height), Users(preset).Count.ToString(), MapLabWindowStyles.DimRight);
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !row.Contains(current.mousePosition)) return;
            if (_selected != preset) ReleaseTemplate();
            _selected = preset;
            _nameText = null;
            if (current.clickCount == 2) EditorGUIUtility.PingObject(preset);
            current.Use();
        }

        private void DrawDetails(MapLabMap map, Action changed)
        {
            MapLabPreset preset = _selected;
            List<Component> users = Users(preset);
            using (new EditorGUILayout.HorizontalScope())
            {
                MapLabWidgets.Icon(GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f)), KindIcon(preset.Kind));
                _nameText ??= preset.name;
                GUI.SetNextControlName("MapLabPresetName");
                _nameText = EditorGUILayout.TextField(_nameText, MapLabWindowStyles.NameField, GUILayout.Height(22f));
                bool commit = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "MapLabPresetName";
                using (new EditorGUI.DisabledScope(_nameText == preset.name || string.IsNullOrWhiteSpace(_nameText)))
                {
                    if (GUILayout.Button("Rename", EditorStyles.miniButton, GUILayout.Width(60f)) || (commit && _nameText != preset.name))
                    {
                        MapLabMegaInspector.Run(() =>
                        {
                            MapLabPresets.Rename(preset, _nameText);
                            _nameText = null;
                            return $"[MapLab] Renamed the preset to '{preset.name}'";
                        }, () =>
                        {
                            Refresh();
                            changed();
                        }, false);
                    }
                }
            }
            GUILayout.Label($"{preset.Kind} preset  ·  used by {users.Count} object{(users.Count == 1 ? "" : "s")} in the open scenes", MapLabWindowStyles.Dim);
            GUILayout.Space(8f);

            Component template = Template(preset);
            if (template == null)
            {
                EditorGUILayout.HelpBox("This preset lost its values. Select an object and press Update from Selection.", MessageType.Warning);
            }
            else
            {
                MapLabWidgets.Caption("SETTINGS");
                DrawFields(preset, template);
                if (preset.IsEnvironment) DrawEnvironment(preset);
            }

            GUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();
            bool sync = EditorGUILayout.ToggleLeft($"Keep the {users.Count} users in sync when I change this", preset.SyncUsers);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, "Sync MapLab Preset Users");
                preset.SyncUsers = sync;
                EditorUtility.SetDirty(preset);
                if (sync) MapLabPresets.Sync(preset);
            }
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Create in Scene", "A new object with these settings at the Scene view centre"), MapLabWindowStyles.PrimaryButton, GUILayout.Height(24f))) CreateInScene(preset, map, changed);
                if (GUILayout.Button(new GUIContent("Apply to Selection", "Copy these settings onto the selected objects of the same kind and link them to the preset"), GUILayout.Height(24f))) ApplyToSelection(preset, changed);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(users.Count == 0))
                {
                    if (GUILayout.Button("Select Users", GUILayout.Height(22f))) Selection.objects = users.Select(user => (Object)user.gameObject).ToArray();
                }
                if (GUILayout.Button(new GUIContent("Update from Selection", "Take the settings of the selected object into this preset"), GUILayout.Height(22f))) UpdateFromSelection(preset, changed);
                if (GUILayout.Button("Delete", GUILayout.Width(60f), GUILayout.Height(22f))) Delete(preset, changed);
            }
        }

        private void DrawFields(MapLabPreset preset, Component template)
        {
            (string Property, string Label)[] fields = preset.Kind == MapLabPresetKind.Environment ? MapFields : preset.Kind == MapLabPresetKind.Path ? PathFields : ShapeFields;
            var serialized = new SerializedObject(template);
            serialized.Update();
            bool hasRim = preset.Kind == MapLabPresetKind.Ring || preset.Kind == MapLabPresetKind.Void;
            List<SerializedProperty> properties = fields
                .Where(field => hasRim || field.Property != "_rimWidth")
                .Select(field => serialized.FindProperty(field.Property))
                .Where(property => property != null)
                .ToList();
            Dictionary<string, string> labels = fields.ToDictionary(field => field.Property, field => field.Label);
            float previous = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FieldLabel;
            EditorGUI.BeginChangeCheck();
            Rect probe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            float width = probe.width > 1f ? probe.width : EditorGUIUtility.currentViewWidth - 24f;
            bool twoColumns = width >= 380f;
            float column = twoColumns ? (width - ColumnGap) * 0.5f : width;
            for (int i = 0; i < properties.Count; i += twoColumns ? 2 : 1)
            {
                Rect line = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight + 2f, GUILayout.ExpandWidth(true));
                EditorGUI.PropertyField(new Rect(line.x, line.y, column, EditorGUIUtility.singleLineHeight), properties[i], new GUIContent(labels[properties[i].name]));
                if (twoColumns && i + 1 < properties.Count)
                {
                    EditorGUI.PropertyField(new Rect(line.x + column + ColumnGap, line.y, column, EditorGUIUtility.singleLineHeight), properties[i + 1], new GUIContent(labels[properties[i + 1].name]));
                }
            }
            EditorGUIUtility.labelWidth = previous;
            if (!EditorGUI.EndChangeCheck()) return;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MapLabPresets.Store(preset, template);
            int synced = MapLabPresets.Sync(preset);
            if (synced > 0) RefreshUsers(preset);
        }

        private static void DrawEnvironment(MapLabPreset preset)
        {
            GUILayout.Space(6f);
            MapLabWidgets.Caption("LAYERS AND FLOOR");
            string layers = preset.Layers.Count > 0 ? string.Join(", ", preset.Layers.Select(layer => layer.Sorted ? $"{layer.Name} (sorted)" : layer.Name)) : "none";
            GUILayout.Label($"Bake layers: {layers}. Applying adds missing layers and sets the kinds of the ones with the same name; sets and paint are never touched.", MapLabWindowStyles.Wrap);
            EditorGUI.BeginChangeCheck();
            var palette = (MapLabFloorPalette)EditorGUILayout.ObjectField(new GUIContent("Floor Palette"), preset.Palette, typeof(MapLabFloorPalette), false);
            int tiles = EditorGUILayout.IntSlider(new GUIContent("Tiles per Side"), preset.TilesPerSide, 8, 256);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(preset, "Edit MapLab Preset");
            preset.Palette = palette;
            preset.TilesPerSide = tiles;
            EditorUtility.SetDirty(preset);
            MapLabPresets.Sync(preset);
        }

        private Component Template(MapLabPreset preset)
        {
            if (_template != null && _templateFor == preset) return _template;
            ReleaseTemplate();
            _template = MapLabPresets.Template(preset);
            _templateFor = preset;
            return _template;
        }

        private void ReleaseTemplate()
        {
            MapLabPresets.Release(_template);
            _template = null;
            _templateFor = null;
        }

        private List<Component> Users(MapLabPreset preset)
        {
            if (!_users.TryGetValue(preset, out List<Component> users)) users = _users[preset] = MapLabPresets.Users(preset);
            return users.Where(user => user != null).ToList();
        }

        private void RefreshUsers(MapLabPreset preset)
        {
            _users[preset] = MapLabPresets.Users(preset);
        }

        private void CreateFromSelection(Action changed)
        {
            Component source = MapLabPresets.Source(Selection.activeGameObject);
            if (source == null)
            {
                EditorUtility.DisplayDialog("MapLab", "Select a plateau, ring, box, flat shape, path or MapLab map first.", "OK");
                GUIUtility.ExitGUI();
                return;
            }
            MapLabPreset preset = null;
            MapLabMegaInspector.Run(() =>
            {
                preset = MapLabPresets.Create(source, source.gameObject.name);
                return $"[MapLab] Made the {preset.Kind} preset '{preset.name}' from '{source.gameObject.name}' in {MapLabPresets.Folder}";
            }, () =>
            {
                Refresh();
                if (preset != null) _selected = preset;
                ReleaseTemplate();
                changed();
            }, false);
        }

        private void UpdateFromSelection(MapLabPreset preset, Action changed)
        {
            Component source = MapLabPresets.Source(Selection.activeGameObject);
            if (!MapLabPresets.Fits(preset, source))
            {
                EditorUtility.DisplayDialog("MapLab", $"Select a {MapLabPresets.TargetType(preset.Kind).Name} to update '{preset.name}' from.", "OK");
                GUIUtility.ExitGUI();
                return;
            }
            MapLabPresets.Capture(preset, source);
            MapLabPresets.Link(source, preset);
            int synced = MapLabPresets.Sync(preset);
            ReleaseTemplate();
            Debug.Log($"[MapLab] Preset '{preset.name}' now has the settings of '{source.gameObject.name}'{(synced > 0 ? $"; {synced} users updated" : "")}");
            RefreshUsers(preset);
            changed();
        }

        private void ApplyToSelection(MapLabPreset preset, Action changed)
        {
            int applied = 0;
            int skipped = 0;
            foreach (var go in Selection.gameObjects)
            {
                Component target = MapLabPresets.Source(go);
                if (MapLabPresets.Apply(preset, target)) applied++;
                else skipped++;
            }
            Debug.Log($"[MapLab] Applied preset '{preset.name}' to {applied} objects{(skipped > 0 ? $"; {skipped} were not a {MapLabPresets.TargetType(preset.Kind).Name}" : "")}");
            RefreshUsers(preset);
            changed();
        }

        private void CreateInScene(MapLabPreset preset, MapLabMap map, Action changed)
        {
            Vector3 position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            position.z = 0f;
            Component created = MapLabPresets.CreateInScene(preset, map, position);
            Selection.activeGameObject = created.gameObject;
            RefreshUsers(preset);
            changed();
        }

        private void Delete(MapLabPreset preset, Action changed)
        {
            if (!EditorUtility.DisplayDialog("MapLab", $"Move preset '{preset.name}' to the trash? Objects made from it keep their settings.", "Delete", "Cancel")) return;
            foreach (var user in Users(preset)) MapLabPresets.Unlink(user);
            ReleaseTemplate();
            AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(preset));
            _selected = null;
            Refresh();
            changed();
            GUIUtility.ExitGUI();
        }

        private static void PickFolder()
        {
            string absolute = EditorUtility.OpenFolderPanel("Save new MapLab presets in", MapLabPresets.Folder, "");
            if (string.IsNullOrEmpty(absolute)) return;
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            absolute = Path.GetFullPath(absolute).Replace('\\', '/');
            if (!absolute.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("MapLab", "Pick a folder inside this project's Assets folder.", "OK");
                return;
            }
            MapLabPresets.Folder = "Assets" + absolute.Substring(assets.Length);
        }

        private static Texture KindIcon(MapLabPresetKind kind)
        {
            switch (kind)
            {
                case MapLabPresetKind.Environment: return MapLabIcons.Load(MapLabIcons.Map);
                case MapLabPresetKind.Path: return MapLabIcons.Load(MapLabIcons.PathShape);
                default: return MapLabIcons.Load(MapLabIcons.ExtrudedShape);
            }
        }
        #endregion
    }
}
