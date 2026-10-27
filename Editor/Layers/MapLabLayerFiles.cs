using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabLayerFiles
    {
        #region [Variables/Fields]
        public const string LayersFolderName = "Layers";
        public const string BaseFolderName = "Base";
        public const string PaintFolderName = "Paint";
        public const string HistoryFolderName = "History~";
        public const string CutFolderName = "Cut";
        public const string DataFolderName = "Data~";
        public const string CompositeFolderName = "Composite";
        public const string OwnersFile = "Owners.png";
        public const string ManifestFile = "Pieces.json";
        public const int HistoryToKeep = 5;
        private static readonly Regex ChunkPattern = new Regex(@"^Chunk_(-?\d+)_(-?\d+)\.png$", RegexOptions.IgnoreCase);
        #endregion

        #region [Public Methods]
        public static string LayersFolder(MapLabMap map)
        {
            return $"{MapLabOutputPaths.MapFolder(map)}/{LayersFolderName}";
        }

        public static string LayerFolder(MapLabMap map, string layer)
        {
            return $"{LayersFolder(map)}/{layer}";
        }

        public static string BaseFolder(MapLabMap map, string layer)
        {
            return $"{LayerFolder(map, layer)}/{BaseFolderName}";
        }

        public static string PaintRoot(MapLabMap map, string layer)
        {
            return $"{LayerFolder(map, layer)}/{PaintFolderName}";
        }

        public static string PaintFolder(MapLabMap map, string layer, string paint)
        {
            return $"{PaintRoot(map, layer)}/{paint}";
        }

        public static string HistoryRoot(MapLabMap map, string layer)
        {
            return $"{LayerFolder(map, layer)}/{HistoryFolderName}";
        }

        public static string CutRoot(MapLabMap map, string layer)
        {
            return $"{LayerFolder(map, layer)}/{CutFolderName}";
        }

        public static string CutFolder(MapLabMap map, string layer, string paint)
        {
            return $"{CutRoot(map, layer)}/{paint}";
        }

        public static string DataFolder(MapLabMap map, string layer)
        {
            return $"{LayerFolder(map, layer)}/{DataFolderName}";
        }

        public static string CompositeFolder(MapLabMap map, string layer)
        {
            return $"{DataFolder(map, layer)}/{CompositeFolderName}";
        }

        public static string ImageFolder(MapLabMap map, string layer, bool sorted)
        {
            return sorted ? CompositeFolder(map, layer) : BaseFolder(map, layer);
        }

        public static string PieceFile(int index)
        {
            return $"Piece_{index:0000}.png";
        }

        public static string ChunkFile(Vector2Int index)
        {
            return $"Chunk_{index.x}_{index.y}.png";
        }

        public static int PerMega(MapLabMap map)
        {
            return map.ChunksPerMega;
        }

        public static string MegaFolder(string folder, Vector2Int mega)
        {
            return $"{folder}/{MegaChunkFormula.FolderName(mega)}";
        }

        public static string ChunkPath(string folder, Vector2Int index, int perMega)
        {
            return $"{MegaFolder(folder, MegaChunkFormula.MegaOfChunk(index, perMega))}/{ChunkFile(index)}";
        }

        public static HashSet<Vector2Int> Megas(string folder, int perMega)
        {
            return new HashSet<Vector2Int>(Chunks(folder).Keys.Select(index => MegaChunkFormula.MegaOfChunk(index, perMega)));
        }

        public static int Regroup(string folder, int perMega)
        {
            int moved = 0;
            foreach (var pair in Chunks(folder))
            {
                string target = ChunkPath(folder, pair.Key, perMega);
                if (string.Equals(pair.Value, target, StringComparison.OrdinalIgnoreCase) || File.Exists(target)) continue;
                EnsureDirectory(Path.GetDirectoryName(target).Replace('\\', '/'));
                string error = AssetImporter.GetAtPath(pair.Value) != null ? AssetDatabase.MoveAsset(pair.Value, target) : MoveFile(pair.Value, target);
                if (string.IsNullOrEmpty(error)) moved++;
            }
            if (moved > 0) PruneEmptyFolders(folder);
            return moved;
        }

        public static void PruneEmptyFolders(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var child in Directory.GetDirectories(folder))
            {
                string path = child.Replace('\\', '/');
                if (!MegaChunkFormula.TryParseFolder(Path.GetFileName(path), out _)) continue;
                if (Directory.GetFiles(path).Any(file => !file.EndsWith(".meta")) || Directory.GetDirectories(path).Length > 0) continue;
                if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
                else Directory.Delete(path, true);
            }
        }

        public static bool TryParseChunk(string fileName, out Vector2Int index)
        {
            index = default;
            Match match = ChunkPattern.Match(Path.GetFileName(fileName));
            if (!match.Success) return false;
            index = new Vector2Int(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            return true;
        }

        public static Dictionary<Vector2Int, string> Chunks(string folder)
        {
            var chunks = new Dictionary<Vector2Int, string>();
            if (!Directory.Exists(folder)) return chunks;
            foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                if (TryParseChunk(file, out Vector2Int index)) chunks[index] = file.Replace('\\', '/');
            }
            return chunks;
        }

        public static List<string> PaintNames(MapLabMap map, string layer)
        {
            string root = PaintRoot(map, layer);
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Dictionary<Vector2Int, Hash128> Hashes(string folder)
        {
            return Chunks(folder).ToDictionary(pair => pair.Key, pair => Hash128.Compute(File.ReadAllBytes(pair.Value)));
        }

        public static Dictionary<Vector2Int, Hash128> Hashes(Dictionary<Vector2Int, string> chunks, IEnumerable<Vector2Int> indices)
        {
            var hashes = new Dictionary<Vector2Int, Hash128>();
            foreach (var index in indices)
            {
                if (chunks.TryGetValue(index, out string path) && File.Exists(path)) hashes[index] = Hash128.Compute(File.ReadAllBytes(path));
            }
            return hashes;
        }

        public static string ArchiveChunks(MapLabMap map, string layer, IEnumerable<Vector2Int> indices)
        {
            Dictionary<Vector2Int, string> chunks = Chunks(BaseFolder(map, layer));
            List<string> paths = indices.Where(chunks.ContainsKey).Select(index => chunks[index]).ToList();
            if (paths.Count == 0) return null;
            string history = $"{HistoryRoot(map, layer)}/{DateTime.Now:yyyyMMdd_HHmmss}";
            for (int i = 2; Directory.Exists(history); i++) history = $"{HistoryRoot(map, layer)}/{DateTime.Now:yyyyMMdd_HHmmss}_{i}";
            Directory.CreateDirectory(history);
            foreach (var path in paths)
            {
                File.Copy(path, $"{history}/{Path.GetFileName(path)}", true);
                AssetDatabase.DeleteAsset(path);
            }
            PruneEmptyFolders(BaseFolder(map, layer));
            PruneHistory(map, layer);
            return history;
        }

        public static int HistoryCount(MapLabMap map, string layer)
        {
            string root = HistoryRoot(map, layer);
            return Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
        }

        public static string Write(string folder, Vector2Int index, Texture2D image, int perMega)
        {
            string path = ChunkPath(folder, index, perMega);
            EnsureDirectory(Path.GetDirectoryName(path).Replace('\\', '/'));
            File.WriteAllBytes(path, image.EncodeToPNG());
            return path;
        }

        public static void EnsureDirectory(string folder)
        {
            if (folder.Contains("~") || !folder.StartsWith("Assets", StringComparison.Ordinal)) Directory.CreateDirectory(folder);
            else MapLabOutputPaths.EnsureFolder(folder);
        }

        public static void WritePng(string path, byte[] pixels, int width, int height)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                var flipped = new byte[pixels.Length];
                int stride = width * 4;
                for (int y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
                image.LoadRawTextureData(flipped);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static byte[] ReadPng(string path, out int width, out int height)
        {
            Texture2D image = ReadPixels(path);
            try
            {
                width = image.width;
                height = image.height;
                return TopDown(image.GetPixels32(), width, height);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static byte[] TopDown(Color32[] colors, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int target = (height - 1 - y) * width * 4;
                for (int x = 0; x < width; x++)
                {
                    Color32 color = colors[y * width + x];
                    int index = target + x * 4;
                    pixels[index] = color.r;
                    pixels[index + 1] = color.g;
                    pixels[index + 2] = color.b;
                    pixels[index + 3] = color.a;
                }
            }
            return pixels;
        }

        public static void ClearFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            else if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        public static void EmptyFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                ClearFolder(folder);
                return;
            }
            var children = Directory.GetFileSystemEntries(folder).Where(path => !path.EndsWith(".meta")).Select(path => path.Replace('\\', '/')).ToList();
            if (children.Count > 0) AssetDatabase.DeleteAssets(children.ToArray(), new List<string>());
        }

        public static string ArchiveFolders(MapLabMap map, string layer, string label, bool keepFolders, params string[] folders)
        {
            string history = $"{HistoryRoot(map, layer)}/{DateTime.Now:yyyyMMdd_HHmmss}{label}";
            for (int i = 2; Directory.Exists(history); i++) history = $"{HistoryRoot(map, layer)}/{DateTime.Now:yyyyMMdd_HHmmss}{label}_{i}";
            bool copied = false;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                string target = $"{history}/{Path.GetFileName(folder)}";
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta")) continue;
                    string relative = file.Substring(folder.Length).TrimStart('/', '\\');
                    string destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(file, destination, true);
                    copied = true;
                }
                if (keepFolders) EmptyFolder(folder);
                else ClearFolder(folder);
            }
            if (copied) PruneHistory(map, layer);
            return copied ? history : null;
        }

        public static void DeleteChunk(string folder, Vector2Int index)
        {
            if (!Chunks(folder).TryGetValue(index, out string path)) return;
            if (AssetImporter.GetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else if (File.Exists(path)) File.Delete(path);
        }

        public static void ConfigureChunks(IEnumerable<string> paths, int pixelsPerUnit, int pixels)
        {
            AssetDatabase.Refresh();
            int maxSize = Mathf.Max(32, Mathf.NextPowerOfTwo(pixels));
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                {
                    if (AssetImporter.GetAtPath(path) is TextureImporter importer) Configure(importer, pixelsPerUnit, maxSize);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        public static void EnsureSprites(IEnumerable<string> paths, float chunkSize)
        {
            List<string> list = paths.ToList();
            if (list.Any(path => AssetImporter.GetAtPath(path) == null)) AssetDatabase.Refresh();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in list)
                {
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    Vector2Int size = PngSize(path);
                    float pixelsPerUnit = size.x / chunkSize;
                    bool ready = importer.textureType == TextureImporterType.Sprite && Mathf.Abs(importer.spritePixelsPerUnit - pixelsPerUnit) < 0.01f && !importer.mipmapEnabled;
                    if (!ready) Configure(importer, pixelsPerUnit, Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(size.x, size.y))));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        public static Vector2Int PngSize(string path)
        {
            var header = new byte[24];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(header, 0, header.Length) < header.Length) return Vector2Int.zero;
            }
            return new Vector2Int(header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19], header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23]);
        }

        public static Texture2D ReadPixels(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }
        #endregion

        #region [Private Methods]
        private static string MoveFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Move(from, to);
            if (File.Exists(from + ".meta")) File.Move(from + ".meta", to + ".meta");
            return null;
        }

        private static void Configure(TextureImporter importer, float pixelsPerUnit, int maxSize)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = maxSize;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomLeft;
            settings.spritePivot = Vector2.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void PruneHistory(MapLabMap map, string layer)
        {
            string root = HistoryRoot(map, layer);
            if (!Directory.Exists(root)) return;
            foreach (var folder in Directory.GetDirectories(root).OrderByDescending(Path.GetFileName).Skip(HistoryToKeep)) Directory.Delete(folder, true);
        }
        #endregion
    }
}
