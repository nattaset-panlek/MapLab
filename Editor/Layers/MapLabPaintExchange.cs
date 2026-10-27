using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabPaintExchange
    {
        #region [Variables/Fields]
        public const int MaxPngSide = 16384;

        private static readonly Regex OrderPrefix = new Regex(@"^(\d+)[_ ]+");

        private sealed class Found
        {
            public string Layer;
            public string Paint;
            public byte[] Pixels;
        }

        private sealed class Report
        {
            public readonly List<Found> Paints = new List<Found>();
            public readonly List<string> Order = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> Unknown = new List<string>();
            public readonly List<string> Notes = new List<string>();
        }
        #endregion

        #region [Public Methods]
        public static MapLabPaintCanvas CanvasFor(MapLabMap map)
        {
            float chunkSize = MapLabChunkRenderer.SnappedChunkSize(map);
            RectInt range = MapLabChunkLayer.ChunkRange(map.Bounds, chunkSize);
            Vector2Int min = range.min;
            Vector2Int max = range.max;
            foreach (var entry in MapLabLayerStack.Resolve(map))
            {
                foreach (var index in MapLabLayerFiles.Chunks(Folder(map, entry)).Keys)
                {
                    min = Vector2Int.Min(min, index);
                    max = Vector2Int.Max(max, index + Vector2Int.one);
                }
            }
            return new MapLabPaintCanvas(min, max - min, chunkSize, map.BakePixelsPerUnit);
        }

        public static MapLabPaintCanvas CanvasForMega(MapLabMap map, Vector2Int mega)
        {
            int perMega = MapLabLayerFiles.PerMega(map);
            RectInt chunks = MegaChunkFormula.ChunksOf(mega, perMega);
            return new MapLabPaintCanvas(chunks.min, chunks.size, MapLabChunkRenderer.SnappedChunkSize(map), map.BakePixelsPerUnit);
        }

        public static List<Vector2Int> MegasWithArt(MapLabMap map)
        {
            int perMega = MapLabLayerFiles.PerMega(map);
            var megas = new HashSet<Vector2Int>();
            if (map.World != null) megas.UnionWith(map.World.Megas.Select(mega => mega.Index));
            foreach (var entry in MapLabLayerStack.Resolve(map)) megas.UnionWith(MapLabLayerFiles.Megas(Folder(map, entry), perMega));
            return megas.OrderBy(mega => mega.y).ThenBy(mega => mega.x).ToList();
        }

        public static string MegaPsdName(Vector2Int mega)
        {
            return $"{MegaChunkFormula.FolderName(mega)}.psd";
        }

        public static string ExportMegaPsds(MapLabMap map, string folder, IEnumerable<Vector2Int> megas = null)
        {
            List<Vector2Int> list = (megas ?? MegasWithArt(map)).Distinct().ToList();
            if (list.Count == 0) throw new InvalidOperationException("Nothing to export yet: add mega chunks or bake a layer first.");
            Directory.CreateDirectory(folder);
            var notes = new List<string>();
            int pixels = 0;
            foreach (var mega in list)
            {
                MapLabPaintCanvas canvas = CanvasForMega(map, mega);
                pixels = canvas.Width;
                string result = ExportPsd(map, Path.Combine(folder, MegaPsdName(mega)), canvas);
                int split = result.IndexOf("\n- ", StringComparison.Ordinal);
                if (split >= 0) notes.AddRange(result.Substring(split + 3).Split(new[] { "\n- " }, StringSplitOptions.None).Select(note => $"{MegaChunkFormula.FolderName(mega)}: {note}"));
            }
            return Summary($"[MapLab] Exported {list.Count} mega chunk PSDs of '{map.name}' to {folder} ({pixels} x {pixels} px each)", notes.Distinct().ToList());
        }

        public static string ImportPsdFolder(MapLabMap map, string folder)
        {
            List<string> files = Directory.GetFiles(folder, "*.psd").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) throw new InvalidOperationException($"No PSD files in {folder}.");
            var lines = new List<string>();
            foreach (var file in files) lines.Add(ImportPsd(map, file).Replace("[MapLab] ", ""));
            return Summary($"[MapLab] Imported {files.Count} PSD files from {folder}", lines);
        }

        public static string ExportPsd(MapLabMap map, string path)
        {
            return ExportPsd(map, path, CanvasFor(map));
        }

        public static string ExportPsd(MapLabMap map, string path, MapLabPaintCanvas canvas)
        {
            if (canvas.Width > MapLabPsd.MaxSide || canvas.Height > MapLabPsd.MaxSide)
            {
                throw new InvalidOperationException($"The paint canvas would be {canvas.Width} x {canvas.Height} px; a PSD allows {MapLabPsd.MaxSide}. Lower Bake Pixels Per Unit or the map Bounds.");
            }
            var notes = new List<string>();
            var document = new MapLabPsd.Document { Width = canvas.Width, Height = canvas.Height };
            var composite = new byte[canvas.Width * canvas.Height * 4];
            var full = new RectInt(0, 0, canvas.Width, canvas.Height);
            foreach (var entry in MapLabLayerStack.Resolve(map))
            {
                byte[] pixels = Fill(map, canvas, entry, notes);
                MapLabPaintCanvas.Over(composite, canvas.Width, canvas.Height, pixels, full, 255);
                document.Layers.Add(PsdLayer(entry.Name, !entry.IsPaint, true, pixels, canvas));
            }
            document.Layers.Add(PsdLayer(canvas.Label, true, false, canvas.DrawGrid(), canvas));
            MapLabPsd.Write(path, document, composite);
            return Summary($"[MapLab] Exported {document.Layers.Count - 1} layers of '{map.name}' to {path} ({canvas.Width} x {canvas.Height} px)", notes);
        }

        public static string ExportPng(MapLabMap map, string folder)
        {
            MapLabPaintCanvas canvas = CanvasFor(map);
            if (canvas.Width > MaxPngSide || canvas.Height > MaxPngSide)
            {
                throw new InvalidOperationException($"The paint canvas would be {canvas.Width} x {canvas.Height} px; PNG export allows {MaxPngSide}. Use PSD, or lower Bake Pixels Per Unit.");
            }
            Directory.CreateDirectory(folder);
            var notes = new List<string>();
            List<MapLabLayerStack.Entry> stack = MapLabLayerStack.Resolve(map);
            for (int i = 0; i < stack.Count; i++)
            {
                MapLabLayerFiles.WritePng(Path.Combine(folder, $"{i + 1:00}_{stack[i].Name}.png"), Fill(map, canvas, stack[i], notes), canvas.Width, canvas.Height);
            }
            MapLabLayerFiles.WritePng(Path.Combine(folder, $"{canvas.Label}.png"), canvas.DrawGrid(), canvas.Width, canvas.Height);
            return Summary($"[MapLab] Exported {stack.Count} layers of '{map.name}' as PNG files to {folder} ({canvas.Width} x {canvas.Height} px)", notes);
        }

        public static string ImportPsd(MapLabMap map, string path)
        {
            MapLabPsd.Document document = MapLabPsd.Read(path);
            MapLabPaintCanvas canvas = FindCanvas(map, AllNames(document.Layers), document.Width, document.Height);
            var report = new Report();
            List<string> layers = MapLabLayerStack.Layers(map).Select(layer => layer.Name).ToList();
            foreach (var layer in document.Layers) Visit(layer, layers, canvas, report);
            return Apply(map, canvas, report, path);
        }

        public static string ImportPng(MapLabMap map, string folder)
        {
            List<string> files = Directory.GetFiles(folder, "*.png").OrderBy(OrderOf).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            var names = files.Select(Path.GetFileNameWithoutExtension).ToList();
            MapLabPaintCanvas canvas = FindCanvas(map, names, -1, -1);
            var report = new Report();
            List<string> layers = MapLabLayerStack.Layers(map).Select(layer => layer.Name).ToList();
            foreach (var file in files)
            {
                string name = OrderPrefix.Replace(Path.GetFileNameWithoutExtension(file), "");
                if (MapLabPaintCanvas.TryParse(name, out _)) continue;
                if (MapLabLayerNames.IsBakeLayer(name, layers))
                {
                    report.Order.Add(name);
                    continue;
                }
                if (!MapLabLayerNames.TryParsePaint(name, layers, out string layer, out string paint))
                {
                    Classify(name, report);
                    continue;
                }
                byte[] pixels = MapLabLayerFiles.ReadPng(file, out int width, out int height);
                if (width != canvas.Width || height != canvas.Height)
                {
                    report.Notes.Add($"'{Path.GetFileName(file)}' is {width} x {height} px, the canvas is {canvas.Width} x {canvas.Height}: not imported");
                    continue;
                }
                report.Paints.Add(new Found { Layer = layer, Paint = Clean(paint), Pixels = pixels });
                report.Order.Add(Clean(paint));
            }
            return Apply(map, canvas, report, folder);
        }
        #endregion

        #region [Private Methods]
        private static string Folder(MapLabMap map, MapLabLayerStack.Entry entry)
        {
            if (entry.IsPaint) return MapLabLayerFiles.PaintFolder(map, entry.Layer, entry.Name);
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, entry.Name);
            return MapLabLayerFiles.ImageFolder(map, entry.Name, settings != null && settings.Sorted);
        }

        private static byte[] Fill(MapLabMap map, MapLabPaintCanvas canvas, MapLabLayerStack.Entry entry, List<string> notes)
        {
            var pixels = new byte[canvas.Width * canvas.Height * 4];
            Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(Folder(map, entry));
            if (chunks.Count == 0 && !entry.IsPaint) notes.Add($"'{entry.Name}' is not baked yet, so its layer is empty");
            int skipped = 0;
            foreach (var pair in chunks)
            {
                if (!canvas.Contains(pair.Key)) continue;
                Texture2D image = MapLabLayerFiles.ReadPixels(pair.Value);
                if (!canvas.Paste(pixels, pair.Key, image.GetPixels32(), image.width)) skipped++;
                Object.DestroyImmediate(image);
            }
            if (skipped > 0) notes.Add($"'{entry.Name}': {skipped} chunks were made at another chunk size or px/unit and were left out");
            return pixels;
        }

        private static MapLabPsd.Layer PsdLayer(string name, bool locked, bool visible, byte[] pixels, MapLabPaintCanvas canvas)
        {
            RectInt bounds = MapLabPsd.Bounds(pixels, canvas.Width, canvas.Height);
            return new MapLabPsd.Layer
            {
                Name = name,
                Locked = locked,
                Visible = visible,
                Rect = bounds,
                Pixels = bounds.width > 0 ? MapLabPsd.Crop(pixels, canvas.Width, bounds) : null
            };
        }

        private static MapLabPaintCanvas FindCanvas(MapLabMap map, IEnumerable<string> names, int width, int height)
        {
            MapLabPaintCanvas canvas = default;
            bool labelled = names.Any(name => MapLabPaintCanvas.TryParse(name, out canvas));
            if (!labelled) canvas = CanvasFor(map);
            if (canvas.PixelsPerUnit != map.BakePixelsPerUnit)
            {
                throw new InvalidOperationException($"The file was exported at {canvas.PixelsPerUnit} px/unit and the map now bakes at {map.BakePixelsPerUnit}. Set Bake Pixels Per Unit back to {canvas.PixelsPerUnit}, or export again and repaint.");
            }
            if (width >= 0 && (width != canvas.Width || height != canvas.Height))
            {
                string grid = labelled ? "the size written in its MapLab Grid layer" : "the map's canvas (the MapLab Grid layer is missing)";
                throw new InvalidOperationException($"The file is {width} x {height} px but {grid} is {canvas.Width} x {canvas.Height}. Do not resize or crop the canvas; export again if the map grew.");
            }
            return canvas;
        }

        private static IEnumerable<string> AllNames(List<MapLabPsd.Layer> layers)
        {
            foreach (var layer in layers)
            {
                yield return layer.Name;
                if (!layer.IsGroup) continue;
                foreach (var name in AllNames(layer.Children)) yield return name;
            }
        }

        private static void Visit(MapLabPsd.Layer layer, List<string> layers, MapLabPaintCanvas canvas, Report report)
        {
            if (MapLabPaintCanvas.TryParse(layer.Name, out _)) return;
            if (MapLabLayerNames.IsBakeLayer(layer.Name, layers))
            {
                report.Order.Add(layer.Name.Trim());
                return;
            }
            if (MapLabLayerNames.TryParsePaint(layer.Name, layers, out string bakeLayer, out string paint))
            {
                var pixels = new byte[canvas.Width * canvas.Height * 4];
                Compose(layer, pixels, canvas, report, true);
                report.Paints.Add(new Found { Layer = bakeLayer, Paint = Clean(paint), Pixels = pixels });
                report.Order.Add(Clean(paint));
                return;
            }
            if (layer.IsGroup)
            {
                foreach (var child in layer.Children) Visit(child, layers, canvas, report);
                return;
            }
            Classify(layer.Name, report);
        }

        private static void Compose(MapLabPsd.Layer layer, byte[] target, MapLabPaintCanvas canvas, Report report, bool root)
        {
            if (!root && !layer.Visible) return;
            if (layer.BlendMode != MapLabPsd.NormalBlend && layer.BlendMode != MapLabPsd.PassBlend) report.Notes.Add($"'{layer.Name}' uses blend mode '{layer.BlendMode}': merged as Normal");
            if (layer.Clipped) report.Notes.Add($"'{layer.Name}' is a clipping mask: merged as a normal layer");
            if (layer.HasMask) report.Notes.Add($"'{layer.Name}' has a layer mask: the mask is ignored, apply it first");
            if (!layer.IsGroup)
            {
                MapLabPaintCanvas.Over(target, canvas.Width, canvas.Height, layer.Pixels, layer.Rect, layer.Opacity);
                return;
            }
            var group = new byte[target.Length];
            foreach (var child in layer.Children) Compose(child, group, canvas, report, false);
            MapLabPaintCanvas.Over(target, canvas.Width, canvas.Height, group, new RectInt(0, 0, canvas.Width, canvas.Height), layer.Opacity);
        }

        private static void Classify(string name, Report report)
        {
            if (MapLabLayerNames.LooksLikePaint(name, out string unknown)) report.Unknown.Add($"'{name}' (no bake layer called '{unknown}')");
            else report.Skipped.Add($"'{name}'");
        }

        private static string Apply(MapLabMap map, MapLabPaintCanvas canvas, Report report, string source)
        {
            float chunkSize = MapLabChunkRenderer.SnappedChunkSize(map);
            int size = MapLabChunkRenderer.ChunkPixels(chunkSize, map.BakePixelsPerUnit);
            RectInt range = MapLabChunkLayer.ChunkRange(canvas.WorldRect, chunkSize);
            var written = new List<string>();
            int chunks = 0;
            int cleared = 0;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                foreach (var found in report.Paints)
                {
                    string paint = MapLabLayerFiles.PaintNames(map, found.Layer).FirstOrDefault(name => MapLabLayerStack.Same(name, found.Paint)) ?? found.Paint;
                    string folder = MapLabLayerFiles.PaintFolder(map, found.Layer, paint);
                    MapLabOutputPaths.EnsureFolder(folder);
                    Dictionary<Vector2Int, string> existing = MapLabLayerFiles.Chunks(folder);
                    foreach (var index in range.allPositionsWithin)
                    {
                        Color32[] pixels = canvas.Cut(found.Pixels, index, chunkSize, out bool empty);
                        if (empty)
                        {
                            if (!existing.ContainsKey(index)) continue;
                            MapLabLayerFiles.DeleteChunk(folder, index);
                            cleared++;
                            continue;
                        }
                        image.SetPixels32(pixels);
                        image.Apply();
                        written.Add(MapLabLayerFiles.Write(folder, index, image, MapLabLayerFiles.PerMega(map)));
                        chunks++;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
            MapLabLayerFiles.ConfigureChunks(written, map.BakePixelsPerUnit, size);
            foreach (var layer in report.Paints.Select(found => found.Layer).Distinct())
            {
                if (MapLabLayerBaker.IsSorted(map, layer)) MapLabSortedBaker.RecutPaint(map, layer);
            }

            List<string> current = MapLabLayerStack.NamesWithLine(map);
            MapLabLayerStack.Save(map, MapLabLayerStack.Merge(current, report.Order));
            MapLabLayerBaker.SyncScene(map);

            var notes = new List<string>(report.Notes.Distinct());
            if (report.Unknown.Count > 0) notes.Add($"Unknown bake layer, skipped: {string.Join(", ", report.Unknown)}");
            if (report.Skipped.Count > 0) notes.Add($"No paint syntax, skipped: {string.Join(", ", report.Skipped)}");
            string head = report.Paints.Count == 0
                ? $"[MapLab] No paint layer found in {source}. Name a layer or group <bake layer>_paint or <bake layer>_paint_<name>."
                : $"[MapLab] Imported {report.Paints.Count} paint layers into '{map.name}' from {source}: {chunks} chunks written, {cleared} erased chunks removed";
            return Summary(head, notes);
        }

        private static string Clean(string name)
        {
            return new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        }

        private static int OrderOf(string file)
        {
            Match match = OrderPrefix.Match(Path.GetFileNameWithoutExtension(file));
            return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
        }

        private static string Summary(string head, List<string> notes)
        {
            if (notes.Count == 0) return head;
            var text = new StringBuilder(head);
            foreach (var note in notes) text.Append("\n- ").Append(note);
            return text.ToString();
        }
        #endregion
    }
}
