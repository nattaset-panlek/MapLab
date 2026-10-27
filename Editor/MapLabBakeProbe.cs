using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabBakeProbe
    {
        #region [Variables/Fields]
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_bake.txt";

        private const int Width = 1920;
        private const int Height = 1080;
        private const float GameplaySize = 6f;
        private const float OverviewSize = 17f;
        private const int VisibleDifference = 12;
        private const int DiffGain = 4;
        private static readonly int[] PixelDensities = { 96, 64 };
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Static Bake Report", false, 103)]
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
            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            var particles = Find<ParticleSystemRenderer>(scene);
            foreach (var particle in particles) particle.enabled = false;
            int originalDensity = map.BakePixelsPerUnit;
            var warnings = MapLabLayerStack.Layers(map).ToDictionary(layer => layer.Name, layer => layer.ChangedUnderPaint.ToList());

            var report = new StringBuilder();
            report.AppendLine($"MapLab static bake report  {DateTime.Now:yyyy-MM-dd HH:mm}  Unity {Application.unityVersion}  {SystemInfo.graphicsDeviceName}");
            report.AppendLine($"Scene: {MapLabSceneBuilder.ScenePath}  ({Width} x {Height}; particles and paint layers hidden in every shot so live and baked compare 1:1)");
            report.AppendLine();
            try
            {
                MapLabStaticBaker.Unbake(map);
                SetActive(map, false);
                foreach (var shape in Find<PointShape>(scene)) shape.Rebuild();
                List<Renderer> ground = MapLabStaticBaker.CollectGround(map);
                Rect gameplayView = View(GameplaySize);
                Texture2D liveGameplay = Capture(camera, GameplaySize);
                Texture2D liveOverview = Capture(camera, OverviewSize);
                double liveGameplayMs = MapLabPerformanceProbe.RenderMs(camera, Vector2.zero, GameplaySize);
                double liveOverviewMs = MapLabPerformanceProbe.RenderMs(camera, Vector2.zero, OverviewSize);
                List<Renderer> liveInView = ground.Where(renderer => Overlaps(renderer.bounds, gameplayView)).ToList();
                Save(liveGameplay, "MapLab_bake_live.png");

                report.AppendLine("## Ground layer (everything drawn below the actors)");
                report.AppendLine($"Live: {ground.Count} renderers, {DrawCalls(ground)} draw calls in the whole map, {liveInView.Count} renderers / {DrawCalls(liveInView)} draw calls inside the gameplay view");
                report.AppendLine();
                report.AppendLine("## Live vs baked");
                report.AppendLine("| Version | Layers / chunks | Chunk draws in gameplay view | Texture memory | Bake s | Gameplay ms | Whole map ms | Gameplay diff mean / max / pixels > 12 | Whole map diff mean / max / pixels > 12 |");
                report.AppendLine("|---|---|---|---|---|---|---|---|---|");
                report.AppendLine($"| Live | - | {DrawCalls(liveInView)} (renderers) | - | - | {liveGameplayMs:0.00} | {liveOverviewMs:0.00} | - | - |");

                string streaming = "";
                foreach (int density in PixelDensities)
                {
                    SetDensity(map, density);
                    var watch = Stopwatch.StartNew();
                    MapLabStaticBaker.Bake(map);
                    watch.Stop();
                    SetActive(map, false);
                    List<MapLabBakedLayer> layers = map.GetComponentsInChildren<MapLabBakedLayer>(true).ToList();
                    List<SpriteRenderer> chunks = layers.Where(layer => layer.ChunkLayer != null).SelectMany(layer => layer.ChunkLayer.Chunks).Where(chunk => chunk != null).ToList();
                    long memory = chunks.Sum(chunk => Profiler.GetRuntimeMemorySizeLong(chunk.sprite.texture));
                    int chunksInView = chunks.Count(chunk => Overlaps(chunk.bounds, gameplayView));

                    Texture2D bakedGameplay = Capture(camera, GameplaySize);
                    Texture2D bakedOverview = Capture(camera, OverviewSize);
                    double gameplayMs = MapLabPerformanceProbe.RenderMs(camera, Vector2.zero, GameplaySize);
                    double overviewMs = MapLabPerformanceProbe.RenderMs(camera, Vector2.zero, OverviewSize);
                    string gameplayDiff = Difference(liveGameplay, bakedGameplay, $"MapLab_bake_diff_{density}.png");
                    string overviewDiff = Difference(liveOverview, bakedOverview, null);
                    Save(bakedGameplay, $"MapLab_bake_baked_{density}.png");
                    report.AppendLine($"| Baked {density} px/unit | {layers.Count} / {chunks.Count} of {MapLabChunkRenderer.SnappedChunkSize(map):0.##} units + {layers.Where(layer => layer.SortedLayer != null).Sum(layer => layer.SortedLayer.Pieces.Count)} sorted images | {chunksInView} | {memory / (1024f * 1024f):0.0} MB | {watch.Elapsed.TotalSeconds:0.0} | {gameplayMs:0.00} | {overviewMs:0.00} | {gameplayDiff} | {overviewDiff} |");

                    var streamer = MapLabLayerBaker.Root(map, false).GetComponent<MapLabChunkStreamer>();
                    streamer.ShowArea(gameplayView);
                    int nearPlayer = streamer.ActiveChunks;
                    streamer.ShowArea(new Rect(gameplayView.position + new Vector2(30f, 0f), gameplayView.size));
                    int farAway = streamer.ActiveChunks;
                    streamer.ShowAll();
                    streaming = $"Chunk streamer (view + 2 units margin): {nearPlayer} of {chunks.Count} chunks active at the gameplay view, {farAway} when the view moves 30 units east";

                    Object.DestroyImmediate(bakedGameplay);
                    Object.DestroyImmediate(bakedOverview);
                }
                report.AppendLine();
                report.AppendLine(streaming);
                report.AppendLine("Diff = largest RGB channel difference per pixel, 0 to 255. Diff images (x4) and the baked shots are next to this report.");

                Object.DestroyImmediate(liveGameplay);
                Object.DestroyImmediate(liveOverview);
            }
            finally
            {
                SetDensity(map, originalDensity);
                foreach (var layer in MapLabLayerStack.Layers(map))
                {
                    if (warnings.TryGetValue(layer.Name, out List<Vector2Int> chunks)) layer.SetChangedUnderPaint(chunks);
                }
                foreach (var particle in particles)
                {
                    if (particle != null) particle.enabled = true;
                }
            }

            report.AppendLine();
            report.AppendLine(MapLabStaticBaker.Bake(map));
            foreach (var layer in MapLabLayerStack.Layers(map))
            {
                if (warnings.TryGetValue(layer.Name, out List<Vector2Int> chunks)) layer.SetChangedUnderPaint(chunks);
            }
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            File.WriteAllText(ReportPath, report.ToString());
            return $"[MapLab] Static bake report written to {ReportPath}\n{report}";
        }
        #endregion

        #region [Private Methods]
        private static void SetActive(MapLabMap map, bool active)
        {
            foreach (var paint in map.GetComponentsInChildren<MapLabChunkLayer>(true).Where(layer => layer.IsPaint)) paint.gameObject.SetActive(active);
            foreach (var sorted in map.GetComponentsInChildren<MapLabSortedLayer>(true))
            {
                foreach (var paint in sorted.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer.name.StartsWith(MapLabLayerBaker.PaintPrefix))) paint.gameObject.SetActive(active);
            }
        }

        private static void SetDensity(MapLabMap map, int density)
        {
            var serialized = new SerializedObject(map);
            serialized.FindProperty("_bakePixelsPerUnit").intValue = density;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Texture2D Capture(Camera camera, float size)
        {
            Transform cameraTransform = camera.transform;
            Vector3 previousPosition = cameraTransform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cameraTransform.position = new Vector3(0f, 0f, previousPosition.z);
            camera.orthographicSize = size;
            camera.targetTexture = target;
            SortingGroup.UpdateAllSortingGroups();
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            cameraTransform.position = previousPosition;
            camera.orthographicSize = previousSize;
            target.Release();
            Object.DestroyImmediate(target);
            return image;
        }

        private static string Difference(Texture2D live, Texture2D baked, string imageName)
        {
            Color32[] a = live.GetPixels32();
            Color32[] b = baked.GetPixels32();
            var diff = imageName != null ? new Color32[a.Length] : null;
            long sum = 0;
            int max = 0;
            int visible = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                sum += d;
                max = Mathf.Max(max, d);
                if (d > VisibleDifference) visible++;
                if (diff == null) continue;
                byte shade = (byte)Mathf.Min(255, d * DiffGain);
                diff[i] = new Color32(shade, shade, shade, 255);
            }
            if (diff != null)
            {
                var image = new Texture2D(live.width, live.height, TextureFormat.RGBA32, false);
                image.SetPixels32(diff);
                image.Apply();
                Save(image, imageName);
                Object.DestroyImmediate(image);
            }
            return $"{(double)sum / a.Length:0.00} / {max} / {100.0 * visible / a.Length:0.00}%";
        }

        private static void Save(Texture2D image, string fileName)
        {
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            File.WriteAllBytes(Path.Combine(MapLabPreviewRenderer.OutputFolder, fileName), image.EncodeToPNG());
        }

        private static Rect View(float size)
        {
            float halfWidth = size * Width / Height;
            return new Rect(-halfWidth, -size, halfWidth * 2f, size * 2f);
        }

        private static bool Overlaps(Bounds bounds, Rect view)
        {
            return bounds.max.x > view.xMin && bounds.min.x < view.xMax && bounds.max.y > view.yMin && bounds.min.y < view.yMax;
        }

        private static int DrawCalls(IEnumerable<Renderer> renderers)
        {
            int draws = 0;
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    draws++;
                    continue;
                }
                int submeshes = Mathf.Min(filter.sharedMesh.subMeshCount, renderer.sharedMaterials.Length);
                for (int i = 0; i < submeshes; i++)
                {
                    if (filter.sharedMesh.GetIndexCount(i) > 0) draws++;
                }
            }
            return draws;
        }

        private static List<T> Find<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }
        #endregion
    }
}
