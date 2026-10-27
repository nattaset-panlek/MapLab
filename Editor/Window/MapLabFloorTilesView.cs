using System;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabFloorTilesView
    {
        #region [Variables/Fields]
        private Vector2 _scroll;
        private int _selected;
        #endregion

        #region [Public Methods]
        public void Draw(Rect area, MapLabMap map, Action changed)
        {
            GUILayout.BeginArea(area);
            MapLabWorldData world = map.World;
            MapLabFloorPalette palette = world != null ? world.Palette : null;
            Rect header = MapLabWidgets.HeaderBar();
            if (world != null && GUI.Button(new Rect(header.xMax - 104f, header.y + 8f, 96f, 18f), new GUIContent("New Palette", "A new palette asset with the six default tiles, used by this map"), EditorStyles.miniButton))
            {
                NewPalette(map, world, changed);
            }
            MapLabGlyphs.Draw(new Rect(header.x + 12f, header.y + 9f, 16f, 16f), MapLabGlyph.FloorTiles, Color.white);
            float x = MapLabWidgets.Title(header, header.x + 36f, "Floor tiles", Mathf.Max(0f, header.width - 180f));
            if (palette != null) GUI.Label(new Rect(x, header.y, header.width - x - 120f, header.height), $"{palette.Count} tiles in {palette.name}", MapLabWindowStyles.Dim);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 12, 8, 12) }))
            {
                if (world == null) DrawNoWorld(map, changed);
                else DrawPalette(map, world, changed);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public static void Swatch(Rect rect, MapLabFloorTile tile, bool selected)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, tile.Color, 0f, 4f);
            if (tile.Texture != null) GUI.DrawTexture(rect, tile.Texture, ScaleMode.ScaleAndCrop, true, 0f, tile.Color, 0f, 4f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, selected ? MapLabWindowStyles.Primary : new Color(0.12f, 0.12f, 0.12f, 1f), selected ? 2f : 1f, 4f);
        }
        #endregion

        #region [Private Methods]
        private static void DrawNoWorld(MapLabMap map, Action changed)
        {
            GUILayout.Label("Floor tiles are laid on mega chunks. Create the world first: it adds a palette with six tiles (Grass, Meadow, Dirt, Stone, Sand, Snow) that you can change here.", MapLabWindowStyles.Wrap);
            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!MapLabLayerStack.Saved(map)))
            {
                if (GUILayout.Button("Create World", MapLabWindowStyles.PrimaryButton, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    MapLabMegaInspector.Run(() =>
                    {
                        MapLabWorldEdits.EnsureWorld(map);
                        int added = MapLabWorldEdits.AddOverBounds(map, 1);
                        return $"[MapLab] Created the world of '{map.name}' with {added} mega chunks";
                    }, changed, false);
                }
            }
        }

        private void DrawPalette(MapLabMap map, MapLabWorldData world, Action changed)
        {
            var worldObject = new SerializedObject(world);
            worldObject.Update();
            EditorGUILayout.PropertyField(worldObject.FindProperty("_palette"), new GUIContent("Palette", "The tile list this map paints with. Several maps can share one palette"));
            if (worldObject.ApplyModifiedProperties())
            {
                world.Touch();
                EditorUtility.SetDirty(world);
                MapLabWorldEdits.RebuildFloor(map);
                changed();
            }
            MapLabFloorPalette palette = world.Palette;
            if (palette == null)
            {
                GUILayout.Label("No palette: pick one or press New Palette.", MapLabWindowStyles.Dim);
                return;
            }
            GUILayout.Space(8f);
            MapLabWidgets.Caption("TILES", "id  ·  name  ·  texture");
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, palette.Count - 1));
            for (int i = 0; i < palette.Count; i++)
            {
                MapLabFloorTile tile = palette.Tiles[i];
                Rect row = MapLabWidgets.ListRow(i == _selected);
                Swatch(new Rect(row.x + 6f, row.y + 3f, 16f, 16f), tile, false);
                GUI.Label(new Rect(row.x + 28f, row.y, 24f, row.height), (i + 1).ToString(), MapLabWindowStyles.Dim);
                GUI.Label(new Rect(row.x + 50f, row.y, row.width - 200f, row.height), tile.Name, i == _selected ? MapLabWindowStyles.RowBold : MapLabWindowStyles.Row);
                string detail = tile.Texture != null ? $"{tile.Texture.name}, {tile.TextureSize:0.##} u" : "colour only";
                GUI.Label(new Rect(row.xMax - 150f, row.y, 144f, row.height), detail, MapLabWindowStyles.DimRight);
                Event current = Event.current;
                if (current.type == EventType.MouseDown && current.button == 0 && row.Contains(current.mousePosition))
                {
                    _selected = i;
                    current.Use();
                }
            }
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(palette.Count >= MapLabFloorPalette.MaxTiles))
                {
                    if (GUILayout.Button("Add Tile", EditorStyles.miniButton, GUILayout.Width(80f)))
                    {
                        Undo.RecordObject(palette, "Add Floor Tile");
                        palette.Tiles.Add(new MapLabFloorTile($"Tile {palette.Count + 1}", Color.gray));
                        _selected = palette.Count - 1;
                        Changed(map, palette, changed);
                    }
                }
                using (new EditorGUI.DisabledScope(palette.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Remove Last", "Only the last tile can go, so painted tile numbers stay the same"), EditorStyles.miniButton, GUILayout.Width(90f)))
                    {
                        Undo.RecordObject(palette, "Remove Floor Tile");
                        palette.Tiles.RemoveAt(palette.Count - 1);
                        _selected = Mathf.Max(0, palette.Count - 1);
                        Changed(map, palette, changed);
                    }
                }
                GUILayout.FlexibleSpace();
            }
            if (palette.Count == 0) return;

            GUILayout.Space(10f);
            MapLabWidgets.Caption($"TILE {_selected + 1}");
            var serialized = new SerializedObject(palette);
            serialized.Update();
            SerializedProperty tileProperty = serialized.FindProperty("_tiles").GetArrayElementAtIndex(_selected);
            EditorGUILayout.PropertyField(tileProperty.FindPropertyRelative("_name"), new GUIContent("Name"));
            EditorGUILayout.PropertyField(tileProperty.FindPropertyRelative("_color"), new GUIContent("Colour", "Tints the texture; with no texture the tile is this colour"));
            EditorGUILayout.PropertyField(tileProperty.FindPropertyRelative("_texture"), new GUIContent("Texture", "A seamless texture; set its Wrap Mode to Repeat"));
            EditorGUILayout.PropertyField(tileProperty.FindPropertyRelative("_textureSize"), new GUIContent("Texture Size", "World units one repeat of the texture covers"));
            if (serialized.ApplyModifiedProperties()) Changed(map, palette, changed);
            Texture2D texture = palette.Tiles[_selected].Texture;
            if (texture != null && texture.wrapMode != TextureWrapMode.Repeat) EditorGUILayout.HelpBox($"'{texture.name}' has Wrap Mode {texture.wrapMode}: set it to Repeat so the tile repeats across the floor.", MessageType.Warning);
            GUILayout.Space(10f);
            GUILayout.Label($"Paint tiles on the Mega chunks page (Paint tool). One floor tile is {map.MegaChunkSize / world.TilesPerSide:0.##} units. The floor bakes into the ground layer that takes 'Floor tiles' on the Layers page; until then it draws live under roads and plazas.", MapLabWindowStyles.Wrap);
        }

        private static void Changed(MapLabMap map, MapLabFloorPalette palette, Action changed)
        {
            EditorUtility.SetDirty(palette);
            MapLabWorldEdits.RebuildFloor(map);
            changed();
        }

        private static void NewPalette(MapLabMap map, MapLabWorldData world, Action changed)
        {
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(world)).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MapLabWorldEdits.PaletteFile}");
            var palette = ScriptableObject.CreateInstance<MapLabFloorPalette>();
            foreach (var (name, color) in MapLabWorldEdits.DefaultTiles) palette.Tiles.Add(new MapLabFloorTile(name, color));
            AssetDatabase.CreateAsset(palette, path);
            Undo.RecordObject(world, "New Floor Palette");
            world.Palette = palette;
            world.Touch();
            EditorUtility.SetDirty(world);
            MapLabWorldEdits.RebuildFloor(map);
            changed();
            EditorGUIUtility.PingObject(palette);
            GUIUtility.ExitGUI();
        }
        #endregion
    }
}
