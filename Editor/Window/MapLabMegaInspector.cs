using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabMegaInspector
    {
        #region [Variables/Fields]
        private const float SwatchWidth = 42f;
        private const float SwatchHeight = 52f;

        private Vector2 _scroll;
        #endregion

        #region [Public Methods]
        public void Draw(Rect area, MapLabMegaState state, Action changed)
        {
            EditorGUI.DrawRect(area, MapLabWindowStyles.Window);
            GUILayout.BeginArea(area);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 12, 10, 12) }))
            {
                EditorGUIUtility.labelWidth = 104f;
                if (state.IsFloorTool) DrawFloor(state, changed);
                else if (state.Tool == MapLabMegaTool.Add) DrawAdd(state, changed);
                else if (state.Selection.Count > 0) DrawSelection(state, changed);
                else DrawOverview(state, changed);
                EditorGUIUtility.labelWidth = 0f;
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public static void Run(Func<string> action, Action changed, bool show)
        {
            try
            {
                string message = action();
                Debug.Log(message);
                if (show || message.Contains("\n- ")) EditorUtility.DisplayDialog("MapLab", message.Replace("[MapLab] ", ""), "OK");
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is InvalidDataException)
            {
                EditorUtility.DisplayDialog("MapLab", e.Message, "OK");
            }
            changed?.Invoke();
            GUIUtility.ExitGUI();
        }

        public static void ExportPsds(MapLabMap map, ICollection<Vector2Int> megas, Action changed)
        {
            string folder = EditorUtility.OpenFolderPanel("Export one PSD per mega chunk into", MapLabLayersView.ExchangeFolder(), "");
            if (string.IsNullOrEmpty(folder)) return;
            MapLabLayersView.Remember(folder);
            Run(() =>
            {
                string message = MapLabPaintExchange.ExportMegaPsds(map, folder, megas);
                EditorUtility.RevealInFinder(folder);
                return message;
            }, changed, false);
        }

        public static void ImportPsds(MapLabMap map, Action changed)
        {
            string folder = EditorUtility.OpenFolderPanel("Import paint from every PSD in", MapLabLayersView.ExchangeFolder(), "");
            if (string.IsNullOrEmpty(folder)) return;
            MapLabLayersView.Remember(folder);
            Run(() => MapLabPaintExchange.ImportPsdFolder(map, folder), changed, true);
        }

        public static byte TilePopup(string label, MapLabFloorPalette palette, byte value)
        {
            var names = new List<string> { "None" };
            if (palette != null) names.AddRange(palette.Tiles.Select((tile, index) => $"{index + 1}  {tile.Name}"));
            int chosen = EditorGUILayout.Popup(label, Mathf.Clamp(value, 0, names.Count - 1), names.ToArray());
            return (byte)chosen;
        }
        #endregion

        #region [Private Methods]
        private void DrawOverview(MapLabMegaState state, Action changed)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            GUILayout.Label("Mega chunks", MapLabWindowStyles.Title);
            RectInt extent = world.Extent();
            GUILayout.Label($"{world.Count} mega chunks, {extent.width} x {extent.height}", MapLabWindowStyles.Dim);
            GUILayout.Space(6f);
            MapLabWidgets.Caption("STATE");
            var counts = world.Megas.GroupBy(mega => MapLabMegaStatus.Of(mega, state.Baked)).ToDictionary(group => group.Key, group => group.Count());
            foreach (MapLabBakeStatus.State value in new[] { MapLabBakeStatus.State.UpToDate, MapLabBakeStatus.State.OutOfDate, MapLabBakeStatus.State.NotBaked })
            {
                Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                MapLabWidgets.Status(new Rect(row.x + 4f, row.y, 160f, row.height), value);
                GUI.Label(new Rect(row.xMax - 60f, row.y, 56f, row.height), counts.TryGetValue(value, out int count) ? count.ToString() : "0", MapLabWindowStyles.DimRight);
            }
            GUILayout.Space(4f);
            List<Vector2Int> stale = state.WithState(MapLabBakeStatus.State.OutOfDate);
            List<Vector2Int> fresh = state.WithState(MapLabBakeStatus.State.NotBaked);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(stale.Count == 0))
                {
                    if (GUILayout.Button($"Bake Out of Date ({stale.Count})", stale.Count > 0 ? MapLabWindowStyles.PrimaryButton : GUI.skin.button, GUILayout.Height(22f)))
                    {
                        MapLabBakeView.Run(() => MapLabMegaStatus.Bake(map, stale), changed);
                    }
                }
                using (new EditorGUI.DisabledScope(fresh.Count == 0))
                {
                    if (GUILayout.Button($"Bake Not Baked ({fresh.Count})", GUILayout.Height(22f))) MapLabBakeView.Run(() => MapLabMegaStatus.Bake(map, fresh), changed);
                }
            }
            GUILayout.Space(10f);
            MapLabWidgets.Caption("SIZE");
            GUILayout.Label($"One mega chunk is {map.ChunksPerMega} x {map.ChunksPerMega} chunks of {map.ChunkSize:0.##} units = {map.MegaChunkSize:0.##} units, {map.ChunkPixels * map.ChunksPerMega} px. Each bakes into its own M_x_y folder and exports as its own PSD.", MapLabWindowStyles.Wrap);
            MapLabBakeView.Settings(map, new[] { "_megaChunkPixels" });
            GUILayout.Space(10f);
            MapLabWidgets.Caption("FLOOR");
            DrawWorldSettings(map, world);
            GUILayout.Space(8f);
            GUILayout.Label("Click a mega chunk to see its layers and files. Add grows the map, Paint lays floor tiles. Drag with the middle button to pan, use the wheel to zoom.", MapLabWindowStyles.Wrap);
        }

        private static void DrawWorldSettings(MapLabMap map, MapLabWorldData world)
        {
            var serialized = new SerializedObject(world);
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("_palette"), new GUIContent("Palette"));
            using (new EditorGUI.DisabledScope(world.Megas.Any(mega => mega.IsPainted)))
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("_tilesPerSide"), new GUIContent("Tiles per Side", "Floor tiles along one side of a mega chunk. Locked once tiles are painted"));
            }
            if (serialized.ApplyModifiedProperties())
            {
                world.Touch();
                EditorUtility.SetDirty(world);
                MapLabWorldEdits.RebuildFloor(map);
            }
            GUILayout.Label($"A floor tile is {map.MegaChunkSize / world.TilesPerSide:0.##} units wide.", MapLabWindowStyles.Dim);
            MapLabFloor floor = MapLabWorldEdits.Floor(map);
            if (floor == null) return;
            var floorObject = new SerializedObject(floor);
            floorObject.Update();
            EditorGUILayout.PropertyField(floorObject.FindProperty("_sortingOrder"), new GUIContent("Floor Order", "Sorting order of the live floor, under roads and plazas"));
            if (floorObject.ApplyModifiedProperties()) floor.Rebuild();
        }

        private void DrawAdd(MapLabMegaState state, Action changed)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            GUILayout.Label("Add mega chunks", MapLabWindowStyles.Title);
            GUILayout.Label($"Click a + next to the map to add one mega chunk, or drag across several + to add a block. Every mega chunk is {map.ChunkPixels * map.ChunksPerMega} x {map.ChunkPixels * map.ChunksPerMega} px ({map.ChunksPerMega} x {map.ChunksPerMega} chunks of {map.ChunkSize:0.##} units).", MapLabWindowStyles.Wrap);
            GUILayout.Space(8f);
            MapLabWidgets.Caption("NEW MEGA CHUNKS");
            state.NewMegaTile = TilePopup("Base Tile", world.Palette, state.NewMegaTile);
            GUILayout.Space(8f);
            MapLabWidgets.Caption("MAP");
            RectInt extent = world.Extent();
            GUILayout.Label(world.Count == 0 ? "No mega chunk yet: click the + at the centre, or start from the map Bounds." : $"{world.Count} mega chunks over {extent.width} x {extent.height}", MapLabWindowStyles.Dim);
            if (GUILayout.Button(new GUIContent("Fill the Map Bounds", "Add every mega chunk that the MapLabMap Bounds touch"), GUILayout.Height(22f)))
            {
                int added = MapLabWorldEdits.AddOverBounds(map, state.NewMegaTile);
                Debug.Log($"[MapLab] Added {added} mega chunks over the map Bounds of '{map.name}'");
                state.FrameAll();
                changed();
            }
        }

        private void DrawSelection(MapLabMegaState state, Action changed)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            List<Vector2Int> picked = state.Selection.OrderBy(index => index.y).ThenBy(index => index.x).ToList();
            Vector2Int first = picked[0];
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(picked.Count == 1 ? $"M {first.x},{first.y}" : $"{picked.Count} mega chunks", MapLabWindowStyles.Title);
                if (MapLabWidgets.Locate(GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f)), "Frame in the viewport (F)")) state.FrameSelection();
            }
            float size = map.MegaChunkSize;
            if (picked.Count == 1)
            {
                Rect rect = MegaChunkFormula.MegaRect(first, size);
                GUILayout.Label($"x {rect.xMin:0.##} to {rect.xMax:0.##} u  ·  y {rect.yMin:0.##} to {rect.yMax:0.##} u  ·  {map.ChunksPerMega} x {map.ChunksPerMega} chunks", MapLabWindowStyles.Dim);
            }
            GUILayout.Space(6f);

            MapLabWidgets.Caption("LAYERS");
            List<MapLabMegaChunk> megas = picked.Select(world.Find).Where(mega => mega != null).ToList();
            foreach (var layer in MapLabLayerStack.Layers(map).Where(layer => !layer.Sorted))
            {
                if (!state.LayerMegas.TryGetValue(layer.Name, out HashSet<Vector2Int> layerMegas)) layerMegas = new HashSet<Vector2Int>();
                MapLabBakeStatus.State value = MapLabBakeStatus.State.NotBaked;
                if (megas.Any(mega => layerMegas.Contains(mega.Index))) value = megas.All(mega => mega.BakedCurrent) ? MapLabBakeStatus.State.UpToDate : MapLabBakeStatus.State.OutOfDate;
                Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                GUI.Label(new Rect(row.x + 4f, row.y, row.width - 110f, row.height), layer.Name, MapLabWindowStyles.Row);
                MapLabWidgets.Status(new Rect(row.xMax - 96f, row.y, 96f, row.height), value);
            }
            GUILayout.Space(6f);

            MapLabWidgets.Caption("FLOOR");
            byte baseTile = megas.Count > 0 ? megas[0].BaseTile : (byte)0;
            EditorGUI.showMixedValue = megas.Any(mega => mega.BaseTile != baseTile);
            EditorGUI.BeginChangeCheck();
            byte chosen = TilePopup("Base Tile", world.Palette, baseTile);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                MapLabWorldEdits.SetBase(map, picked, chosen);
                changed();
            }
            int painted = megas.Sum(mega => mega.PaintedCount());
            int total = megas.Count * world.TilesPerSide * world.TilesPerSide;
            EditorGUILayout.LabelField("Painted", $"{painted:N0} of {total:N0} tiles");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Paint Floor", GUILayout.Height(22f)))
                {
                    state.Tool = MapLabMegaTool.Paint;
                    state.FrameSelection();
                }
                using (new EditorGUI.DisabledScope(painted == 0))
                {
                    if (GUILayout.Button("Clear Paint", GUILayout.Height(22f)))
                    {
                        MapLabWorldEdits.ClearPaint(map, picked);
                        changed();
                    }
                }
            }
            GUILayout.Space(6f);

            MapLabWidgets.Caption("FILES");
            DrawFiles(map, first);
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Bake", "Rebake only these mega chunks in every ground layer"), MapLabWindowStyles.PrimaryButton, GUILayout.Height(24f)))
                {
                    MapLabBakeView.Run(() => MapLabMegaStatus.Bake(map, picked), changed);
                }
                if (GUILayout.Button(new GUIContent("Export PSD", "One PSD per picked mega chunk"), GUILayout.Height(24f))) ExportPsds(map, picked, changed);
                if (GUILayout.Button(new GUIContent("Import PSD", "Bring paint back from a folder of mega chunk PSDs"), GUILayout.Height(24f))) ImportPsds(map, changed);
            }
        }

        private static void DrawFiles(MapLabMap map, Vector2Int mega)
        {
            if (!MapLabLayerStack.Saved(map))
            {
                GUILayout.Label("Save the scene to bake.", MapLabWindowStyles.Dim);
                return;
            }
            bool any = false;
            foreach (var layer in MapLabLayerStack.Layers(map).Where(layer => !layer.Sorted))
            {
                string folder = MapLabLayerFiles.MegaFolder(MapLabLayerFiles.BaseFolder(map, layer.Name), mega);
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                any = true;
                int files = Directory.GetFiles(folder, "*.png").Length;
                MapLabWidgets.FileRow(AssetDatabase.LoadAssetAtPath<Object>(folder), "Folder Icon", $"{layer.Name} / {MegaChunkFormula.FolderName(mega)}/", $"{files} chunks");
            }
            if (!any) GUILayout.Label($"Not baked yet. Bakes write Layers / <layer> / Base / {MegaChunkFormula.FolderName(mega)}/", MapLabWindowStyles.Wrap);
        }

        private void DrawFloor(MapLabMegaState state, Action changed)
        {
            MapLabMap map = state.Map;
            MapLabWorldData world = map.World;
            Vector2Int? target = state.Selection.Count == 1 ? state.Selection.First() : state.HoverMega;
            MapLabMegaChunk mega = target.HasValue ? world.Find(target.Value) : null;
            GUILayout.Label(mega != null ? $"Floor  ·  M {mega.Index.x},{mega.Index.y}" : "Floor", MapLabWindowStyles.Title);
            GUILayout.Space(4f);
            MapLabWidgets.Caption("TILE", "palette");
            DrawSwatches(state, world.Palette);
            if (world.Palette == null) GUILayout.Label("Pick a palette on the Floor tiles page.", MapLabWindowStyles.Wrap);
            GUILayout.Space(4f);
            state.BrushSize = EditorGUILayout.IntSlider(new GUIContent("Brush Size", "Tiles per side ([ and ])"), state.BrushSize, 1, MapLabMegaState.MaxBrush);
            GUILayout.Space(8f);
            MapLabWidgets.Caption("TOOLS");
            GUILayout.Label("Paint places the tile, Shift erases, Alt picks the tile under the cursor. Fill fills one chunk; Shift-click fills the whole mega chunk (or every picked one). Erase puts tiles back to the base tile.", MapLabWindowStyles.Wrap);
            GUILayout.Space(8f);
            if (mega == null)
            {
                GUILayout.Label("Hover or pick a mega chunk to see its floor.", MapLabWindowStyles.Dim);
                return;
            }
            MapLabWidgets.Caption("THIS MEGA CHUNK");
            byte chosen = TilePopup("Base Tile", world.Palette, mega.BaseTile);
            if (chosen != mega.BaseTile)
            {
                MapLabWorldEdits.SetBase(map, new[] { mega.Index }, chosen);
                changed();
            }
            EditorGUILayout.LabelField("Painted", $"{mega.PaintedCount():N0} of {world.TilesPerSide * world.TilesPerSide:N0} tiles");
            Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(row.x, row.y, 100f, row.height), "Bake", MapLabWindowStyles.Row);
            MapLabWidgets.Status(new Rect(row.x + EditorGUIUtility.labelWidth + 2f, row.y, 120f, row.height), MapLabMegaStatus.Of(mega, state.Baked));
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake", MapLabWindowStyles.PrimaryButton, GUILayout.Height(24f))) MapLabBakeView.Run(() => MapLabMegaStatus.Bake(map, new[] { mega.Index }), changed);
                using (new EditorGUI.DisabledScope(!mega.IsPainted))
                {
                    if (GUILayout.Button("Clear Paint", GUILayout.Height(24f)))
                    {
                        MapLabWorldEdits.ClearPaint(map, new[] { mega.Index });
                        changed();
                    }
                }
            }
        }

        private static void DrawSwatches(MapLabMegaState state, MapLabFloorPalette palette)
        {
            if (palette == null || palette.Count == 0) return;
            float width = EditorGUIUtility.currentViewWidth;
            Rect probe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (probe.width > 1f) width = probe.width;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + 2f) / (SwatchWidth + 2f)));
            int rows = Mathf.CeilToInt(palette.Count / (float)columns);
            Rect area = GUILayoutUtility.GetRect(0f, rows * (SwatchHeight + 2f), GUILayout.ExpandWidth(true));
            for (int i = 0; i < palette.Count; i++)
            {
                MapLabFloorTile tile = palette.Tiles[i];
                var cell = new Rect(area.x + (i % columns) * (SwatchWidth + 2f), area.y + (i / columns) * (SwatchHeight + 2f), SwatchWidth, SwatchHeight);
                var swatch = new Rect(cell.x + 5f, cell.y + 2f, 32f, 32f);
                bool selected = state.BrushTile == i + 1;
                MapLabFloorTilesView.Swatch(swatch, tile, selected);
                GUI.Label(new Rect(cell.x - 4f, swatch.yMax + 2f, cell.width + 8f, 14f), tile.Name, selected ? MapLabWindowStyles.SwatchLabelSelected : MapLabWindowStyles.SwatchLabel);
                Event current = Event.current;
                if (current.type == EventType.MouseDown && current.button == 0 && cell.Contains(current.mousePosition))
                {
                    state.BrushTile = (byte)(i + 1);
                    if (state.Tool == MapLabMegaTool.Erase || state.Tool == MapLabMegaTool.Pick) state.Tool = MapLabMegaTool.Paint;
                    current.Use();
                }
            }
        }
        #endregion
    }
}
