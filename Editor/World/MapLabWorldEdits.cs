using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabWorldEdits
    {
        #region [Variables/Fields]
        public const string WorldFile = "World.asset";
        public const string PaletteFile = "Floor Palette.asset";
        public const int DefaultFloorOrder = -101;

        public static readonly (string Name, Color Color)[] DefaultTiles =
        {
            ("Grass", new Color32(0x5E, 0x8F, 0x4E, 0xFF)), ("Meadow", new Color32(0x86, 0xA6, 0x5F, 0xFF)), ("Dirt", new Color32(0x7A, 0x64, 0x48, 0xFF)),
            ("Stone", new Color32(0x7E, 0x85, 0x90, 0xFF)), ("Sand", new Color32(0xB5, 0xA5, 0x7F, 0xFF)), ("Snow", new Color32(0xD5, 0xDC, 0xE4, 0xFF))
        };
        #endregion

        #region [Public Methods]
        public static MapLabWorldData EnsureWorld(MapLabMap map)
        {
            if (map.World != null)
            {
                EnsureFloor(map);
                return map.World;
            }
            if (!MapLabLayerStack.Saved(map)) throw new InvalidOperationException("Save the scene first: MapLab keeps the world data in a folder next to it.");
            string folder = MapLabOutputPaths.MapFolder(map);
            MapLabOutputPaths.EnsureFolder(folder);
            var world = ScriptableObject.CreateInstance<MapLabWorldData>();
            world.Palette = EnsurePalette(folder);
            AssetDatabase.CreateAsset(world, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{WorldFile}"));
            Undo.RecordObject(map, "Create MapLab World");
            map.World = world;
            EditorUtility.SetDirty(map);
            EnsureFloor(map);
            EnsureFloorLayer(map);
            AssetDatabase.SaveAssets();
            return world;
        }

        public static MapLabFloorPalette EnsurePalette(string folder)
        {
            string path = $"{folder}/{PaletteFile}";
            var palette = AssetDatabase.LoadAssetAtPath<MapLabFloorPalette>(path);
            if (palette != null) return palette;
            palette = ScriptableObject.CreateInstance<MapLabFloorPalette>();
            foreach (var (name, color) in DefaultTiles) palette.Tiles.Add(new MapLabFloorTile(name, color));
            AssetDatabase.CreateAsset(palette, path);
            return palette;
        }

        public static MapLabFloor Floor(MapLabMap map)
        {
            return map.GetComponentsInChildren<MapLabFloor>(true).FirstOrDefault(floor => floor.World == map.World);
        }

        public static MapLabFloor EnsureFloor(MapLabMap map)
        {
            MapLabFloor floor = Floor(map);
            if (floor != null) return floor;
            var go = new GameObject(MapLabFloor.ObjectName) { layer = map.gameObject.layer };
            go.transform.SetParent(map.transform, false);
            go.transform.SetAsFirstSibling();
            floor = go.AddComponent<MapLabFloor>();
            floor.Setup(map.World, ExtrudedShapeMenu.LoadOrCreateMaterial(), DefaultFloorOrder);
            Undo.RegisterCreatedObjectUndo(go, "Create MapLab Floor");
            return floor;
        }

        public static string EnsureFloorLayer(MapLabMap map)
        {
            MapLabLayerStack.EnsureLayers(map);
            List<MapLabBakeLayerSettings> ground = MapLabLayerStack.LayersBottomToTop(map).Where(layer => !layer.Sorted).ToList();
            if (ground.Count == 0 || ground.Any(layer => (layer.Kinds & MapLabBakeKinds.Floor) != 0)) return null;
            Undo.RecordObject(map, "Bake MapLab Floor");
            ground[0].Kinds |= MapLabBakeKinds.Floor;
            MapLabLayerStack.MarkDirty(map);
            return $"Floor tiles now bake into the '{ground[0].Name}' layer";
        }

        public static int AddMegas(MapLabMap map, IEnumerable<Vector2Int> indices, byte baseTile)
        {
            MapLabWorldData world = EnsureWorld(map);
            List<Vector2Int> added = indices.Where(index => !world.Contains(index)).Distinct().ToList();
            if (added.Count == 0) return 0;
            Undo.RecordObject(world, added.Count == 1 ? "Add Mega Chunk" : $"Add {added.Count} Mega Chunks");
            foreach (var index in added) world.Add(index, baseTile);
            Changed(map, added);
            return added.Count;
        }

        public static int RemoveMegas(MapLabMap map, IEnumerable<Vector2Int> indices)
        {
            MapLabWorldData world = map.World;
            if (world == null) return 0;
            List<Vector2Int> removed = indices.Where(world.Contains).Distinct().ToList();
            if (removed.Count == 0) return 0;
            Undo.RecordObject(world, removed.Count == 1 ? "Remove Mega Chunk" : $"Remove {removed.Count} Mega Chunks");
            foreach (var index in removed) world.Remove(index);
            Changed(map, removed);
            return removed.Count;
        }

        public static float TileSize(MapLabMap map)
        {
            return map.World != null ? map.MegaChunkSize / map.World.TilesPerSide : map.MegaChunkSize / MegaChunkFormula.DefaultTilesPerSide;
        }

        public static int Paint(MapLabMap map, Vector2 world, int brush, byte tile, string undoName)
        {
            if (map.World == null) return 0;
            Vector2Int center = MegaChunkFormula.TileAt(world, TileSize(map));
            return SetTiles(map, FloorFormula.Brush(center, brush), tile, undoName);
        }

        public static int SetTiles(MapLabMap map, IEnumerable<Vector2Int> tiles, byte tile, string undoName)
        {
            MapLabWorldData data = map.World;
            if (data == null) return 0;
            int size = data.TilesPerSide;
            Undo.RecordObject(data, undoName);
            var touched = new HashSet<Vector2Int>();
            int changed = 0;
            foreach (var index in tiles)
            {
                Vector2Int megaIndex = MegaChunkFormula.MegaOfTile(index, size);
                MapLabMegaChunk mega = data.Find(megaIndex);
                if (mega == null) continue;
                Vector2Int cell = MegaChunkFormula.CellOfTile(index, size);
                if (!mega.SetCell(cell.x, cell.y, size, tile)) continue;
                changed++;
                touched.Add(megaIndex);
            }
            foreach (var index in touched) data.Find(index).Touch();
            if (changed > 0) Changed(map, touched);
            return changed;
        }

        public static int FillChunk(MapLabMap map, Vector2 world, byte tile)
        {
            if (map.World == null) return 0;
            float chunkSize = map.ChunkSize;
            float tileSize = TileSize(map);
            Vector2Int chunk = new Vector2Int(Mathf.FloorToInt(world.x / chunkSize), Mathf.FloorToInt(world.y / chunkSize));
            Vector2Int min = MegaChunkFormula.TileAt(new Vector2(chunk.x, chunk.y) * chunkSize + Vector2.one * (tileSize * 0.5f), tileSize);
            int count = Mathf.Max(1, Mathf.RoundToInt(chunkSize / tileSize));
            var tiles = new List<Vector2Int>(count * count);
            for (int y = 0; y < count; y++)
            {
                for (int x = 0; x < count; x++) tiles.Add(min + new Vector2Int(x, y));
            }
            return SetTiles(map, tiles, tile, "Fill Chunk");
        }

        public static int FillMegas(MapLabMap map, IEnumerable<Vector2Int> indices, byte tile)
        {
            MapLabWorldData data = map.World;
            if (data == null) return 0;
            List<MapLabMegaChunk> megas = indices.Select(data.Find).Where(mega => mega != null).Distinct().ToList();
            if (megas.Count == 0) return 0;
            Undo.RecordObject(data, megas.Count == 1 ? "Fill Mega Chunk" : $"Fill {megas.Count} Mega Chunks");
            foreach (var mega in megas)
            {
                bool changed = mega.SetBase(tile) | mega.ClearCells();
                if (changed) mega.Touch();
            }
            Changed(map, megas.Select(mega => mega.Index));
            return megas.Count;
        }

        public static int SetBase(MapLabMap map, IEnumerable<Vector2Int> indices, byte tile)
        {
            MapLabWorldData data = map.World;
            if (data == null) return 0;
            List<MapLabMegaChunk> megas = indices.Select(data.Find).Where(mega => mega != null && mega.BaseTile != tile).Distinct().ToList();
            if (megas.Count == 0) return 0;
            Undo.RecordObject(data, "Set Base Tile");
            foreach (var mega in megas)
            {
                mega.SetBase(tile);
                mega.Touch();
            }
            Changed(map, megas.Select(mega => mega.Index));
            return megas.Count;
        }

        public static int AddOverBounds(MapLabMap map, byte baseTile)
        {
            float size = map.MegaChunkSize;
            Rect bounds = map.Bounds;
            Vector2Int min = MegaChunkFormula.MegaAt(bounds.min, size);
            Vector2Int max = MegaChunkFormula.MegaAt(bounds.max - Vector2.one * 0.001f, size);
            var cells = new List<Vector2Int>();
            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++) cells.Add(new Vector2Int(x, y));
            }
            return AddMegas(map, cells, baseTile);
        }

        public static int ClearPaint(MapLabMap map, IEnumerable<Vector2Int> indices)
        {
            MapLabWorldData data = map.World;
            if (data == null) return 0;
            List<MapLabMegaChunk> megas = indices.Select(data.Find).Where(mega => mega != null && mega.IsPainted).Distinct().ToList();
            if (megas.Count == 0) return 0;
            Undo.RecordObject(data, "Clear Floor Paint");
            foreach (var mega in megas)
            {
                mega.ClearCells();
                mega.Touch();
            }
            Changed(map, megas.Select(mega => mega.Index));
            return megas.Count;
        }

        public static byte Pick(MapLabMap map, Vector2 world)
        {
            MapLabWorldData data = map.World;
            if (data == null) return 0;
            Vector2Int tile = MegaChunkFormula.TileAt(world, TileSize(map));
            Vector2Int cell = MegaChunkFormula.CellOfTile(tile, data.TilesPerSide);
            return data.TileAt(MegaChunkFormula.MegaOfTile(tile, data.TilesPerSide), cell.x, cell.y);
        }

        public static void RebuildFloor(MapLabMap map)
        {
            MapLabFloor floor = Floor(map);
            if (floor != null) floor.Rebuild();
        }
        #endregion

        #region [Private Methods]
        private static void Changed(MapLabMap map, IEnumerable<Vector2Int> megas)
        {
            MapLabWorldData world = map.World;
            world.Touch();
            EditorUtility.SetDirty(world);
            MapLabFloor floor = EnsureFloor(map);
            foreach (var index in megas) floor.RebuildMega(index);
        }
        #endregion
    }
}
