using System;
using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabLayerDetailsView
    {
        #region [Variables/Fields]
        public static readonly Color DecorateColor = new Color32(0x4F, 0xB3, 0xA9, 0xFF);
        public static readonly Color DecorateText = new Color32(0xBF, 0xE8, 0xE2, 0xFF);
        public static readonly Color DecorateFill = new Color32(0x20, 0x38, 0x33, 0xFF);
        private static readonly Color GroundColor = new Color32(0x5B, 0x8D, 0xEF, 0xFF);
        private static readonly Color SortedColor = new Color32(0x7B, 0x7F, 0xE6, 0xFF);
        private static readonly Color SetColor = new Color32(0x7B, 0x7F, 0xE6, 0xFF);
        private const int ShownMembers = 12;

        private string _nameText;
        private string _nameFor;
        #endregion

        #region [Properties]
        public string Renamed { get; private set; }
        #endregion

        #region [Public Methods]
        public void DrawNormal(MapLabMap map, MapLabBakeLayerSettings settings, MapLabBakeStatus.Status status, IReadOnlyDictionary<MapLabBakeKinds, int> counts, IReadOnlyDictionary<MapLabBakeKinds, int> totals, float width, Action changed)
        {
            bool baked = MapLabLayerBaker.BakedLayer(map, settings.Name) != null;
            DrawHeader(settings, settings.Sorted ? "Sorted layer" : "Normal layer", settings.Sorted ? SortedColor : GroundColor, () => DrawBakeButtons(map, settings, baked, changed), () => DrawLockButton(map, settings, changed));
            List<MapLabBakeKinds> held = MapLabLayerTypes.Held(map, settings);
            int objects = held.Sum(kind => counts != null && counts.TryGetValue(kind, out int count) ? count : 0);
            string line = settings.Sorted
                ? $"Bakes tall shapes into one picture per piece at their own sort point, so actors still walk in front and behind. Holds {objects} shapes."
                : held.Count == 0
                    ? "Draws under the actors. No object type is drawn into it yet: pick this layer in the table below."
                    : $"Draws under the actors. The {held.Count} highlighted object types below are drawn into it: {objects} objects.";
            GUILayout.Label(line, MapLabWindowStyles.Wrap);
            Summary(status);
            MapLabLayerTypesTable.Draw(map, settings, totals, width, changed);
            DrawLimitSets(map, settings, width, changed);
            DrawPaintWarning(map, settings, changed);
            DrawName(map, settings, changed);
            DrawFiles(map, settings, changed);
            if (!settings.Sorted) DrawFullRebake(map, settings, changed);
            DrawRemove(map, settings, changed);
        }

        public void DrawDecorate(MapLabMap map, MapLabBakeLayerSettings settings, MapLabBakeStatus.Status status, IReadOnlyDictionary<MapLabBakeKinds, int> counts, float width, Action changed)
        {
            bool baked = MapLabLayerBaker.BakedLayer(map, settings.Name) != null;
            DrawHeader(settings, "Decorate", DecorateColor, () => DrawBakeButtons(map, settings, baked, changed), () =>
            {
                using (new EditorGUI.DisabledScope(settings.Locked))
                {
                    if (!GUILayout.Button(new GUIContent("Move Above the Line", "Make it a normal layer again: its objects get their Y-sort and collider back"), GUILayout.Height(22f))) return;
                    MapLabLayerEdits.SetSide(map, settings, false);
                    changed();
                    GUIUtility.ExitGUI();
                }
            });
            GUILayout.Label("Background art. Everything in its sets draws over the floor, under the shadows and the actors.", MapLabWindowStyles.Wrap);
            Summary(status);

            MapLabBakeView.Section("FROM SETS", "everything in them");
            DrawSetChips(map, settings, width, changed, "+ Add Set");
            if (!settings.Sets.Any(set => set != null))
            {
                var warn = new GUIStyle(MapLabWindowStyles.Wrap) { normal = { textColor = MapLabWindowStyles.OutOfDate } };
                GUILayout.Label("Pick at least one set: a Decorate layer takes everything in its sets and nothing else.", warn);
            }

            MapLabBakeView.Section("WHAT HAPPENS TO THEM", null);
            Fact("Drawn", true, "over the floor, under the shadows and the actors");
            Fact("Y-sort", false, "they never hide the actors");
            Fact("Collider", false, "actors walk straight over them");
            Rect shadow = FactRow("Shadow");
            bool cast = GUI.Toggle(new Rect(shadow.x + 96f, shadow.y + 3f, 110f, 18f), settings.CastShadows, " Cast shadows");
            GUI.Label(new Rect(shadow.x + 210f, shadow.y, Mathf.Max(0f, shadow.width - 214f), shadow.height), cast ? "their own shadow, under the shadows of other plateaus" : "no shadow at all", MapLabWindowStyles.Dim);
            if (cast != settings.CastShadows)
            {
                MapLabLayerEdits.SetCastShadows(map, settings, cast);
                changed();
            }
            DrawMixedShadows(map, changed);

            DrawMembers(map, settings, counts);
            DrawName(map, settings, changed);
            DrawFiles(map, settings, changed);
            DrawRemove(map, settings, changed);
        }

        public void DrawPaint(MapLabMap map, MapLabLayerStack.Entry entry, int chunks, Action changed)
        {
            MapLabBakeView.Section($"PAINT  {entry.Name}", $"of {entry.Layer}");
            string where = MapLabLayerBaker.IsSorted(map, entry.Layer)
                ? "It is cut onto the pieces of that sorted layer, so it moves in front of and behind actors with them. Its place in the list only orders it among the paint of the same layer."
                : "Move it up or down to draw it above or below other layers.";
            GUILayout.Label($"Art drawn over '{entry.Layer}' in {chunks} chunks. Bakes never write here: change it by Export, paint, Import. {where}", MapLabWindowStyles.Wrap);
            string folder = MapLabLayerFiles.PaintFolder(map, entry.Layer, entry.Name);
            MapLabBakeView.Section("FILES", $"{MapLabLayerFiles.LayersFolderName}/{entry.Layer}/{MapLabLayerFiles.PaintFolderName}/");
            MapLabWidgets.FileRow(AssetDatabase.LoadAssetAtPath<Object>(folder), "Folder Icon", $"{entry.Name}/", $"{chunks} chunks");
            GUILayout.Space(8f);
            if (!GUILayout.Button(new GUIContent("Delete Paint Layer", "Moves its folder to the system trash"), EditorStyles.miniButton, GUILayout.Width(120f))) return;
            if (EditorUtility.DisplayDialog("MapLab", $"Move the paint layer '{entry.Name}' to the trash? You can restore it from the system trash.", "Move to Trash", "Cancel"))
            {
                MapLabLayerEdits.DeletePaint(map, entry.Layer, entry.Name);
            }
            changed();
            GUIUtility.ExitGUI();
        }

        public void ClearRenamed()
        {
            Renamed = null;
        }
        #endregion

        #region [Private Methods]
        private static void DrawHeader(MapLabBakeLayerSettings settings, string kind, Color color, Action buttons, Action extra)
        {
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                var title = new GUIContent(settings.Name, settings.Name);
                GUILayout.Label(title, MapLabWindowStyles.Title, GUILayout.Height(22f), GUILayout.MaxWidth(MapLabWindowStyles.Title.CalcSize(title).x));
                Rect chip = GUILayoutUtility.GetRect(MapLabWidgets.ChipWidth(kind), 18f, GUILayout.Width(MapLabWidgets.ChipWidth(kind)));
                chip.y += 2f;
                MapLabWidgets.Chip(chip, kind, color);
                if (settings.Locked) GUILayout.Label(new GUIContent(EditorGUIUtility.IconContent("InspectorLock").image, "Locked: bakes skip this layer"), GUILayout.Width(18f), GUILayout.Height(22f));
                GUILayout.FlexibleSpace();
                buttons();
                extra();
            }
        }

        private static void DrawBakeButtons(MapLabMap map, MapLabBakeLayerSettings settings, bool baked, Action changed)
        {
            using (new EditorGUI.DisabledScope(settings.Locked))
            {
                if (GUILayout.Button(baked ? "Rebake" : "Bake", MapLabWindowStyles.PrimaryButton, GUILayout.Width(70f), GUILayout.Height(22f)))
                {
                    MapLabBakeView.Run(() => MapLabLayerBaker.Bake(map, settings.Name), changed);
                }
                using (new EditorGUI.DisabledScope(!baked))
                {
                    if (GUILayout.Button(new GUIContent("Show Live", "Unbake this layer to edit its objects; its images stay on disk"), GUILayout.Width(76f), GUILayout.Height(22f)))
                    {
                        MapLabBakeView.Run(() => MapLabLayerBaker.Unbake(map, settings.Name), changed);
                    }
                }
            }
        }

        private static void DrawLockButton(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            var lockLabel = new GUIContent(settings.Locked ? "Unlock" : "Lock", settings.Locked ? "Let bakes write this layer again" : "Art owns this layer: bakes skip it");
            if (!GUILayout.Button(lockLabel, GUILayout.Width(60f), GUILayout.Height(22f))) return;
            bool unlock = settings.Locked;
            if (!unlock || EditorUtility.DisplayDialog("MapLab", $"Unlock '{settings.Name}'? The next bake rebuilds its images from the shapes and replaces anything flattened into them (older images stay in History~).", "Unlock", "Cancel"))
            {
                MapLabLayerEdits.SetLocked(map, settings, !settings.Locked);
            }
            changed();
            GUIUtility.ExitGUI();
        }

        private static void Summary(MapLabBakeStatus.Status status)
        {
            if (string.IsNullOrEmpty(status.Summary)) return;
            GUILayout.Label(status.Summary, new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = MapLabWindowStyles.StateColor(status.State) } });
        }

        private static Rect FactRow(string label)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(row.x, row.y + 1f, row.width, row.height - 2f), EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, MapLabWindowStyles.Card, 0f, 3f);
            GUI.Label(new Rect(row.x + 8f, row.y, 84f, row.height), label, MapLabWindowStyles.RowBold);
            return row;
        }

        private static void Fact(string label, bool on, string text)
        {
            Rect row = FactRow(label);
            var pill = new Rect(row.x + 96f, row.y + 4f, 34f, 16f);
            if (Event.current.type == EventType.Repaint)
            {
                Color fill = on ? new Color(DecorateColor.r, DecorateColor.g, DecorateColor.b, 0.3f) : MapLabWindowStyles.Chip;
                GUI.DrawTexture(pill, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, fill, 0f, 8f);
            }
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, normal = { textColor = on ? DecorateText : MapLabWindowStyles.DimText } };
            GUI.Label(pill, on ? "ON" : "OFF", style);
            GUI.Label(new Rect(row.x + 140f, row.y, Mathf.Max(0f, row.width - 144f), row.height), text, MapLabWindowStyles.Row);
        }

        private static void DrawMixedShadows(MapLabMap map, Action changed)
        {
            MapLabBakeLayerSettings mixed = MapLabLayerStack.Layers(map).FirstOrDefault(layer => !MapLabLayerStack.IsDecorate(map, layer.Name) && MapLabLayerStack.MixesShadows(layer));
            if (mixed == null) return;
            GUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = MapLabWindowStyles.OutOfDate } };
                GUILayout.Label($"Shadows bake together with the rest of '{mixed.Name}', so once baked they sit under the decor. Give the shadows a layer of their own.", style);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (!GUILayout.Button("Give Shadows Their Own Layer", EditorStyles.miniButton, GUILayout.Width(180f))) return;
                    Debug.Log(MapLabDrawLayers.EnsureShadowLayer(map));
                    MapLabDrawLayers.Sync(map);
                    MapLabLayerBaker.SyncScene(map);
                    changed();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DrawMembers(MapLabMap map, MapLabBakeLayerSettings settings, IReadOnlyDictionary<MapLabBakeKinds, int> counts)
        {
            List<Component> members = MapLabDrawLayers.Members(map, settings.Name);
            int others = counts == null ? 0 : counts.Where(pair => !MapLabLayerClaims.DecorateKinds.Contains(pair.Key)).Sum(pair => pair.Value);
            MapLabBakeView.Section($"OBJECTS  {members.Count}", "settings are kept, they come back above the line");
            if (members.Count == 0) GUILayout.Label("Nothing yet: plateaus and props in the sets above show up here.", MapLabWindowStyles.Dim);
            foreach (var member in members.Take(ShownMembers))
            {
                Rect row = MapLabWidgets.ListRow(false);
                bool shape = member is ExtrudedShape;
                MapLabWidgets.Icon(new Rect(row.x + 2f, row.y + 3f, 16f, 16f), shape ? MapLabIcons.Load(MapLabIcons.ExtrudedShape) : EditorGUIUtility.IconContent("Prefab Icon").image);
                GUI.Label(new Rect(row.x + 24f, row.y, row.width * 0.5f, row.height), member.gameObject.name, MapLabWindowStyles.Row);
                GUI.Label(new Rect(row.x + 24f + row.width * 0.5f, row.y, row.width * 0.5f - 60f, row.height), shape ? "Plateau shape" : "Prop", MapLabWindowStyles.Dim);
                if (!MapLabWidgets.Locate(new Rect(row.xMax - 20f, row.y + 3f, 16f, 16f), "Select it in the Hierarchy")) continue;
                Selection.activeGameObject = member.gameObject;
                EditorGUIUtility.PingObject(member.gameObject);
            }
            if (members.Count > ShownMembers) GUILayout.Label($"and {members.Count - ShownMembers} more", MapLabWindowStyles.Dim);
            if (others > 0) GUILayout.Label($"Plus {others} ground sprites, roads and flat shapes from the same sets.", MapLabWindowStyles.Dim);
        }

        private static void DrawLimitSets(MapLabMap map, MapLabBakeLayerSettings settings, float width, Action changed)
        {
            bool filtered = settings.Sets.Any(set => set != null);
            MapLabBakeView.Section("ONLY FROM SETS", filtered ? "only objects in these sets" : "optional: none = from everywhere");
            DrawSetChips(map, settings, width, changed, filtered ? "+ Add Set" : "+ Limit to a Set");
        }

        private static void DrawSetChips(MapLabMap map, MapLabBakeLayerSettings settings, float width, Action changed, string addLabel)
        {
            List<MapLabSet> chosen = settings.Sets.Where(set => set != null).ToList();
            var addContent = new GUIContent(addLabel);
            float addWidth = EditorStyles.miniBoldLabel.CalcSize(addContent).x + 8f;
            float limit = Mathf.Max(120f, width - 4f);
            var lines = new List<List<int>> { new List<int>() };
            float x = 0f;
            for (int i = 0; i <= chosen.Count; i++)
            {
                float w = i < chosen.Count ? MapLabWidgets.ChipWidth(chosen[i].name) + 14f : addWidth;
                if (x + w > limit && lines[lines.Count - 1].Count > 0)
                {
                    lines.Add(new List<int>());
                    x = 0f;
                }
                lines[lines.Count - 1].Add(i);
                x += w + 4f;
            }
            foreach (var line in lines)
            {
                Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                float cx = row.x;
                foreach (int i in line)
                {
                    if (i == chosen.Count)
                    {
                        var add = new Rect(cx, row.y + 2f, addWidth, 18f);
                        var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color32(0x8F, 0xB3, 0xF5, 0xFF) }, hover = { textColor = Color.white } };
                        if (GUI.Button(add, addContent, style)) AddSetMenu(map, settings, chosen, changed).DropDown(add);
                        continue;
                    }
                    MapLabSet set = chosen[i];
                    float w = MapLabWidgets.ChipWidth(set.name) + 14f;
                    var chip = new Rect(cx, row.y + 2f, w, 18f);
                    MapLabWidgets.Chip(chip, set.name, set.Color, "Click x to take this set out");
                    if (GUI.Button(new Rect(chip.xMax - 16f, chip.y + 1f, 14f, 16f), new GUIContent("x", $"Take '{set.name}' out"), MapLabWindowStyles.DimRight))
                    {
                        MapLabLayerEdits.SetSet(map, settings, set, false);
                        changed();
                        GUIUtility.ExitGUI();
                    }
                    cx += w + 4f;
                }
            }
        }

        private static GenericMenu AddSetMenu(MapLabMap map, MapLabBakeLayerSettings settings, List<MapLabSet> chosen, Action changed)
        {
            var menu = new GenericMenu();
            List<MapLabSet> sets = MapLabSets.SetsOf(map).Where(set => !chosen.Contains(set)).ToList();
            foreach (var set in sets)
            {
                MapLabSet target = set;
                menu.AddItem(new GUIContent(set.name), false, () =>
                {
                    MapLabLayerEdits.SetSet(map, settings, target, true);
                    changed?.Invoke();
                });
            }
            if (sets.Count == 0) menu.AddDisabledItem(new GUIContent(MapLabSets.SetsOf(map).Count == 0 ? "This map has no sets yet" : "Every set is already in"));
            return menu;
        }

        private static void DrawPaintWarning(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            if (settings.ChangedUnderPaint.Count == 0) return;
            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string chunks = string.Join("  ", settings.ChangedUnderPaint.Take(12).Select(index => $"{index.x},{index.y}"));
                string more = settings.ChangedUnderPaint.Count > 12 ? $" and {settings.ChangedUnderPaint.Count - 12} more" : "";
                var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = MapLabWindowStyles.OutOfDate } };
                GUILayout.Label($"The bake changed under paint in {settings.ChangedUnderPaint.Count} chunks: {chunks}{more}. The paint is safe, but check that it still fits (Export shows the chunk names on the Grid layer).", style);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Mark Reviewed", EditorStyles.miniButton, GUILayout.Width(100f)))
                    {
                        MapLabLayerEdits.MarkReviewed(map, settings);
                        changed();
                    }
                }
            }
        }

        private void DrawName(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            if (_nameFor != settings.Name)
            {
                _nameFor = settings.Name;
                _nameText = settings.Name;
            }
            MapLabBakeView.Section("NAME", null);
            using (new EditorGUILayout.HorizontalScope())
            {
                _nameText = EditorGUILayout.TextField(_nameText);
                using (new EditorGUI.DisabledScope(_nameText == settings.Name))
                {
                    if (!GUILayout.Button(new GUIContent("Rename", "Renames the folder and every paint layer that belongs to it"), EditorStyles.miniButton, GUILayout.Width(60f))) return;
                    string error = MapLabLayerEdits.Rename(map, settings.Name, _nameText);
                    if (error != null) MapLabLayersView.Show(error);
                    else Renamed = _nameText.Trim();
                    _nameFor = null;
                    changed();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DrawFiles(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            if (!MapLabLayerStack.Saved(map))
            {
                MapLabBakeView.Section("FILES", "save the scene first");
                return;
            }
            MapLabBakeView.Section("FILES", $"{MapLabLayerFiles.LayersFolderName}/{settings.Name}/");
            string baseFolder = MapLabLayerFiles.BaseFolder(map, settings.Name);
            MapLabWidgets.FileRow(AssetDatabase.LoadAssetAtPath<Object>(baseFolder), "Folder Icon", $"{MapLabLayerFiles.BaseFolderName}/", $"{MapLabLayerFiles.Chunks(baseFolder).Count} chunks");
            Object paintRoot = AssetDatabase.LoadAssetAtPath<Object>(MapLabLayerFiles.PaintRoot(map, settings.Name));
            int paints = MapLabLayerFiles.PaintNames(map, settings.Name).Count;
            if (paintRoot != null) MapLabWidgets.FileRow(paintRoot, "Folder Icon", $"{MapLabLayerFiles.PaintFolderName}/", paints == 1 ? "1 layer" : $"{paints} layers");
            else GUILayout.Label($"{MapLabLayerFiles.PaintFolderName}/  none yet: name a layer {settings.Name}_paint in your paint program", MapLabWindowStyles.Dim);
            int history = MapLabLayerFiles.HistoryCount(map, settings.Name);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{MapLabLayerFiles.HistoryFolderName}/  {history} older bakes kept (hidden from Unity)", MapLabWindowStyles.Dim);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(history == 0))
                {
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(48f))) EditorUtility.RevealInFinder(MapLabLayerFiles.HistoryRoot(map, settings.Name));
                }
            }
            bool baked = MapLabLayerBaker.BakedLayer(map, settings.Name) != null;
            using (new EditorGUI.DisabledScope(settings.Locked || !baked || MapLabLayerFlatten.Paints(map, settings.Name).Count == 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Merge the paint into the baked images and lock the layer.", MapLabWindowStyles.Dim);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Flatten", "Merge this layer's paint into its baked images, move the paint files to History~ and lock the layer"), EditorStyles.miniButton, GUILayout.Width(64f))) Flatten(map, settings, changed);
                }
            }
        }

        private static void DrawFullRebake(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Bake redraws only the chunks whose objects changed; empty chunks are never drawn.", MapLabWindowStyles.Dim);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(settings.Locked || MapLabLayerBaker.BakedLayer(map, settings.Name) == null))
                {
                    var content = new GUIContent("Rebake Every Chunk", "Ignore the change check and draw every chunk again, for example after changing render settings");
                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(130f))) MapLabBakeView.Run(() => MapLabLayerBaker.Bake(map, settings.Name, null, true), changed);
                }
            }
        }

        private static void DrawRemove(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(settings.Locked || map.BakeLayers.Count <= 1))
            {
                if (!GUILayout.Button(new GUIContent("Remove Layer", "Its objects show live again; its folder and paint stay on disk"), EditorStyles.miniButton, GUILayout.Width(100f))) return;
                if (EditorUtility.DisplayDialog("MapLab", $"Remove the layer '{settings.Name}'? Its objects show live again. Its folder and paint stay on disk.", "Remove", "Cancel"))
                {
                    MapLabLayersView.Show(MapLabLayerEdits.Remove(map, settings.Name));
                }
                changed();
                GUIUtility.ExitGUI();
            }
        }

        private static void Flatten(MapLabMap map, MapLabBakeLayerSettings settings, Action changed)
        {
            int paints = MapLabLayerFlatten.Paints(map, settings.Name).Count;
            string apart = MapLabLayerFlatten.PaintIsApart(map, settings.Name) ? " Some of its paint sits apart from it in the list; after flattening it all draws at the layer's place." : "";
            string message = $"Merge {paints} paint layers into '{settings.Name}' and lock it?{apart} The paint files move to History~, and bakes skip the layer until you unlock it.";
            if (EditorUtility.DisplayDialog("MapLab", message, "Flatten", "Cancel")) MapLabLayersView.Report(() => MapLabLayerFlatten.Flatten(map, settings.Name), true);
            changed();
            GUIUtility.ExitGUI();
        }
        #endregion
    }
}
