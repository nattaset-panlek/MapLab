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
    public static class MapLabLayerProbe
    {
        #region [Variables/Fields]
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_layers.txt";

        private const string SamplePsdPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_sample_paint.psd";
        private const string ExportPsdPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_layers_export.psd";
        private const string ShotPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_layers.png";
        private const string GroundLayer = "Ground";
        private const string ShadowLayer = "Shadows";
        private const string WallLayer = "Walls";
        private const string PlazaName = "Plaza";
        private const int ShotWidth = 1920;
        private const int ShotHeight = 1080;
        private const float ShotSize = 7f;
        private const int Seed = 1607;
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Bake Layers Report", false, 105)]
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
            Scene scene = EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            MapLabMap map = MapLabWalkableBaker.FindMaps(scene).First();
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            var report = new StringBuilder();
            report.AppendLine($"MapLab bake layers report  {DateTime.Now:yyyy-MM-dd HH:mm}  Unity {Application.unityVersion}");
            report.AppendLine($"Scene: {MapLabSceneBuilder.ScenePath}");

            Section(report, "1. Bake all layers");
            report.AppendLine(MapLabStaticBaker.Bake(map));

            Section(report, "1b. Bake again redraws only what changed");
            report.AppendLine($"Nothing changed: {MapLabLayerBaker.Bake(map, GroundLayer)}");
            Transform road = map.GetComponentsInChildren<PathShape>(true).First().transform;
            road.position += Vector3.right * 0.5f;
            report.AppendLine($"'{road.name}' moved 0.5 units: {MapLabLayerBaker.Bake(map, GroundLayer)}");
            road.position -= Vector3.right * 0.5f;
            report.AppendLine($"Moved back: {MapLabLayerBaker.Bake(map, GroundLayer)}");
            report.AppendLine($"Every chunk again: {MapLabLayerBaker.Bake(map, GroundLayer, null, true)}");
            MapLabLayerEdits.MarkReviewed(map, MapLabLayerStack.Find(map, GroundLayer));

            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            Section(report, "2. The sorted layer looks and sorts like the live shapes");
            var views = new[] { (MapLabGroundBuilder.PlazaCenter, ShotSize, "plaza"), (Vector2.zero, 17f, "map") };
            report.AppendLine(MapLabSortedProbe.CompareWithActors(map, WallLayer, camera, views));

            Section(report, "3. Flatten (temporary paint, then cleaned up)");
            var area = new Rect(MapLabGroundBuilder.PlazaCenter - new Vector2(4f, 3f), new Vector2(8f, 6f));
            report.AppendLine(MapLabSortedProbe.FlattenCheck(map, ShadowLayer, area));
            report.AppendLine(MapLabSortedProbe.FlattenCheck(map, WallLayer, area));

            Section(report, "4. Sample paint, imported from a PSD");
            if (MapLabLayerFiles.PaintNames(map, WallLayer).Count == 0)
            {
                WriteSamplePsd(map, SamplePsdPath);
                report.AppendLine(MapLabPaintExchange.ImportPsd(map, SamplePsdPath));
            }
            else
            {
                report.AppendLine("Sample paint is already on disk");
            }
            report.AppendLine($"Stack, bottom to top: {string.Join(", ", MapLabLayerStack.Resolve(map).Select(entry => entry.Name))}");

            report.AppendLine($"Wall paint cut onto {CutPieces(map)} pieces of '{WallLayer}'");

            Section(report, "5. Rebake keeps the paint");
            Dictionary<string, Hash128> paint = PaintHashes(map);
            report.AppendLine(MapLabLayerBaker.Bake(map, GroundLayer));
            report.AppendLine(MapLabLayerBaker.Bake(map, WallLayer));
            report.AppendLine($"Paint files untouched: {Same(paint, PaintHashes(map))} ({paint.Count} chunks); wall paint cut again onto {CutPieces(map)} pieces");
            MapLabBakeLayerSettings ground = MapLabLayerStack.Find(map, GroundLayer);
            report.AppendLine($"Chunks flagged after an identical rebake: {ground.ChangedUnderPaint.Count}");

            Section(report, "6. A change under the paint is flagged");
            Transform plaza = map.GetComponentsInChildren<ExtrudedShape>(true).First(shape => shape.name == PlazaName).transform;
            plaza.position += Vector3.right * 0.5f;
            MapLabLayerBaker.Bake(map, GroundLayer);
            report.AppendLine($"Plaza moved 0.5 units, rebaked: {ground.ChangedUnderPaint.Count} painted chunks flagged ({string.Join("  ", ground.ChangedUnderPaint.Select(index => $"{index.x},{index.y}"))})");
            plaza.position -= Vector3.right * 0.5f;
            MapLabLayerBaker.Bake(map, GroundLayer);
            MapLabLayerEdits.MarkReviewed(map, ground);
            report.AppendLine($"Moved back, rebaked, marked reviewed: {ground.ChangedUnderPaint.Count} flagged; paint still untouched: {Same(paint, PaintHashes(map))}");

            Section(report, "7. Export to PSD, read it back, import it again");
            report.AppendLine(MapLabPaintExchange.ExportPsd(map, ExportPsdPath));
            MapLabPsd.Document exported = MapLabPsd.Read(ExportPsdPath);
            report.AppendLine($"PSD layers, bottom to top: {string.Join(", ", exported.Layers.Select(Describe))}");
            report.AppendLine(MapLabPaintExchange.ImportPsd(map, ExportPsdPath));
            report.AppendLine($"Paint identical after export and import: {Same(paint, PaintHashes(map))}");

            Section(report, "8. Draw order of the chunk layers");
            Transform root = MapLabLayerBaker.Root(map, false);
            foreach (var layer in root.GetComponentsInChildren<MapLabChunkLayer>(true))
            {
                SpriteRenderer first = layer.Chunks.FirstOrDefault(chunk => chunk != null);
                string order = first != null ? $"{SortingLayer.IDToName(first.sortingLayerID)} {first.sortingOrder}" : "no chunks";
                report.AppendLine($"{layer.name}: {layer.Chunks.Count(chunk => chunk != null)} chunks, order {order}");
            }
            report.AppendLine($"Actors draw at {map.ActorSortingLayer} {map.ActorSortingOrder}; the mist particles at {MapLabSceneBuilder.MistOrder} stay between Ground and Shadows");

            SaveShot(camera);
            report.AppendLine();
            report.AppendLine($"Shot: {ShotPath}");
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(ReportPath, report.ToString());
            return $"[MapLab] Bake layers report written to {ReportPath}\n{report}";
        }
        #endregion

        #region [Private Methods]
        private static void Section(StringBuilder report, string title)
        {
            report.AppendLine();
            report.AppendLine($"## {title}");
        }

        private static string Describe(MapLabPsd.Layer layer)
        {
            var flags = new List<string>();
            if (layer.Locked) flags.Add("locked");
            if (!layer.Visible) flags.Add("hidden");
            return flags.Count > 0 ? $"{layer.Name} ({string.Join(", ", flags)})" : layer.Name;
        }

        private static void WriteSamplePsd(MapLabMap map, string path)
        {
            MapLabPaintCanvas canvas = MapLabPaintExchange.CanvasFor(map);
            var random = new System.Random(Seed);
            byte[] dirt = Blobs(canvas, random, 26, new Color32(96, 78, 58, 255), 0.4f, 1.1f, 0.42f);
            byte[] moss = Blobs(canvas, random, 18, new Color32(70, 104, 66, 255), 0.3f, 0.8f, 0.5f);
            byte[] sketch = Blobs(canvas, random, 4, new Color32(230, 60, 60, 255), 1f, 2f, 1f);
            byte[] ivy = Blobs(canvas, random, 40, new Color32(54, 92, 60, 255), 0.25f, 0.6f, 0.75f);
            var full = new RectInt(0, 0, canvas.Width, canvas.Height);
            var document = new MapLabPsd.Document { Width = canvas.Width, Height = canvas.Height };
            document.Layers.Add(new MapLabPsd.Layer { Name = GroundLayer, Locked = true });
            document.Layers.Add(new MapLabPsd.Layer { Name = "Ground_paint", Rect = full, Pixels = dirt });
            document.Layers.Add(new MapLabPsd.Layer
            {
                Name = "ground_paint_moss",
                Children = new List<MapLabPsd.Layer> { new MapLabPsd.Layer { Name = "moss strokes", Rect = full, Pixels = moss } }
            });
            document.Layers.Add(new MapLabPsd.Layer { Name = ShadowLayer, Locked = true });
            document.Layers.Add(new MapLabPsd.Layer { Name = WallLayer, Locked = true });
            document.Layers.Add(new MapLabPsd.Layer { Name = "Walls_paint_ivy", Rect = full, Pixels = ivy });
            document.Layers.Add(new MapLabPsd.Layer { Name = "sketch idea", Visible = false, Rect = full, Pixels = sketch });
            document.Layers.Add(new MapLabPsd.Layer { Name = "Cave_paint", Rect = full, Pixels = sketch });
            document.Layers.Add(new MapLabPsd.Layer { Name = canvas.Label, Visible = false, Locked = true, Rect = full, Pixels = canvas.DrawGrid() });
            var composite = new byte[canvas.Width * canvas.Height * 4];
            MapLabPaintCanvas.Over(composite, canvas.Width, canvas.Height, dirt, full, 255);
            MapLabPaintCanvas.Over(composite, canvas.Width, canvas.Height, moss, full, 255);
            MapLabPsd.Write(path, document, composite);
        }

        private static byte[] Blobs(MapLabPaintCanvas canvas, System.Random random, int count, Color32 color, float minRadius, float maxRadius, float alpha)
        {
            var pixels = new byte[canvas.Width * canvas.Height * 4];
            Rect world = canvas.WorldRect;
            int ppu = canvas.PixelsPerUnit;
            for (int i = 0; i < count; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                double distance = Math.Sqrt(random.NextDouble()) * 6.0;
                var center = MapLabGroundBuilder.PlazaCenter + new Vector2((float)(Math.Cos(angle) * distance * 1.3), (float)(Math.Sin(angle) * distance));
                float radius = Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble());
                int cx = Mathf.RoundToInt((center.x - world.xMin) * ppu);
                int cy = Mathf.RoundToInt((world.yMax - center.y) * ppu);
                int r = Mathf.CeilToInt(radius * ppu);
                for (int y = Mathf.Max(0, cy - r); y < Mathf.Min(canvas.Height, cy + r); y++)
                {
                    for (int x = Mathf.Max(0, cx - r); x < Mathf.Min(canvas.Width, cx + r); x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy) * 1.8f) / r;
                        if (d >= 1f) continue;
                        byte a = (byte)(alpha * 255f * Mathf.SmoothStep(1f, 0f, d));
                        int index = (y * canvas.Width + x) * 4;
                        if (a <= pixels[index + 3]) continue;
                        pixels[index] = color.r;
                        pixels[index + 1] = color.g;
                        pixels[index + 2] = color.b;
                        pixels[index + 3] = a;
                    }
                }
            }
            return pixels;
        }

        private static int CutPieces(MapLabMap map)
        {
            string root = MapLabLayerFiles.CutRoot(map, WallLayer);
            return Directory.Exists(root) ? Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Length : 0;
        }

        private static Dictionary<string, Hash128> PaintHashes(MapLabMap map)
        {
            var hashes = new Dictionary<string, Hash128>();
            foreach (var layer in MapLabLayerStack.Layers(map))
            {
                foreach (var paint in MapLabLayerFiles.PaintNames(map, layer.Name))
                {
                    foreach (var pair in MapLabLayerFiles.Hashes(MapLabLayerFiles.PaintFolder(map, layer.Name, paint))) hashes[$"{paint}/{pair.Key}"] = pair.Value;
                }
            }
            return hashes;
        }

        private static bool Same(Dictionary<string, Hash128> a, Dictionary<string, Hash128> b)
        {
            return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out Hash128 hash) && hash == pair.Value);
        }

        private static void SaveShot(Camera camera)
        {
            Vector3 previousPosition = camera.transform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(MapLabGroundBuilder.PlazaCenter.x, MapLabGroundBuilder.PlazaCenter.y, previousPosition.z);
                camera.orthographicSize = ShotSize;
                camera.targetTexture = target;
                SortingGroup.UpdateAllSortingGroups();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                image.Apply();
                File.WriteAllBytes(ShotPath, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.transform.position = previousPosition;
                camera.orthographicSize = previousSize;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
        #endregion
    }
}
