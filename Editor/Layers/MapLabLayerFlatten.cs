using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabLayerFlatten
    {
        #region [Variables/Fields]
        private const string HistoryLabel = "_flattened";
        #endregion

        #region [Public Methods]
        public static List<string> Paints(MapLabMap map, string layer)
        {
            return MapLabLayerStack.Resolve(map).Where(entry => entry.IsPaint && MapLabLayerStack.Same(entry.Layer, layer)).Select(entry => entry.Name).ToList();
        }

        public static bool PaintIsApart(MapLabMap map, string layer)
        {
            List<MapLabLayerStack.Entry> stack = MapLabLayerStack.Resolve(map);
            int index = stack.FindIndex(entry => !entry.IsPaint && MapLabLayerStack.Same(entry.Name, layer));
            if (index < 0) return false;
            int above = index + 1;
            while (above < stack.Count && stack[above].IsPaint && MapLabLayerStack.Same(stack[above].Layer, layer)) above++;
            return stack.Skip(above).Any(entry => entry.IsPaint && MapLabLayerStack.Same(entry.Layer, layer)) || stack.Take(index).Any(entry => entry.IsPaint && MapLabLayerStack.Same(entry.Layer, layer));
        }

        public static string Flatten(MapLabMap map, string layer)
        {
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer);
            if (settings == null) throw new InvalidOperationException($"There is no layer called '{layer}'.");
            if (settings.Locked) throw new InvalidOperationException($"'{layer}' is already locked.");
            if (MapLabLayerBaker.BakedLayer(map, layer) == null) throw new InvalidOperationException($"Bake '{layer}' before flattening its paint into it.");
            List<string> paints = Paints(map, layer);
            if (paints.Count == 0) throw new InvalidOperationException($"'{layer}' has no paint to flatten.");

            int images = settings.Sorted ? FlattenSorted(map, layer, paints) : FlattenGround(map, layer, paints);
            var folders = paints.Select(paint => MapLabLayerFiles.PaintFolder(map, layer, paint)).ToList();
            if (settings.Sorted) folders.Add(MapLabLayerFiles.CutRoot(map, layer));
            string history = MapLabLayerFiles.ArchiveFolders(map, layer, HistoryLabel, false, folders.ToArray());

            Undo.RecordObject(map, "Flatten MapLab Paint");
            settings.Locked = true;
            settings.SetChangedUnderPaint(Array.Empty<Vector2Int>());
            MapLabLayerStack.MarkDirty(map);
            if (settings.Sorted) MapLabLayerBaker.SyncScene(map);
            else MapLabLayerBaker.RefreshChunks(map, layer);
            return $"[MapLab] Flattened {paints.Count} paint layers into '{layer}' ({images} images rewritten) and locked it. The paint files are kept in {history}";
        }
        #endregion

        #region [Private Methods]
        private static int FlattenGround(MapLabMap map, string layer, List<string> paints)
        {
            string baseFolder = MapLabLayerFiles.BaseFolder(map, layer);
            Dictionary<Vector2Int, string> bases = MapLabLayerFiles.Chunks(baseFolder);
            List<Dictionary<Vector2Int, string>> paintChunks = paints.Select(paint => MapLabLayerFiles.Chunks(MapLabLayerFiles.PaintFolder(map, layer, paint))).ToList();
            int size = MapLabChunkRenderer.ChunkPixels(MapLabChunkRenderer.SnappedChunkSize(map), map.BakePixelsPerUnit);
            var indices = new HashSet<Vector2Int>(bases.Keys);
            foreach (var chunks in paintChunks) indices.UnionWith(chunks.Keys);

            var written = new List<string>();
            foreach (var index in indices)
            {
                byte[] pixels = bases.TryGetValue(index, out string basePath) ? Read(basePath, size) : new byte[size * size * 4];
                if (pixels == null) continue;
                foreach (var chunks in paintChunks)
                {
                    if (chunks.TryGetValue(index, out string paintPath)) Over(pixels, Read(paintPath, size), size, size);
                }
                string path = basePath ?? MapLabLayerFiles.ChunkPath(baseFolder, index, MapLabLayerFiles.PerMega(map));
                MapLabOutputPaths.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                MapLabLayerFiles.WritePng(path, pixels, size, size);
                written.Add(path);
            }
            MapLabLayerFiles.ConfigureChunks(written, map.BakePixelsPerUnit, size);
            return written.Count;
        }

        private static int FlattenSorted(MapLabMap map, string layer, List<string> paints)
        {
            MapLabSortedManifest manifest = MapLabSortedBaker.Manifest(map, layer);
            if (manifest == null) throw new InvalidOperationException($"'{layer}' has no piece list. Bake it again first.");
            string baseFolder = MapLabLayerFiles.BaseFolder(map, layer);
            var written = new List<string>();
            int largest = 32;
            foreach (var piece in manifest.pieces)
            {
                string path = $"{baseFolder}/{piece.file}";
                byte[] pixels = Read(path, piece.width, piece.height);
                if (pixels == null) continue;
                bool changed = false;
                foreach (var paint in paints)
                {
                    string cut = $"{MapLabLayerFiles.CutFolder(map, layer, paint)}/{piece.file}";
                    if (!File.Exists(cut)) continue;
                    Over(pixels, Read(cut, piece.width, piece.height), piece.width, piece.height);
                    changed = true;
                }
                if (!changed) continue;
                MapLabLayerFiles.WritePng(path, pixels, piece.width, piece.height);
                written.Add(path);
                largest = Mathf.Max(largest, piece.width, piece.height);
            }

            string compositeFolder = MapLabLayerFiles.CompositeFolder(map, layer);
            int size = manifest.Canvas.ChunkPixels;
            Dictionary<Vector2Int, string> composite = MapLabLayerFiles.Chunks(compositeFolder);
            foreach (var paint in paints)
            {
                foreach (var pair in MapLabLayerFiles.Chunks(MapLabLayerFiles.PaintFolder(map, layer, paint)))
                {
                    if (!composite.TryGetValue(pair.Key, out string target)) continue;
                    byte[] pixels = Read(target, size);
                    if (pixels == null) continue;
                    Over(pixels, Read(pair.Value, size), size, size);
                    MapLabLayerFiles.WritePng(target, pixels, size, size);
                }
            }
            MapLabLayerFiles.ConfigureChunks(written, manifest.pixelsPerUnit, largest);
            return written.Count;
        }

        private static byte[] Read(string path, int size)
        {
            return Read(path, size, size);
        }

        private static byte[] Read(string path, int width, int height)
        {
            byte[] pixels = MapLabLayerFiles.ReadPng(path, out int readWidth, out int readHeight);
            return readWidth == width && readHeight == height ? pixels : null;
        }

        private static void Over(byte[] target, byte[] source, int width, int height)
        {
            if (source != null) MapLabPaintCanvas.Over(target, width, height, source, new RectInt(0, 0, width, height), 255);
        }
        #endregion
    }
}
