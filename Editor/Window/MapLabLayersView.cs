using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabLayersView
    {
        #region [Variables/Fields]
        private static readonly string[] SettingsProperties = { "_actorSortingLayer", "_actorSortingOrder", "_bakeChunkSize", "_bakePixelsPerUnit", "_megaChunkPixels" };
        private const string FolderPreference = "MapLab.PaintExchangeFolder";
        private const string DefaultExchangeFolder = "MapLabPaint";
        private const float CompactWidth = 430f;
        private const float TwoColumnWidth = 720f;
        private const float PaintIndent = 16f;
        private const float DetailWidth = 74f;

        private Vector2 _scroll;
        private Vector2 _detailScroll;
        private string _selected;
        private Cache _cache;
        private readonly MapLabLayerDetailsView _details = new MapLabLayerDetailsView();

        private sealed class Cache
        {
            public MapLabMap Map;
            public List<MapLabLayerStack.Entry> Stack;
            public Dictionary<string, MapLabBakeStatus.Status> Statuses;
            public Dictionary<string, Dictionary<MapLabBakeKinds, int>> Kinds;
            public Dictionary<MapLabBakeKinds, int> Totals;
            public Dictionary<string, int> PaintChunks;
            public HashSet<string> Decorate;
            public int Unclaimed;
            public int Sorted;
            public int Line;
        }

        private GUIStyle _pill;
        #endregion

        #region [Public Methods]
        public MapLabBakeStatus.Status Refresh(MapLabMap map)
        {
            if (MapLabLayerStack.Upgrade(map)) MapLabDrawLayers.Sync(map);
            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out List<Renderer> unclaimed, out List<Renderer> unclaimedSorted);
            var cache = new Cache
            {
                Map = map,
                Stack = MapLabLayerStack.ResolveWithLine(map, true, out int line),
                Line = line,
                Decorate = new HashSet<string>(MapLabLayerStack.DecorateLayers(map).Select(layer => layer.Name), StringComparer.OrdinalIgnoreCase),
                Statuses = new Dictionary<string, MapLabBakeStatus.Status>(StringComparer.OrdinalIgnoreCase),
                Kinds = new Dictionary<string, Dictionary<MapLabBakeKinds, int>>(StringComparer.OrdinalIgnoreCase),
                Totals = MapLabLayerClaims.Kinds.ToDictionary(kind => kind, kind => 0),
                PaintChunks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                Unclaimed = unclaimed.Count,
                Sorted = MapLabLayerClaims.CountShapes(unclaimedSorted)
            };
            foreach (var layer in MapLabLayerStack.Layers(map))
            {
                cache.Statuses[layer.Name] = MapLabBakeStatus.Layer(map, layer, claims[layer.Name]);
                cache.Kinds[layer.Name] = KindCounts(map, layer, claims[layer.Name]);
                if (cache.Decorate.Contains(layer.Name)) continue;
                foreach (var kind in MapLabLayerClaims.Kinds) cache.Totals[kind] += cache.Kinds[layer.Name][kind];
            }
            Dictionary<MapLabBakeKinds, int> live = MapLabLayerClaims.CountKinds(map, unclaimed);
            foreach (var kind in MapLabLayerClaims.Kinds) cache.Totals[kind] += live[kind];
            cache.Totals[MapLabBakeKinds.SortedShapes] += cache.Sorted;
            foreach (var entry in cache.Stack.Where(entry => entry.IsPaint))
            {
                cache.PaintChunks[entry.Name] = MapLabLayerFiles.Chunks(MapLabLayerFiles.PaintFolder(map, entry.Layer, entry.Name)).Count;
            }
            _cache = cache;
            return MapLabBakeStatus.StaticGround(map, claims);
        }

        public void Draw(Rect area, MapLabMap map, MapLabBakeStatus.Status status, Action changed)
        {
            if (_cache == null || !ReferenceEquals(_cache.Map, map) || _cache.Map == null) Refresh(map);
            if (_details.Renamed != null)
            {
                _selected = _details.Renamed;
                _details.ClearRenamed();
            }
            SelectDefault();
            GUILayout.BeginArea(area);
            DrawHeader(map, status, changed, area.width);
            var padding = new GUIStyle { padding = new RectOffset(12, 12, 8, 12) };
            if (area.width >= TwoColumnWidth)
            {
                float left = Mathf.Clamp(area.width * 0.4f, 320f, 440f);
                float top = MapLabWidgets.HeaderHeight;
                var stackArea = new Rect(0f, top, left, area.height - top);
                var detailArea = new Rect(left + 1f, top, area.width - left - 1f, area.height - top);
                EditorGUI.DrawRect(new Rect(left, top, 1f, area.height - top), MapLabWindowStyles.Line);

                GUILayout.BeginArea(stackArea);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                using (new EditorGUILayout.VerticalScope(padding)) DrawStackColumn(map, status, changed);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();

                GUILayout.BeginArea(detailArea);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                using (new EditorGUILayout.VerticalScope(padding)) DrawDetails(map, detailArea.width - 40f, changed);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            else
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                using (new EditorGUILayout.VerticalScope(padding))
                {
                    DrawStackColumn(map, status, changed);
                    GUILayout.Space(10f);
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true)), MapLabWindowStyles.Line);
                    DrawDetails(map, area.width - 40f, changed);
                }
                EditorGUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        public static string ExchangeFolder()
        {
            string folder = EditorPrefs.GetString(FolderPreference, "");
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return folder;
            folder = Path.GetFullPath(DefaultExchangeFolder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static void Remember(string folder)
        {
            EditorPrefs.SetString(FolderPreference, folder);
        }

        public static bool Report(Func<string> action, bool always = false)
        {
            try
            {
                string message = action();
                Debug.Log(message);
                if (always || message.Contains("\n- ")) Show(message.Replace("[MapLab] ", ""));
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is InvalidDataException)
            {
                Show(e.Message);
                return false;
            }
        }

        public static void Show(string message)
        {
            if (!string.IsNullOrEmpty(message)) EditorUtility.DisplayDialog("MapLab", message, "OK");
        }
        #endregion

        #region [Private Methods]
        private void SelectDefault()
        {
            List<MapLabLayerStack.Entry> stack = _cache.Stack;
            if (_selected != null && stack.Any(entry => MapLabLayerStack.Same(entry.Name, _selected))) return;
            _selected = stack.Where(entry => !entry.IsPaint).Select(entry => entry.Name).LastOrDefault();
        }

        private void DrawHeader(MapLabMap map, MapLabBakeStatus.Status status, Action changed, float width)
        {
            Rect header = MapLabWidgets.HeaderBar();
            bool compact = width < CompactWidth;
            float menuWidth = compact ? 54f : 64f;
            float bakeWidth = compact ? 64f : 76f;

            float right = header.xMax - 8f - menuWidth;
            var import = new Rect(right, header.y + 8f, menuWidth, 18f);
            if (GUI.Button(import, new GUIContent("Import", "Bring paint back from a PSD or a folder of PNG files"), EditorStyles.miniPullDown)) ImportMenu(map, changed).DropDown(import);
            right -= menuWidth + 4f;
            var export = new Rect(right, header.y + 8f, menuWidth, 18f);
            if (GUI.Button(export, new GUIContent("Export", "Send every layer to a paint program as a PSD or PNG files"), EditorStyles.miniPullDown)) ExportMenu(map, changed).DropDown(export);
            right -= bakeWidth + 6f;
            if (GUI.Button(new Rect(right, header.y + 7f, bakeWidth, 20f), new GUIContent("Bake All", "Bake every unlocked layer"), MapLabWindowStyles.PrimaryButton))
            {
                MapLabBakeView.Run(() => MapLabStaticBaker.Bake(map), changed);
            }

            float x = header.x + 12f;
            MapLabWidgets.Icon(new Rect(x, header.y + 8f, 18f, 18f), EditorGUIUtility.IconContent("Texture Icon").image);
            x = MapLabWidgets.Title(header, x + 26f, "Layers", Mathf.Max(0f, right - x - 34f));
            if (x + 100f <= right - 6f) MapLabWidgets.Status(new Rect(x, header.y, 100f, header.height), status.State);
            else if (x + 8f <= right - 6f) MapLabWindowStyles.Dot(new Rect(x, header.y, 7f, header.height), MapLabWindowStyles.StateColor(status.State));
        }

        private void DrawStackColumn(MapLabMap map, MapLabBakeStatus.Status status, Action changed)
        {
            GUILayout.Label(status.Summary ?? "", new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = MapLabWindowStyles.StateColor(status.State) } });
            GUILayout.Space(6f);
            MapLabWidgets.Caption("STACK", "top draws last");
            List<MapLabLayerStack.Entry> stack = _cache.Stack;
            for (int i = stack.Count - 1; i >= _cache.Line; i--) DrawRow(map, stack, i, changed);
            DrawDecorateLine();
            if (_cache.Line == 0) DrawDecorateHint();
            for (int i = Mathf.Min(_cache.Line, stack.Count) - 1; i >= 0; i--) DrawRow(map, stack, i, changed);
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect add = GUILayoutUtility.GetRect(new GUIContent("Add Layer"), EditorStyles.miniPullDown, GUILayout.Width(84f));
                if (GUI.Button(add, new GUIContent("Add Layer", "A normal layer above the line"), EditorStyles.miniPullDown)) AddMenu(map, changed).DropDown(add);
                if (GUILayout.Button(new GUIContent("Add Decorate Layer", "A layer under the line: everything in the sets you pick becomes background art"), EditorStyles.miniButton, GUILayout.Width(128f)))
                {
                    _selected = MapLabLayerEdits.AddDecorate(map).Name;
                    changed();
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(8f);
            DrawLegend();
            MapLabBakeView.Section("SETTINGS", null);
            MapLabBakeView.Settings(map, SettingsProperties);
            GUILayout.Space(4f);
            GUILayout.Label($"{_cache.Sorted} tall shapes and {_cache.Unclaimed} ground objects are in no layer and stay live.", MapLabWindowStyles.Wrap);
        }

        private void DrawDecorateLine()
        {
            Rect row = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            _pill ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = MapLabLayerDetailsView.DecorateText } };
            Color line = MapLabLayerDetailsView.DecorateColor;
            EditorGUI.DrawRect(new Rect(row.x, row.center.y, row.width, 1f), new Color(line.r, line.g, line.b, 0.8f));
            float width = Mathf.Min(row.width - 8f, 250f);
            var pill = new Rect(row.center.x - width * 0.5f, row.y + 4f, width, 18f);
            GUI.DrawTexture(pill, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, MapLabLayerDetailsView.DecorateFill, 0f, 9f);
            GUI.DrawTexture(pill, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, line, 1f, 9f);
            GUI.Label(pill, new GUIContent("DECORATE  ·  shadows only, no collider", "Layers under this line are Decorate: everything in their sets is background art"), _pill);
        }

        private static void DrawDecorateHint()
        {
            Rect box = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            Color line = MapLabLayerDetailsView.DecorateColor;
            MapLabWidgets.DashedBox(new Rect(box.x + 2f, box.y + 2f, box.width - 4f, box.height - 4f), new Color(line.r, line.g, line.b, 0.6f));
            var style = new GUIStyle(MapLabWindowStyles.Wrap) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(box, "Move a layer under the line, or press Add Decorate Layer,\nto turn the sets you pick into background art.", style);
        }

        private static void DrawLegend()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                LegendLine(new Color32(0x5B, 0x8D, 0xEF, 0xFF), "Above the line: normal layers", "Each one draws the object types the table gives it.", null);
                LegendLine(MapLabLayerDetailsView.DecorateColor, "Below the line: Decorate", "Bakes whole sets as background art: shadow only, no collider, no Y-sort.", MapLabLayerDetailsView.DecorateText);
            }
        }

        private static void LegendLine(Color bar, string title, string text, Color? titleColor)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect tag = GUILayoutUtility.GetRect(4f, 30f, GUILayout.Width(4f));
                EditorGUI.DrawRect(new Rect(tag.x, tag.y + 3f, 3f, tag.height - 6f), bar);
                using (new EditorGUILayout.VerticalScope())
                {
                    var style = new GUIStyle(EditorStyles.miniBoldLabel);
                    if (titleColor.HasValue && EditorGUIUtility.isProSkin) style.normal.textColor = titleColor.Value;
                    GUILayout.Label(title, style);
                    GUILayout.Label(text, MapLabWindowStyles.Wrap);
                }
            }
        }

        private void DrawRow(MapLabMap map, List<MapLabLayerStack.Entry> stack, int index, Action changed)
        {
            MapLabLayerStack.Entry entry = stack[index];
            bool selected = MapLabLayerStack.Same(entry.Name, _selected);
            bool decorate = _cache.Decorate.Contains(entry.Layer);
            Rect row = GUILayoutUtility.GetRect(0f, MapLabWindowStyles.RowHeight, GUILayout.ExpandWidth(true));
            if (selected) EditorGUI.DrawRect(row, MapLabWindowStyles.SelectedRow);
            else if (decorate) EditorGUI.DrawRect(row, new Color(MapLabLayerDetailsView.DecorateColor.r, MapLabLayerDetailsView.DecorateColor.g, MapLabLayerDetailsView.DecorateColor.b, 0.08f));
            Color tint = selected ? Color.white : GUI.skin.label.normal.textColor;

            GameObject target = SceneObject(map, entry);
            if (target != null)
            {
                bool hidden = SceneVisibilityManager.instance.IsHidden(target);
                string tooltip = hidden ? "Show in the Scene view" : "Hide in the Scene view only (the game still draws it)";
                if (MapLabWindowStyles.IconButton(new Rect(row.x + 4f, row.y + 3f, 16f, 16f), EditorGUIUtility.IconContent(hidden ? "scenevis_hidden" : "scenevis_visible").image, tooltip, tint))
                {
                    SceneVisibilityManager.instance.ToggleVisibility(target, true);
                }
            }

            MapLabBakeLayerSettings settings = entry.IsPaint ? null : MapLabLayerStack.Find(map, entry.Name);
            bool sorted = settings != null && settings.Sorted;
            float right = row.xMax - 4f;
            int position = index + (index >= _cache.Line ? 1 : 0);
            bool downCrosses = index == _cache.Line;
            if (position > 0 && !(sorted && downCrosses) && MapLabWindowStyles.IconButton(new Rect(right - 16f, row.y + 3f, 16f, 16f), MapLabWindowStyles.ArrowDown, downCrosses ? "Move under the line: it becomes Decorate" : "Move down (drawn earlier)", tint))
            {
                MapLabLayerEdits.Move(map, entry.Name, -1);
                changed();
                GUIUtility.ExitGUI();
            }
            right -= 18f;
            bool upCrosses = index == _cache.Line - 1;
            if (position < stack.Count && MapLabWindowStyles.IconButton(new Rect(right - 16f, row.y + 3f, 16f, 16f), MapLabWindowStyles.ArrowUp, upCrosses ? "Move above the line: it becomes a normal layer" : "Move up (drawn later)", tint))
            {
                MapLabLayerEdits.Move(map, entry.Name, 1);
                changed();
                GUIUtility.ExitGUI();
            }
            right -= 22f;

            string detail;
            Color color;
            if (entry.IsPaint)
            {
                detail = $"{_cache.PaintChunks[entry.Name]} chunks";
                color = MapLabWindowStyles.DimText;
            }
            else
            {
                MapLabBakeStatus.Status status = _cache.Statuses[entry.Name];
                detail = status.State == MapLabBakeStatus.State.NotBaked ? "Live" : MapLabWindowStyles.StateLabel(status.State);
                color = MapLabWindowStyles.StateColor(status.State);
            }
            var style = new GUIStyle(MapLabWindowStyles.DimRight) { normal = { textColor = selected ? Color.white : color } };
            GUI.Label(new Rect(right - DetailWidth, row.y, DetailWidth, row.height), detail, style);
            right -= DetailWidth + 4f;
            if (settings != null && settings.Locked)
            {
                MapLabWindowStyles.IconButton(new Rect(right - 16f, row.y + 3f, 16f, 16f), EditorGUIUtility.IconContent("InspectorLock").image, "Locked: bakes skip this layer", tint);
                right -= 20f;
            }

            float x = row.x + 24f + (entry.IsPaint ? PaintIndent : 0f);
            EditorGUI.DrawRect(new Rect(x, row.y + 4f, 3f, row.height - 8f), LayerColor(entry, settings, decorate));
            x += 8f;
            var name = new GUIContent(entry.Name, entry.IsPaint ? $"Paint of '{entry.Layer}'" : decorate ? "Decorate layer" : sorted ? "Sorted layer (draws with the actors, not by this list)" : "Normal layer");
            float nameWidth = Mathf.Min((selected ? MapLabWindowStyles.RowBold : MapLabWindowStyles.Row).CalcSize(name).x, Mathf.Max(0f, right - x - 4f));
            GUI.Label(new Rect(x, row.y, nameWidth, row.height), name, selected ? MapLabWindowStyles.RowBold : MapLabWindowStyles.Row);
            x += nameWidth + 8f;
            string note = entry.IsPaint ? "paint" : decorate ? SetNames(settings) : sorted ? "tall shapes, Y-sort" : TypeNote(map, settings);
            if (right - x > 30f) GUI.Label(new Rect(x, row.y, right - x - 4f, row.height), note, selected ? new GUIStyle(MapLabWindowStyles.Dim) { normal = { textColor = new Color(0.79f, 0.85f, 0.92f) } } : MapLabWindowStyles.Dim);

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !row.Contains(current.mousePosition)) return;
            _selected = entry.Name;
            if (current.clickCount == 2 && target != null)
            {
                Selection.activeGameObject = target;
                EditorGUIUtility.PingObject(target);
            }
            current.Use();
        }

        private void DrawDetails(MapLabMap map, float width, Action changed)
        {
            int index = _cache.Stack.FindIndex(entry => MapLabLayerStack.Same(entry.Name, _selected));
            if (index < 0) return;
            MapLabLayerStack.Entry selected = _cache.Stack[index];
            if (selected.IsPaint)
            {
                _details.DrawPaint(map, selected, _cache.PaintChunks[selected.Name], changed);
                return;
            }
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, selected.Name);
            if (settings == null) return;
            MapLabBakeStatus.Status status = _cache.Statuses.TryGetValue(settings.Name, out MapLabBakeStatus.Status found) ? found : default;
            Dictionary<MapLabBakeKinds, int> counts = _cache.Kinds.TryGetValue(settings.Name, out var kinds) ? kinds : null;
            if (_cache.Decorate.Contains(settings.Name)) _details.DrawDecorate(map, settings, status, counts, width, changed);
            else _details.DrawNormal(map, settings, status, counts, _cache.Totals, width, changed);
        }

        private static Color LayerColor(MapLabLayerStack.Entry entry, MapLabBakeLayerSettings settings, bool decorate)
        {
            Color color = decorate ? MapLabLayerDetailsView.DecorateColor : settings != null && settings.Sorted ? new Color32(0x7B, 0x7F, 0xE6, 0xFF) : new Color32(0x5B, 0x8D, 0xEF, 0xFF);
            return entry.IsPaint ? new Color(color.r, color.g, color.b, 0.55f) : color;
        }

        private static string SetNames(MapLabBakeLayerSettings settings)
        {
            List<string> names = settings.Sets.Where(set => set != null).Select(set => set.name).ToList();
            return names.Count == 0 ? "no set yet" : string.Join(", ", names);
        }

        private static string TypeNote(MapLabMap map, MapLabBakeLayerSettings settings)
        {
            List<MapLabBakeKinds> held = MapLabLayerTypes.Held(map, settings);
            return held.Count == 0 ? "nothing drawn into it" : string.Join(", ", held.Select(kind => MapLabLayerClaims.KindLabel(kind).ToLowerInvariant()));
        }

        private GenericMenu AddMenu(MapLabMap map, Action changed)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Normal layer"), false, () =>
            {
                _selected = MapLabLayerEdits.Add(map).Name;
                changed();
            });
            menu.AddItem(new GUIContent("Sorted layer (tall shapes, Y-sort)"), false, () =>
            {
                _selected = MapLabLayerEdits.Add(map, true).Name;
                changed();
            });
            return menu;
        }

        private static GenericMenu ExportMenu(MapLabMap map, Action changed)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("PSD per Mega Chunk (folder)..."), false, () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Export one PSD per mega chunk into", ExchangeFolder(), "");
                if (string.IsNullOrEmpty(folder)) return;
                Remember(folder);
                if (Report(() => MapLabPaintExchange.ExportMegaPsds(map, folder))) EditorUtility.RevealInFinder(folder);
                changed();
            });
            menu.AddItem(new GUIContent("One PSD for the Whole Map..."), false, () =>
            {
                string path = EditorUtility.SaveFilePanel("Export layers for painting", ExchangeFolder(), $"{MapLabOutputPaths.MapFolderName(map)}.psd", "psd");
                if (string.IsNullOrEmpty(path)) return;
                Remember(Path.GetDirectoryName(path));
                if (Report(() => MapLabPaintExchange.ExportPsd(map, path))) EditorUtility.RevealInFinder(path);
                changed();
            });
            menu.AddItem(new GUIContent("PNG File per Layer (folder)..."), false, () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Export one PNG per layer into", ExchangeFolder(), "");
                if (string.IsNullOrEmpty(folder)) return;
                Remember(folder);
                if (Report(() => MapLabPaintExchange.ExportPng(map, folder))) EditorUtility.RevealInFinder(folder);
                changed();
            });
            return menu;
        }

        private static GenericMenu ImportMenu(MapLabMap map, Action changed)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Folder of Mega Chunk PSDs..."), false, () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Import paint from every PSD in", ExchangeFolder(), "");
                if (string.IsNullOrEmpty(folder)) return;
                Remember(folder);
                Report(() => MapLabPaintExchange.ImportPsdFolder(map, folder), true);
                changed();
            });
            menu.AddItem(new GUIContent("Photoshop File (.psd)..."), false, () =>
            {
                string path = EditorUtility.OpenFilePanel("Import paint layers", ExchangeFolder(), "psd");
                if (string.IsNullOrEmpty(path)) return;
                Remember(Path.GetDirectoryName(path));
                Report(() => MapLabPaintExchange.ImportPsd(map, path), true);
                changed();
            });
            menu.AddItem(new GUIContent("PNG Folder..."), false, () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Import paint layers from", ExchangeFolder(), "");
                if (string.IsNullOrEmpty(folder)) return;
                Remember(folder);
                Report(() => MapLabPaintExchange.ImportPng(map, folder), true);
                changed();
            });
            return menu;
        }

        private static GameObject SceneObject(MapLabMap map, MapLabLayerStack.Entry entry)
        {
            if (entry.IsPaint)
            {
                MapLabChunkLayer paint = MapLabLayerBaker.PaintLayer(map, entry.Name);
                return paint != null ? paint.gameObject : null;
            }
            MapLabBakedLayer baked = MapLabLayerBaker.BakedLayer(map, entry.Name);
            return baked != null ? baked.gameObject : null;
        }

        private static Dictionary<MapLabBakeKinds, int> KindCounts(MapLabMap map, MapLabBakeLayerSettings layer, List<Renderer> live)
        {
            Dictionary<MapLabBakeKinds, int> counts = MapLabLayerClaims.CountKinds(map, live);
            MapLabBakedLayer baked = MapLabLayerBaker.BakedLayer(map, layer.Name);
            if (baked == null) return counts;
            foreach (var renderer in baked.HiddenRenderers.Where(renderer => renderer != null)) counts[MapLabLayerClaims.KindOf(renderer, map)]++;
            foreach (var shape in baked.BakedVisuals.Where(shape => shape != null))
            {
                MapLabBakeKinds kind = baked.IsSorted ? MapLabBakeKinds.SortedShapes : shape is PathShape ? MapLabBakeKinds.Paths : shape is ExtrudedShape extruded && extruded.DrawsAsGround ? MapLabBakeKinds.PulledShapes : MapLabBakeKinds.FlatShapes;
                counts[kind]++;
            }
            counts[MapLabBakeKinds.ShapeShadows] += baked.BakedShadows.Count(shape => shape != null);
            counts[MapLabBakeKinds.Floor] += baked.BakedFloors.Where(floor => floor != null).Sum(floor => floor.GetComponentsInChildren<MeshRenderer>(true).Length);
            return counts;
        }
        #endregion
    }
}
