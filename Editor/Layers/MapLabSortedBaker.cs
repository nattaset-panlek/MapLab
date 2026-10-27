using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabSortedBaker
    {
        #region [Variables/Fields]
        public const string BaseChild = "Base";

        private const string PiecePrefix = "Piece ";
        private static readonly Regex PieceName = new Regex(@"^Piece (\d+)$");
        #endregion

        #region [Public Methods]
        public static string Bake(MapLabMap map, MapLabBakeLayerSettings settings, List<Renderer> claimed)
        {
            Scene scene = map.gameObject.scene;
            string layer = settings.Name;
            int pixelsPerUnit = map.BakePixelsPerUnit;
            float chunkSize = MapLabChunkRenderer.SnappedChunkSize(map);
            var watch = Stopwatch.StartNew();
            MapLabPaintCanvas canvas = CanvasFor(claimed, chunkSize, pixelsPerUnit);
            string compositeFolder = MapLabLayerFiles.CompositeFolder(map, layer);
            string baseFolder = MapLabLayerFiles.BaseFolder(map, layer);
            Dictionary<Vector2Int, Hash128> before = MapLabLayerFiles.Hashes(compositeFolder);
            MapLabLayerFiles.ArchiveFolders(map, layer, "", true, baseFolder, compositeFolder);

            var composite = new byte[canvas.Width * canvas.Height * 4];
            List<Vector2Int> covered = MapLabChunkCoverage.Of(claimed, chunkSize).Where(canvas.Contains).ToList();
            MapLabChunkRenderer.Render(scene, claimed, covered, chunkSize, pixelsPerUnit, (index, image) =>
            {
                canvas.Paste(composite, index, image.GetPixels32(), image.width);
                MapLabLayerFiles.Write(compositeFolder, index, image, MapLabLayerFiles.PerMega(map));
            });

            List<int> order = MapLabPieceOwners.DrawOrder(claimed.Select(Key).ToList());
            int[] owners = MapLabPieceOwners.Owners(canvas, claimed.Select(Triangles).ToList(), order, composite);
            RectInt[] rects = MapLabPieceOwners.PieceRects(owners, canvas.Width, canvas.Height, claimed.Count);
            MapLabSortedManifest manifest = MapLabSortedManifest.For(canvas);
            var remap = new int[claimed.Count + 1];
            var written = new List<string>();
            int largest = 32;
            MapLabOutputPaths.EnsureFolder(baseFolder);
            foreach (int index in order)
            {
                if (rects[index].width == 0) continue;
                Renderer source = claimed[index];
                RectInt rect = rects[index];
                byte[] pixels = MapLabPieceOwners.Cut(owners, composite, canvas.Width, rect, index + 1, out bool empty);
                if (empty) continue;
                string file = MapLabLayerFiles.PieceFile(manifest.pieces.Count);
                MapLabLayerFiles.WritePng($"{baseFolder}/{file}", pixels, rect.width, rect.height);
                written.Add($"{baseFolder}/{file}");
                largest = Mathf.Max(largest, rect.width, rect.height);
                manifest.pieces.Add(new MapLabSortedManifest.Piece
                {
                    file = file,
                    x = rect.x,
                    y = rect.y,
                    width = rect.width,
                    height = rect.height,
                    sortPoint = SortPoint(source),
                    sortingLayer = SortingLayer.IDToName(MapLabLayerClaims.SortingLayerOf(source)),
                    sortingOrder = MapLabLayerClaims.SortingOrderOf(source)
                });
                remap[index + 1] = manifest.pieces.Count;
            }
            for (int i = 0; i < owners.Length; i++) owners[i] = remap[owners[i]];

            string data = MapLabLayerFiles.DataFolder(map, layer);
            Directory.CreateDirectory(data);
            MapLabLayerFiles.WritePng($"{data}/{MapLabLayerFiles.OwnersFile}", MapLabPieceOwners.EncodeOwners(owners), canvas.Width, canvas.Height);
            manifest.Save($"{data}/{MapLabLayerFiles.ManifestFile}");
            MapLabLayerFiles.ConfigureChunks(written, pixelsPerUnit, largest);
            RecutPaint(map, layer);
            string atlas = MapLabLayerAtlas.Ensure(map, layer);
            int warnings = MapLabLayerBaker.UpdateChangedUnderPaint(map, settings, before, MapLabLayerFiles.Hashes(compositeFolder));

            MapLabBakedLayer baked = BuildLayer(map, layer, manifest);
            MapLabStaticBaker.HideSources(baked, claimed);
            MapLabBakeStatus.RecordStaticStamp(map, baked);
            MapLabLayerBaker.SyncScene(map);
            SortingGroup.UpdateAllSortingGroups();
            watch.Stop();
            EditorSceneManager.MarkSceneDirty(scene);

            int shapes = baked.BakedVisuals.Count;
            string warning = warnings > 0 ? $"; {warnings} painted chunks changed underneath, check them in the Layers page" : "";
            string packed = atlas != null ? $", packed by {atlas}" : "";
            return $"[MapLab] Baked sorted layer '{layer}' of '{map.name}': {shapes} shapes ({claimed.Count} y-sort pieces) into {manifest.pieces.Count} images at {pixelsPerUnit} px/unit in {baseFolder}{packed} ({watch.Elapsed.TotalSeconds:0.0} s){warning}";
        }

        public static void RecutPaint(MapLabMap map, string layer)
        {
            string data = MapLabLayerFiles.DataFolder(map, layer);
            MapLabSortedManifest manifest = MapLabSortedManifest.Load($"{data}/{MapLabLayerFiles.ManifestFile}");
            string ownersPath = $"{data}/{MapLabLayerFiles.OwnersFile}";
            MapLabLayerFiles.EmptyFolder(MapLabLayerFiles.CutRoot(map, layer));
            if (manifest == null || !File.Exists(ownersPath)) return;

            MapLabPaintCanvas canvas = manifest.Canvas;
            int[] owners = MapLabPieceOwners.DecodeOwners(MapLabLayerFiles.ReadPng(ownersPath, out int width, out int height));
            if (width != canvas.Width || height != canvas.Height) return;
            var written = new List<string>();
            int largest = 32;
            foreach (var paint in MapLabLayerFiles.PaintNames(map, layer))
            {
                byte[] pixels = PaintCanvas(canvas, MapLabLayerFiles.PaintFolder(map, layer, paint));
                if (pixels == null) continue;
                string folder = MapLabLayerFiles.CutFolder(map, layer, paint);
                for (int i = 0; i < manifest.pieces.Count; i++)
                {
                    MapLabSortedManifest.Piece piece = manifest.pieces[i];
                    byte[] cut = MapLabPieceOwners.Cut(owners, pixels, canvas.Width, manifest.PixelRect(piece), i + 1, out bool empty);
                    if (empty) continue;
                    MapLabOutputPaths.EnsureFolder(folder);
                    MapLabLayerFiles.WritePng($"{folder}/{piece.file}", cut, piece.width, piece.height);
                    written.Add($"{folder}/{piece.file}");
                    largest = Mathf.Max(largest, piece.width, piece.height);
                }
            }
            MapLabLayerFiles.ConfigureChunks(written, canvas.PixelsPerUnit, largest);
        }

        public static void SyncPaint(MapLabMap map, List<MapLabLayerStack.Entry> stack)
        {
            foreach (var baked in map.GetComponentsInChildren<MapLabBakedLayer>(true).Where(layer => layer.IsSorted))
            {
                string layer = baked.LayerName;
                List<string> paints = stack.Where(entry => entry.IsPaint && MapLabLayerStack.Same(entry.Layer, layer)).Select(entry => entry.Name).ToList();
                foreach (var piece in baked.SortedLayer.Pieces)
                {
                    if (piece.Root == null) continue;
                    foreach (var old in piece.Root.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer.name.StartsWith(MapLabLayerBaker.PaintPrefix)).ToList())
                    {
                        Object.DestroyImmediate(old.gameObject);
                    }
                    Match match = PieceName.Match(piece.Root.name);
                    Transform image = piece.Root.transform.Find(BaseChild);
                    if (!match.Success || image == null) continue;
                    string file = MapLabLayerFiles.PieceFile(int.Parse(match.Groups[1].Value));
                    var baseRenderer = image.GetComponent<SpriteRenderer>();
                    for (int i = 0; i < paints.Count; i++)
                    {
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{MapLabLayerFiles.CutFolder(map, layer, paints[i])}/{file}");
                        if (sprite == null) continue;
                        var go = new GameObject(MapLabLayerBaker.PaintPrefix + paints[i]) { layer = piece.Root.layer };
                        go.transform.SetParent(piece.Root.transform, false);
                        go.transform.position = image.position;
                        var renderer = go.AddComponent<SpriteRenderer>();
                        renderer.sprite = sprite;
                        renderer.sharedMaterial = baseRenderer.sharedMaterial;
                        renderer.sortingLayerID = baseRenderer.sortingLayerID;
                        renderer.sortingOrder = i + 1;
                    }
                }
            }
        }

        public static MapLabSortedManifest Manifest(MapLabMap map, string layer)
        {
            return MapLabSortedManifest.Load($"{MapLabLayerFiles.DataFolder(map, layer)}/{MapLabLayerFiles.ManifestFile}");
        }

        public static List<MapLabPieceOwners.Triangle> Triangles(Renderer renderer)
        {
            var triangles = new List<MapLabPieceOwners.Triangle>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return triangles;
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            Transform transform = renderer.transform;
            var world = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) world[i] = transform.TransformPoint(vertices[i]);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    bool opaque = colors.Length != vertices.Length || Mathf.Min(colors[a].a, Mathf.Min(colors[b].a, colors[c].a)) >= MapLabPieceOwners.OpaqueAlpha / 255f;
                    triangles.Add(new MapLabPieceOwners.Triangle(world[a], world[b], world[c], opaque));
                }
            }
            return triangles;
        }

        public static MapLabPieceOwners.SortKey Key(Renderer renderer)
        {
            return new MapLabPieceOwners.SortKey(SortingLayer.GetLayerValueFromID(MapLabLayerClaims.SortingLayerOf(renderer)), MapLabLayerClaims.SortingOrderOf(renderer), SortPoint(renderer).y);
        }
        #endregion

        #region [Private Methods]
        private static MapLabPaintCanvas CanvasFor(List<Renderer> renderers, float chunkSize, int pixelsPerUnit)
        {
            HashSet<Vector2Int> covered = MapLabChunkCoverage.Of(renderers, chunkSize);
            var min = new Vector2Int(covered.Min(index => index.x), covered.Min(index => index.y));
            var max = new Vector2Int(covered.Max(index => index.x), covered.Max(index => index.y));
            return new MapLabPaintCanvas(min, max - min + Vector2Int.one, chunkSize, pixelsPerUnit);
        }

        private static Vector3 SortPoint(Renderer renderer)
        {
            SortingGroup group = renderer.GetComponentInParent<SortingGroup>();
            return group != null ? group.transform.position : renderer.bounds.center;
        }

        private static byte[] PaintCanvas(MapLabPaintCanvas canvas, string folder)
        {
            Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(folder);
            if (chunks.Count == 0) return null;
            var pixels = new byte[canvas.Width * canvas.Height * 4];
            bool any = false;
            foreach (var pair in chunks)
            {
                Texture2D image = MapLabLayerFiles.ReadPixels(pair.Value);
                any |= canvas.Paste(pixels, pair.Key, image.GetPixels32(), image.width);
                Object.DestroyImmediate(image);
            }
            return any ? pixels : null;
        }

        private static MapLabBakedLayer BuildLayer(MapLabMap map, string layer, MapLabSortedManifest manifest)
        {
            Transform root = MapLabLayerBaker.Root(map, true);
            var go = new GameObject(MapLabLayerBaker.LayerPrefix + layer) { layer = root.gameObject.layer };
            go.transform.SetParent(root, false);
            Material material = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.default2DMaterial : null;
            string folder = MapLabLayerFiles.BaseFolder(map, layer);
            var pieces = new List<MapLabSortedLayer.Piece>(manifest.pieces.Count);
            for (int i = 0; i < manifest.pieces.Count; i++)
            {
                MapLabSortedManifest.Piece entry = manifest.pieces[i];
                var piece = new GameObject($"{PiecePrefix}{i:0000}") { layer = go.layer };
                piece.transform.SetParent(go.transform, false);
                piece.transform.position = entry.sortPoint;
                var group = piece.AddComponent<SortingGroup>();
                group.sortingLayerName = entry.sortingLayer;
                group.sortingOrder = entry.sortingOrder;

                Rect world = manifest.WorldRect(entry);
                var image = new GameObject(BaseChild) { layer = go.layer };
                image.transform.SetParent(piece.transform, false);
                image.transform.position = new Vector3(world.x, world.y, entry.sortPoint.z);
                var renderer = image.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{folder}/{entry.file}");
                if (material != null) renderer.sharedMaterial = material;
                renderer.sortingLayerName = entry.sortingLayer;
                renderer.sortingOrder = 0;
                pieces.Add(new MapLabSortedLayer.Piece(piece, world));
            }
            go.AddComponent<MapLabSortedLayer>().Setup(layer, manifest.pixelsPerUnit, pieces);
            return go.AddComponent<MapLabBakedLayer>();
        }
        #endregion
    }
}
