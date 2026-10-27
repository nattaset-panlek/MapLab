using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabSortedProbe
    {
        #region [Variables/Fields]
        private const int Width = 1920;
        private const int Height = 1080;
        private const int VisibleDifference = 12;
        private const float ActorWidth = 0.8f;
        private const float ActorHeight = 1.6f;
        private const float ActorGap = 0.3f;
        private static readonly Color32 ActorColor = new Color32(230, 80, 160, 255);
        #endregion

        #region [Public Methods]
        public static string CompareWithActors(MapLabMap map, string layer, Camera camera, IEnumerable<(Vector2 Center, float Size, string Name)> views)
        {
            var report = new StringBuilder();
            List<GameObject> actors = PlaceActors(map);
            try
            {
                MapLabLayerBaker.Unbake(map, layer);
                var live = views.Select(view => (view, Capture(camera, view.Center, view.Size))).ToList();
                report.AppendLine(MapLabLayerBaker.Bake(map, layer));
                foreach (var (view, before) in live)
                {
                    Texture2D after = Capture(camera, view.Center, view.Size);
                    report.AppendLine($"{view.Name}: live vs baked with {actors.Count} stand-in actors in front of, behind and beside every shape: {Difference(before, after, $"MapLab_sorted_{view.Name}")}");
                    Object.DestroyImmediate(before);
                    Object.DestroyImmediate(after);
                }
            }
            finally
            {
                foreach (var actor in actors) Object.DestroyImmediate(actor);
            }
            return report.ToString().TrimEnd();
        }

        public static string FlattenCheck(MapLabMap map, string layer, Rect area)
        {
            string paint = $"{layer}_paint_probe";
            string folder = MapLabLayerFiles.PaintFolder(map, layer, paint);
            MapLabPaintCanvas canvas = MapLabPaintExchange.CanvasFor(map);
            var pixels = new byte[canvas.Width * canvas.Height * 4];
            Rect world = canvas.WorldRect;
            int ppu = canvas.PixelsPerUnit;
            for (int y = Mathf.Max(0, Mathf.RoundToInt((world.yMax - area.yMax) * ppu)); y < Mathf.Min(canvas.Height, Mathf.RoundToInt((world.yMax - area.yMin) * ppu)); y++)
            {
                for (int x = Mathf.Max(0, Mathf.RoundToInt((area.xMin - world.xMin) * ppu)); x < Mathf.Min(canvas.Width, Mathf.RoundToInt((area.xMax - world.xMin) * ppu)); x++)
                {
                    int i = (y * canvas.Width + x) * 4;
                    pixels[i] = 40;
                    pixels[i + 1] = 200;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = 160;
                }
            }
            MapLabOutputPaths.EnsureFolder(folder);
            var image = new Texture2D(canvas.ChunkPixels, canvas.ChunkPixels, TextureFormat.RGBA32, false);
            var written = new List<string>();
            foreach (var index in canvas.Range.allPositionsWithin)
            {
                Color32[] cut = canvas.Cut(pixels, index, canvas.ChunkSize, out bool empty);
                if (empty) continue;
                image.SetPixels32(cut);
                image.Apply();
                written.Add(MapLabLayerFiles.Write(folder, index, image, MapLabLayerFiles.PerMega(map)));
            }
            Object.DestroyImmediate(image);
            MapLabLayerFiles.ConfigureChunks(written, ppu, canvas.ChunkPixels);
            bool sorted = MapLabLayerBaker.IsSorted(map, layer);
            if (sorted) MapLabSortedBaker.RecutPaint(map, layer);
            MapLabLayerBaker.SyncScene(map);

            string images = MapLabLayerFiles.BaseFolder(map, layer);
            Dictionary<string, Hash128> before = Hashes(images);
            int cutPieces = sorted ? Directory.GetFiles(MapLabLayerFiles.CutFolder(map, layer, paint), "*.png").Length : 0;
            string message = MapLabLayerFlatten.Flatten(map, layer);
            Dictionary<string, Hash128> after = Hashes(images);
            int changed = after.Count(pair => !before.TryGetValue(pair.Key, out Hash128 hash) || hash != pair.Value);
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer);
            bool locked = settings.Locked;
            bool paintGone = !Directory.Exists(folder);
            string history = Directory.GetDirectories(MapLabLayerFiles.HistoryRoot(map, layer)).OrderByDescending(Path.GetFileName).First();
            string keptFolder = Path.Combine(history, paint);
            bool kept = Directory.Exists(keptFolder) && Directory.GetFiles(keptFolder, "*.png", SearchOption.AllDirectories).Length == written.Count;

            MapLabLayerEdits.SetLocked(map, settings, false);
            RestoreOtherPaint(map, layer, history, paint);
            string rebake = MapLabLayerBaker.Bake(map, layer);
            Directory.Delete(history, true);
            string pieces = sorted ? $", cut onto {cutPieces} pieces" : "";
            string firstLine = rebake.Split('\n')[0];
            return $"{message}\nTemporary paint over {area}{pieces}: {changed} baked images changed, layer locked {locked}, paint folder removed {paintGone}, paint kept in History~ {kept}\nCleanup, unlocked and rebaked: {firstLine}";
        }
        #endregion

        #region [Private Methods]
        private static void RestoreOtherPaint(MapLabMap map, string layer, string history, string probe)
        {
            var written = new List<string>();
            foreach (var folder in Directory.GetDirectories(history))
            {
                string name = Path.GetFileName(folder);
                if (name == probe || name == MapLabLayerFiles.CutFolderName) continue;
                string target = MapLabLayerFiles.PaintFolder(map, layer, name);
                MapLabOutputPaths.EnsureFolder(target);
                foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(folder.Length).Replace('\\', '/').TrimStart('/');
                    string copy = $"{target}/{relative}";
                    MapLabLayerFiles.EnsureDirectory(Path.GetDirectoryName(copy).Replace('\\', '/'));
                    File.Copy(file, copy, true);
                    written.Add(copy);
                }
            }
            MapLabLayerFiles.ConfigureChunks(written, map.BakePixelsPerUnit, MapLabChunkRenderer.ChunkPixels(MapLabChunkRenderer.SnappedChunkSize(map), map.BakePixelsPerUnit));
        }

        private static List<GameObject> PlaceActors(MapLabMap map)
        {
            var texture = new Texture2D(8, 16, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            texture.SetPixels32(Enumerable.Repeat(ActorColor, 8 * 16).ToArray());
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), new Vector2(0.5f, 0f), 8f / ActorWidth);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            var actors = new List<GameObject>();
            foreach (var shape in map.GetComponentsInChildren<ExtrudedShape>(true).Where(shape => shape.YSort && !shape.IsStacked))
            {
                var collider = shape.GetComponentInChildren<PolygonCollider2D>(true);
                if (collider == null) continue;
                Bounds bounds = collider.bounds;
                var spots = new[]
                {
                    new Vector2(bounds.center.x, bounds.min.y - ActorGap),
                    new Vector2(bounds.center.x, bounds.center.y),
                    new Vector2(bounds.max.x + ActorGap, bounds.center.y),
                    new Vector2(bounds.min.x - ActorGap, bounds.max.y)
                };
                foreach (var spot in spots)
                {
                    var actor = new GameObject("Probe Actor") { hideFlags = HideFlags.DontSave };
                    actor.transform.position = new Vector3(spot.x, spot.y, 0f);
                    var renderer = actor.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.sortingLayerName = map.ActorSortingLayer;
                    renderer.sortingOrder = map.ActorSortingOrder;
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                    actors.Add(actor);
                }
            }
            return actors;
        }

        private static Texture2D Capture(Camera camera, Vector2 center, float size)
        {
            Transform cameraTransform = camera.transform;
            Vector3 previousPosition = cameraTransform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                cameraTransform.position = new Vector3(center.x, center.y, previousPosition.z);
                camera.orthographicSize = size;
                camera.targetTexture = target;
                SortingGroup.UpdateAllSortingGroups();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                cameraTransform.position = previousPosition;
                camera.orthographicSize = previousSize;
                target.Release();
                Object.DestroyImmediate(target);
            }
            return image;
        }

        private static string Difference(Texture2D live, Texture2D baked, string name)
        {
            Color32[] a = live.GetPixels32();
            Color32[] b = baked.GetPixels32();
            var diff = new Color32[a.Length];
            long sum = 0;
            int max = 0;
            int visible = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                sum += d;
                max = Mathf.Max(max, d);
                if (d > VisibleDifference) visible++;
                byte shade = (byte)Mathf.Min(255, d * 4);
                diff[i] = new Color32(shade, shade, shade, 255);
            }
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            File.WriteAllBytes(Path.Combine(MapLabPreviewRenderer.OutputFolder, $"{name}_live.png"), live.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(MapLabPreviewRenderer.OutputFolder, $"{name}_baked.png"), baked.EncodeToPNG());
            var image = new Texture2D(live.width, live.height, TextureFormat.RGBA32, false);
            image.SetPixels32(diff);
            image.Apply();
            File.WriteAllBytes(Path.Combine(MapLabPreviewRenderer.OutputFolder, $"{name}_diff.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            return $"mean {(double)sum / a.Length:0.00} / max {max} / {100.0 * visible / a.Length:0.00}% pixels differ by more than {VisibleDifference}";
        }

        private static Dictionary<string, Hash128> Hashes(string folder)
        {
            if (!Directory.Exists(folder)) return new Dictionary<string, Hash128>();
            return Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories).ToDictionary(Path.GetFileName, path => Hash128.Compute(File.ReadAllBytes(path)));
        }
        #endregion
    }
}
