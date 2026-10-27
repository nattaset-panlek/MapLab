using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabWorldProbe
    {
        #region [Variables/Fields]
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_world.txt";

        private const string ProbeFolderName = "WorldProbe";
        private const string ProbeSceneName = "MapLab_WorldProbe.unity";
        private const string ExportFolder = MapLabPreviewRenderer.OutputFolder + "/WorldProbe";
        private const string PresetFolder = "Assets/MapLabProbePresets";
        private const string ShotPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_world_shot.png";
        private const string MapImagePath = MapLabPreviewRenderer.OutputFolder + "/MapLab_world_map.png";
        private const string PaintName = "Ground_paint_probe";
        private const int ShotWidth = 1600;
        private const int ShotHeight = 900;
        private const int MapPixelsPerTile = 4;
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/World Report", false, 106)]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Run());
            EditorUtility.RevealInFinder(ReportPath);
        }

        public static void ReportBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(Run()));
        }

        public static string Run()
        {
            if (!File.Exists(MapLabSceneBuilder.ScenePath)) throw new InvalidOperationException("Build the MapLab demo scene first.");
            string probeFolder = $"{MapLabSceneBuilder.DemoFolder}/{ProbeFolderName}";
            string probeScene = $"{probeFolder}/{ProbeSceneName}";
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            if (Directory.Exists(ExportFolder)) Directory.Delete(ExportFolder, true);
            var report = new StringBuilder();
            report.AppendLine($"MapLab world report  {DateTime.Now:yyyy-MM-dd HH:mm}  Unity {Application.unityVersion}");
            report.AppendLine($"Copy of {MapLabSceneBuilder.ScenePath} at {probeScene}; the copy, its bake folder and the test presets are deleted at the end");
            int failures = 0;
            try
            {
                MapLabLayerFiles.ClearFolder(probeFolder);
                MapLabLayerFiles.ClearFolder(PresetFolder);
                MapLabOutputPaths.EnsureFolder(probeFolder);
                if (!AssetDatabase.CopyAsset(MapLabSceneBuilder.ScenePath, probeScene)) throw new InvalidOperationException($"Could not copy the demo scene to {probeScene}");
                Scene scene = EditorSceneManager.OpenScene(probeScene, OpenSceneMode.Single);
                MapLabMap map = MapLabWalkableBaker.FindMaps(scene).First();
                SortingGroup.UpdateAllSortingGroups();
                failures += StartWorld(report, map);
                failures += PaintFloor(report, map);
                failures += BakeAll(report, map);
                failures += RebakeOne(report, map);
                failures += ExportAndImport(report, map);
                failures += Presets(report, map);
                failures += MapLabDrawLayerProbe.Run(report, map, scene);
                failures += Shots(report, map, scene);
            }
            catch (Exception e)
            {
                failures++;
                report.AppendLine($"FAIL  {e.GetType().Name}: {e.Message}");
                report.AppendLine(e.StackTrace);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                MapLabLayerFiles.ClearFolder(probeFolder);
                MapLabLayerFiles.ClearFolder(PresetFolder);
                AssetDatabase.Refresh();
            }
            report.AppendLine();
            report.AppendLine(failures == 0 ? "RESULT  all checks passed" : $"RESULT  {failures} checks failed");
            File.WriteAllText(ReportPath, report.ToString());
            if (failures > 0) throw new InvalidOperationException($"{failures} world checks failed, see {ReportPath}");
            return $"[MapLab] World report written to {ReportPath}: all checks passed";
        }

        public static void Shoot(Camera camera, Vector2 center, float size, string path)
        {
            Vector3 previousPosition = camera.transform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(center.x, center.y, previousPosition.z);
                camera.orthographicSize = size;
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.transform.position = previousPosition;
                camera.orthographicSize = previousSize;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
        #endregion

        #region [Private Methods]
        private static int StartWorld(StringBuilder report, MapLabMap map)
        {
            Section(report, "1. Start a world over the demo map");
            var (layers, restored) = MapLabLayerBaker.UnbakeAll(map, true);
            report.AppendLine($"Unbaked {layers} layers of the copy ({restored} sources live again)");
            MapLabWorldData world = MapLabWorldEdits.EnsureWorld(map);
            report.AppendLine(MapLabWorldEdits.EnsureFloorLayer(map) ?? "A ground layer already takes floor tiles");
            int added = MapLabWorldEdits.AddOverBounds(map, 1);
            MapLabFloorPalette palette = world.Palette;
            palette.Tiles[0].Texture = ExtrudedShapeMenu.Art("MapLab_GrassFill");
            palette.Tiles[2].Texture = ExtrudedShapeMenu.Art("MapLab_DirtFill");
            palette.Tiles[3].Texture = ExtrudedShapeMenu.Art("MapLab_CobbleFill");
            EditorUtility.SetDirty(palette);
            MapLabWorldEdits.RebuildFloor(map);
            RectInt extent = world.Extent();
            report.AppendLine($"World asset {AssetDatabase.GetAssetPath(world)}, palette {AssetDatabase.GetAssetPath(palette)} ({palette.Count} tiles)");
            report.AppendLine($"Map bounds {map.Bounds} -> {added} mega chunks over {extent.width} x {extent.height} (from {extent.min} to {extent.max - Vector2Int.one})");
            report.AppendLine($"Mega chunk = {map.ChunksPerMega} x {map.ChunksPerMega} chunks of {map.ChunkSize} u at {map.BakePixelsPerUnit} px/u = {map.MegaChunkSize} u, {map.ChunkPixels * map.ChunksPerMega} px");
            return Check(report, added > 0 && map.ChunkPixels * map.ChunksPerMega == 4096, "the demo map gets mega chunks of 4096 px");
        }

        private static int PaintFloor(StringBuilder report, MapLabMap map)
        {
            Section(report, "2. Paint floor tiles");
            MapLabWorldData world = map.World;
            byte dirt = world.Palette.IdOf("Dirt");
            byte stone = world.Palette.IdOf("Stone");
            byte meadow = world.Palette.IdOf("Meadow");
            Rect bounds = map.Bounds;
            int painted = 0;
            var from = new Vector2(bounds.xMin + 2f, bounds.center.y - 3f);
            var to = new Vector2(bounds.xMax - 2f, bounds.center.y + 5f);
            for (float t = 0f; t <= 1f; t += 0.01f) painted += MapLabWorldEdits.Paint(map, Vector2.Lerp(from, to, t) + Vector2.up * Mathf.Sin(t * 9f) * 2f, 3, dirt, "Probe Paint");
            int filled = MapLabWorldEdits.FillChunk(map, MapLabGroundBuilder.PlazaCenter, stone);
            Vector2Int corner = world.Megas.OrderBy(mega => mega.Index.y).ThenBy(mega => mega.Index.x).First().Index;
            MapLabWorldEdits.SetBase(map, new[] { corner }, meadow);
            foreach (var mega in world.Megas) report.AppendLine($"  M {mega.Index.x},{mega.Index.y}: base {world.Palette.NameOf(mega.BaseTile)}, {mega.PaintedCount()} painted tiles, version {mega.Version}");
            report.AppendLine($"Road: {painted} tiles of Dirt; plaza chunk: {filled} tiles of Stone; M {corner.x},{corner.y} base set to Meadow");
            MapLabFloor floor = MapLabWorldEdits.Floor(map);
            int meshes = floor.GetComponentsInChildren<MeshFilter>(true).Length;
            int quads = floor.GetComponentsInChildren<MeshFilter>(true).Sum(filter => filter.sharedMesh.vertexCount / 4);
            report.AppendLine($"Live floor: {meshes} meshes, {quads} quads for {world.Count * world.TilesPerSide * world.TilesPerSide:N0} tiles");
            return Check(report, painted > 0 && filled == 256 && quads < world.Count * world.TilesPerSide * world.TilesPerSide / 4, "painting changes tiles and the floor merges them into few quads");
        }

        private static int BakeAll(StringBuilder report, MapLabMap map)
        {
            Section(report, "3. Bake all layers: one folder per mega chunk");
            SortingGroup.UpdateAllSortingGroups();
            report.AppendLine(MapLabStaticBaker.Bake(map));
            int failures = 0;
            int perMega = MapLabLayerFiles.PerMega(map);
            foreach (var layer in MapLabLayerStack.Layers(map).Where(layer => !layer.Sorted))
            {
                string folder = MapLabLayerFiles.BaseFolder(map, layer.Name);
                Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(folder);
                int misplaced = chunks.Count(pair => pair.Value != MapLabLayerFiles.ChunkPath(folder, pair.Key, perMega));
                List<string> megaFolders = Directory.GetDirectories(folder).Select(Path.GetFileName).OrderBy(name => name).ToList();
                int loose = Directory.GetFiles(folder, "*.png").Length;
                report.AppendLine($"  {layer.Name}: {chunks.Count} chunks in {megaFolders.Count} folders ({string.Join(", ", megaFolders)}), {loose} loose files, {misplaced} in the wrong folder");
                failures += Check(report, chunks.Count > 0 && misplaced == 0 && loose == 0 && megaFolders.All(name => MegaChunkFormula.TryParseFolder(name, out _)), $"'{layer.Name}' writes every chunk into its M_x_y folder");
            }
            MapLabBakedLayer ground = MapLabLayerBaker.BakedLayer(map, MapLabLayerStack.LayersBottomToTop(map).First(layer => (layer.Kinds & MapLabBakeKinds.Floor) != 0).Name);
            failures += Check(report, ground != null && ground.BakedFloors.Count == 1 && ground.BakedFloors[0].Baked, "the floor is baked into the ground layer and its live meshes are hidden");
            Dictionary<MapLabBakeStatus.State, int> states = StateCounts(map);
            report.AppendLine($"Mega chunk states: {Describe(states)}");
            failures += Check(report, !states.ContainsKey(MapLabBakeStatus.State.OutOfDate), "no mega chunk is out of date after Bake All");
            return failures;
        }

        private static int RebakeOne(StringBuilder report, MapLabMap map)
        {
            Section(report, "4. Change one mega chunk and rebake only it");
            MapLabWorldData world = map.World;
            HashSet<Vector2Int> baked = MapLabMegaStatus.BakedMegas(map);
            Vector2Int target = world.Megas.Select(mega => mega.Index).First(baked.Contains);
            string ground = MapLabLayerStack.LayersBottomToTop(map).First(layer => !layer.Sorted).Name;
            string folder = MapLabLayerFiles.BaseFolder(map, ground);
            int perMega = MapLabLayerFiles.PerMega(map);
            Dictionary<Vector2Int, Hash128> before = MapLabLayerFiles.Hashes(folder);
            Rect area = MegaChunkFormula.MegaRect(target, map.MegaChunkSize);
            int painted = MapLabWorldEdits.Paint(map, area.center, 9, world.Palette.IdOf("Sand"), "Probe Paint");
            List<Vector2Int> stale = MapLabMegaStatus.WithState(map, MapLabBakeStatus.State.OutOfDate);
            report.AppendLine($"Painted {painted} Sand tiles in M {target.x},{target.y}; out of date now: {string.Join(" ", stale.Select(mega => $"{mega.x},{mega.y}"))}");
            int failures = Check(report, stale.Count == 1 && stale[0] == target, "only the painted mega chunk goes out of date");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            report.AppendLine(MapLabMegaStatus.Bake(map, stale));
            watch.Stop();
            Dictionary<Vector2Int, Hash128> after = MapLabLayerFiles.Hashes(folder);
            int changedInside = after.Count(pair => MegaChunkFormula.MegaOfChunk(pair.Key, perMega) == target && (!before.TryGetValue(pair.Key, out Hash128 hash) || hash != pair.Value));
            int changedOutside = after.Count(pair => MegaChunkFormula.MegaOfChunk(pair.Key, perMega) != target && (!before.TryGetValue(pair.Key, out Hash128 hash) || hash != pair.Value));
            int lost = before.Keys.Count(index => !after.ContainsKey(index));
            report.AppendLine($"'{ground}': {changedInside} chunks changed inside M {target.x},{target.y}, {changedOutside} outside, {lost} lost ({watch.Elapsed.TotalSeconds:0.0} s)");
            failures += Check(report, changedInside > 0 && changedOutside == 0 && lost == 0, "the rebake rewrites only that mega chunk's folder");
            failures += Check(report, MapLabMegaStatus.WithState(map, MapLabBakeStatus.State.OutOfDate).Count == 0, "it is up to date again");
            return failures;
        }

        private static int ExportAndImport(StringBuilder report, MapLabMap map)
        {
            Section(report, "5. One PSD per mega chunk, paint, import the folder");
            report.AppendLine(MapLabPaintExchange.ExportMegaPsds(map, ExportFolder));
            List<string> files = Directory.GetFiles(ExportFolder, "*.psd").OrderBy(file => file).ToList();
            string first = files.First();
            MapLabPsd.Document document = MapLabPsd.Read(first);
            report.AppendLine($"{files.Count} files: {string.Join(", ", files.Select(Path.GetFileName))}");
            report.AppendLine($"{Path.GetFileName(first)}: {document.Width} x {document.Height} px, layers {string.Join(", ", document.Layers.Select(layer => layer.Name))}");
            int failures = Check(report, files.Count == MapLabPaintExchange.MegasWithArt(map).Count && document.Width == 4096 && document.Height == 4096, "each mega chunk is its own 4096 x 4096 PSD");

            var rect = new RectInt(1024, 1024, 512, 512);
            var pixels = new byte[rect.width * rect.height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 40;
                pixels[i + 1] = 200;
                pixels[i + 2] = 255;
                pixels[i + 3] = 150;
            }
            document.Layers.Insert(document.Layers.Count - 1, new MapLabPsd.Layer { Name = PaintName, Rect = rect, Pixels = pixels });
            MapLabPsd.Write(first, document, new byte[document.Width * document.Height * 4]);
            report.AppendLine(MapLabPaintExchange.ImportPsdFolder(map, ExportFolder));
            string paintFolder = MapLabLayerFiles.PaintFolder(map, "Ground", PaintName);
            Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(paintFolder);
            int perMega = MapLabLayerFiles.PerMega(map);
            string expected = Path.GetFileNameWithoutExtension(first);
            report.AppendLine($"Paint '{PaintName}': {chunks.Count} chunks in {string.Join(", ", chunks.Values.Select(path => Path.GetFileName(Path.GetDirectoryName(path))).Distinct())}");
            failures += Check(report, chunks.Count > 0 && chunks.All(pair => pair.Value == MapLabLayerFiles.ChunkPath(paintFolder, pair.Key, perMega) && MegaChunkFormula.FolderName(MegaChunkFormula.MegaOfChunk(pair.Key, perMega)) == expected),
                "paint from one mega chunk PSD lands in that mega chunk's paint folder");
            return failures;
        }

        private static int Presets(StringBuilder report, MapLabMap map)
        {
            Section(report, "6. Presets");
            List<ExtrudedShape> plateaus = map.GetComponentsInChildren<ExtrudedShape>(true).Where(shape => !shape.IsStacked && shape.Type == ExtrudedShapeType.Plateau && shape.Height > 0.5f).ToList();
            ExtrudedShape source = plateaus[0];
            ExtrudedShape other = plateaus.Skip(1).FirstOrDefault(shape => Mathf.Abs(shape.Height - source.Height) > 0.01f) ?? plateaus.Last();
            MapLabPreset preset = MapLabPresets.Create(source, "Probe Plateau", PresetFolder);
            report.AppendLine($"Preset '{preset.name}' ({preset.Kind}) from '{source.name}' (height {source.Height}) at {AssetDatabase.GetAssetPath(preset)}");
            List<Vector2> footprint = other.Points.ToList();
            MapLabPresets.Apply(preset, other);
            int failures = Check(report, Mathf.Abs(other.Height - source.Height) < 0.001f && footprint.SequenceEqual(other.Points), $"applying to '{other.name}' copies the height and keeps its footprint");
            Component created = MapLabPresets.CreateInScene(preset, map, new Vector3(map.Bounds.center.x, map.Bounds.center.y, 0f));
            failures += Check(report, created is ExtrudedShape shape && Mathf.Abs(shape.Height - source.Height) < 0.001f && shape.Preset == preset, "Create in Scene makes a linked plateau with the preset values");
            Component template = MapLabPresets.Template(preset);
            var serialized = new SerializedObject(template);
            serialized.FindProperty("_height").floatValue = 3.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MapLabPresets.Store(preset, template);
            MapLabPresets.Release(template);
            int synced = MapLabPresets.Sync(preset);
            report.AppendLine($"Changed the preset height to 3.25: {synced} users updated");
            failures += Check(report, synced == 3 && MapLabPresets.Users(preset).All(user => Mathf.Abs(((ExtrudedShape)user).Height - 3.25f) < 0.001f), "changing the preset updates every user");
            MapLabPreset environment = MapLabPresets.Create(map, "Probe Environment", PresetFolder);
            var copy = new GameObject("Probe Map").AddComponent<MapLabMap>();
            MapLabPresets.Apply(environment, copy);
            report.AppendLine($"Environment preset: layers {string.Join(", ", environment.Layers.Select(layer => layer.Name))}; applied to a new map -> {string.Join(", ", copy.BakeLayers.Select(layer => layer.Name))}");
            failures += Check(report, copy.BakeLayers.Count == map.BakeLayers.Count && copy.MegaChunkPixels == map.MegaChunkPixels, "an environment preset brings the bake layers and sizes");
            Object.DestroyImmediate(copy.gameObject);
            return failures;
        }

        private static int Shots(StringBuilder report, MapLabMap map, Scene scene)
        {
            Section(report, "7. Pictures");
            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            SortingGroup.UpdateAllSortingGroups();
            Shoot(camera, map.Bounds.center, map.Bounds.height * 0.5f + 2f, ShotPath);
            report.AppendLine($"Baked map: {ShotPath}");
            WriteMapImage(map, MapImagePath);
            report.AppendLine($"Mega chunk thumbnails as the viewport draws them: {MapImagePath}");
            return Check(report, File.Exists(ShotPath) && File.Exists(MapImagePath), "pictures written");
        }

        private static void WriteMapImage(MapLabMap map, string path)
        {
            MapLabWorldData world = map.World;
            RectInt extent = world.Extent();
            int tiles = world.TilesPerSide;
            int side = tiles * MapPixelsPerTile;
            int width = extent.width * side + 1;
            int height = extent.height * side + 1;
            var pixels = new Color32[width * height];
            var background = new Color32(0x24, 0x27, 0x2C, 0xFF);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;
            var thumbnails = new MapLabMegaThumbnails();
            try
            {
                foreach (var mega in world.Megas)
                {
                    Color32[] source = thumbnails.Get(world, mega).GetPixels32();
                    int left = (mega.Index.x - extent.xMin) * side;
                    int bottom = (mega.Index.y - extent.yMin) * side;
                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            Color32 color = source[(y / MapPixelsPerTile) * tiles + x / MapPixelsPerTile];
                            bool chunkLine = x % (side / map.ChunksPerMega) == 0 || y % (side / map.ChunksPerMega) == 0;
                            bool megaLine = x == 0 || y == 0;
                            if (megaLine) color = new Color32(0x0E, 0x10, 0x13, 0xFF);
                            else if (chunkLine) color = Color32.Lerp(color, new Color32(255, 255, 255, 255), 0.22f);
                            pixels[(bottom + y) * width + left + x] = color;
                        }
                    }
                }
            }
            finally
            {
                thumbnails.Clear();
            }
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.SetPixels32(pixels);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        private static Dictionary<MapLabBakeStatus.State, int> StateCounts(MapLabMap map)
        {
            HashSet<Vector2Int> baked = MapLabMegaStatus.BakedMegas(map);
            return map.World.Megas.GroupBy(mega => MapLabMegaStatus.Of(mega, baked)).ToDictionary(group => group.Key, group => group.Count());
        }

        private static string Describe(Dictionary<MapLabBakeStatus.State, int> states)
        {
            return string.Join(", ", states.Select(pair => $"{MapLabWindowStyles.StateLabel(pair.Key)} {pair.Value}"));
        }

        private static int Check(StringBuilder report, bool passed, string what)
        {
            report.AppendLine($"{(passed ? "PASS" : "FAIL")}  {what}");
            return passed ? 0 : 1;
        }

        private static void Section(StringBuilder report, string title)
        {
            report.AppendLine();
            report.AppendLine(title);
        }
        #endregion
    }
}
