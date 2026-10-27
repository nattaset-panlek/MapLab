using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class BakeLayerTests
    {
        #region [Variables/Fields]
        private static readonly string[] Layers = { "Ground", "Mid", "Mid_top" };
        private readonly List<GameObject> _created = new List<GameObject>();
        #endregion

        #region [Unity Methods]
        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void PaintNames_FollowTheLayerSyntax()
        {
            Assert.IsTrue(MapLabLayerNames.TryParsePaint("mid_paint", Layers, out string layer, out string paint));
            Assert.AreEqual(("Mid", "Mid_paint"), (layer, paint), "case does not matter and the layer keeps its own spelling");
            Assert.IsTrue(MapLabLayerNames.TryParsePaint("Mid_paint_cracks 2", Layers, out layer, out paint));
            Assert.AreEqual(("Mid", "Mid_paint_cracks 2"), (layer, paint));
            Assert.IsTrue(MapLabLayerNames.TryParsePaint("mid_top_paint", Layers, out layer, out _));
            Assert.AreEqual("Mid_top", layer, "the longest layer name wins");
            Assert.IsFalse(MapLabLayerNames.TryParsePaint("Mid_painting", Layers, out _, out _), "the label must follow an underscore");
            Assert.IsFalse(MapLabLayerNames.TryParsePaint("sketch idea", Layers, out _, out _));
            Assert.IsFalse(MapLabLayerNames.TryParsePaint("cave_paint", Layers, out _, out _));
            Assert.IsTrue(MapLabLayerNames.LooksLikePaint("cave_paint", out string unknown));
            Assert.AreEqual("cave", unknown, "an unknown layer is reported by name");
            Assert.IsTrue(MapLabLayerNames.IsBakeLayer("mid", Layers), "an exact layer name is the locked reference");
        }

        [Test]
        public void LayerNames_KeepPaintSyntaxAndFolderNamesSafe()
        {
            Assert.IsTrue(MapLabLayerNames.IsValidLayerName("Cliffs", out _));
            Assert.IsFalse(MapLabLayerNames.IsValidLayerName("Cliffs_paint", out _), "_paint is reserved");
            Assert.IsFalse(MapLabLayerNames.IsValidLayerName(" ", out _));
            Assert.IsFalse(MapLabLayerNames.IsValidLayerName("A/B", out _));
            Assert.AreEqual("Cliff_paint_moss", MapLabLayerNames.RenamePaint("Mid_paint_moss", "Mid", "Cliff"));
        }

        [Test]
        public void ChunkFiles_KeepNegativeIndices()
        {
            Assert.AreEqual("Chunk_-2_3.png", MapLabLayerFiles.ChunkFile(new Vector2Int(-2, 3)));
            Assert.IsTrue(MapLabLayerFiles.TryParseChunk("Assets/Map/Base/Chunk_-2_3.png", out Vector2Int index));
            Assert.AreEqual(new Vector2Int(-2, 3), index);
            Assert.IsFalse(MapLabLayerFiles.TryParseChunk("Chunk_a_3.png", out _));
        }

        [Test]
        public void Stack_KeepsTheSavedOrderAndPutsNewPaintAboveItsLayer()
        {
            var paints = new[]
            {
                new MapLabLayerStack.Entry("Ground_paint", "Ground", true),
                new MapLabLayerStack.Entry("Shadows_paint_soft", "Shadows", true),
                new MapLabLayerStack.Entry("Ground_paint_moss", "Ground", true)
            };
            var layers = new[] { "Ground", "Shadows" };

            List<string> saved = Names(MapLabLayerStack.Resolve(layers, paints, new[] { "Shadows", "ground_paint", "Ground", "Removed" }));
            CollectionAssert.AreEqual(new[] { "Shadows", "Shadows_paint_soft", "Ground_paint", "Ground", "Ground_paint_moss" }, saved,
                "a paint can sit below its layer; unknown names drop out; new paint goes right above its layer");

            List<string> fresh = Names(MapLabLayerStack.Resolve(layers, paints, new string[0]));
            CollectionAssert.AreEqual(new[] { "Ground", "Ground_paint", "Ground_paint_moss", "Shadows", "Shadows_paint_soft" }, fresh);
        }

        [Test]
        public void Stack_MergeTakesTheOrderOfTheImportedFile()
        {
            var current = new[] { "Ground", "Ground_paint", "Mid", "Mid_paint", "Top" };
            var imported = new[] { "ground", "mid_paint", "Mid", "Top", "sketch" };
            CollectionAssert.AreEqual(new[] { "Ground", "Ground_paint", "Mid_paint", "Mid", "Top" }, MapLabLayerStack.Merge(current, imported),
                "entries missing from the file keep their place after their old neighbour");
        }

        [Test]
        public void SortingKeys_FollowTheStackAndStayBelowTheActors()
        {
            var actor = new Vector2Int(0, 6);
            Assert.AreEqual(new[] { -100, -99, 1 }, Orders(new Vector2Int?[] { new Vector2Int(0, -100), null, new Vector2Int(0, 1) }, actor),
                "a baked layer keeps the order of its lowest source, paint draws right above the layer below it");
            Assert.AreEqual(new[] { -1, 0, 1, 2, 3, 4, 5 }, Orders(new Vector2Int?[] { new Vector2Int(0, 1), null, null, null, null, null, null }, actor),
                "a tall stack is pushed down so nothing reaches the actors");
            Assert.AreEqual(new[] { -51, -50, -49 }, Orders(new Vector2Int?[] { null, new Vector2Int(0, -50), null }, actor));
            Assert.AreEqual(new[] { 4, 5 }, Orders(new Vector2Int?[] { null, null }, actor));
            List<Vector2Int> lower = MapLabLayerStack.AssignKeys(new Vector2Int?[] { new Vector2Int(-1, 10), null }, actor);
            CollectionAssert.AreEqual(new[] { new Vector2Int(-1, 10), new Vector2Int(-1, 11) }, lower, "a lower sorting layer is already below the actors");
        }

        [Test]
        public void Claims_PutEachGroundObjectInOneLayer()
        {
            MapLabMap map = CreateMap();
            SpriteRenderer grass = Sprite(map.transform, "Grass", -100);
            var boulder = new GameObject("Boulder");
            boulder.transform.SetParent(map.transform, false);
            boulder.AddComponent<SpriteRenderer>().sortingOrder = 6;
            SpriteRenderer propShadow = Sprite(boulder.transform, "Shadow", 1);
            var roads = new GameObject("Roads");
            roads.transform.SetParent(map.transform, false);
            MapLabSet roadSet = roads.AddComponent<MapLabSet>();
            var road = new GameObject("Road");
            road.transform.SetParent(roads.transform, false);
            var path = road.AddComponent<PathShape>();
            path.SetPoints(new[] { new Vector2(-3f, 0f), new Vector2(3f, 0f) });
            ExtrudedShape plaza = FlatShape(map.transform, "Plaza");
            ExtrudedShape plateau = Shape(map.transform, "Plateau");

            Renderer strip = road.transform.Find(PathShape.StripName).GetComponent<Renderer>();
            Renderer plazaPiece = plaza.transform.Find(ExtrudedShape.VisualName).GetComponentInChildren<Renderer>();
            Renderer plateauShadow = plateau.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>();
            Assert.AreEqual(MapLabBakeKinds.Sprites, MapLabLayerClaims.KindOf(grass, map));
            Assert.AreEqual(MapLabBakeKinds.PropShadows, MapLabLayerClaims.KindOf(propShadow, map), "a sprite under an object drawn with the actors is its shadow");
            Assert.AreEqual(MapLabBakeKinds.Paths, MapLabLayerClaims.KindOf(strip, map));
            Assert.AreEqual(MapLabBakeKinds.FlatShapes, MapLabLayerClaims.KindOf(plazaPiece, map));
            Assert.AreEqual(MapLabBakeKinds.ShapeShadows, MapLabLayerClaims.KindOf(plateauShadow, map));

            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites | MapLabBakeKinds.Paths | MapLabBakeKinds.FlatShapes));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Shadows", MapLabBakeKinds.ShapeShadows | MapLabBakeKinds.PropShadows));
            var roadLayer = new MapLabBakeLayerSettings("Roads", MapLabBakeKinds.Paths);
            roadLayer.Sets.Add(roadSet);
            map.BakeLayers.Add(roadLayer);

            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out List<Renderer> unclaimed);
            CollectionAssert.AreEquivalent(new[] { grass, plazaPiece }, claims["Ground"]);
            CollectionAssert.AreEquivalent(new[] { propShadow, plateauShadow }, claims["shadows"]);
            CollectionAssert.AreEquivalent(new[] { strip }, claims["Roads"], "a layer with ticked sets takes its objects first, even from the top of the stack");
            Assert.IsEmpty(unclaimed);
            Assert.AreEqual(1, MapLabLayerClaims.CountSorted(map), "the tall plateau sorts with the actors and stays live");
        }

        [Test]
        public void PackBits_RoundTripsEveryRunShape()
        {
            var rows = new List<byte[]>
            {
                new byte[] { 7 },
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 5, 5, 5, 5 },
                Enumerable.Repeat((byte)9, 300).ToArray(),
                Enumerable.Range(0, 300).Select(i => (byte)(i % 2)).ToArray(),
                new byte[] { 1, 1, 2, 2, 2, 3, 4, 4, 4, 4, 5, 6, 6 }
            };
            foreach (var row in rows)
            {
                byte[] packed = MapLabPsd.PackBits(row, row.Length);
                var unpacked = new byte[row.Length];
                MapLabPsd.UnpackBits(packed, 0, packed.Length, unpacked, 0, row.Length);
                CollectionAssert.AreEqual(row, unpacked);
                Assert.LessOrEqual(packed.Length, row.Length + (row.Length + 127) / 128);
            }
            Assert.AreEqual(6, MapLabPsd.PackBits(rows[3], 300).Length, "300 equal bytes are three runs");
        }

        [Test]
        public void Psd_RoundTripKeepsLayersGroupsNamesAndPixels()
        {
            var document = new MapLabPsd.Document { Width = 5, Height = 4 };
            document.Layers.Add(new MapLabPsd.Layer { Name = "Ground", Locked = true, Rect = new RectInt(0, 0, 5, 4), Pixels = Pattern(5, 4, 3) });
            document.Layers.Add(new MapLabPsd.Layer
            {
                Name = "ground_paint_หญ้า",
                Opacity = 200,
                Children = new List<MapLabPsd.Layer>
                {
                    new MapLabPsd.Layer { Name = "stroke", Rect = new RectInt(1, 1, 2, 2), Pixels = Pattern(2, 2, 7) },
                    new MapLabPsd.Layer { Name = "hidden", Visible = false, Rect = new RectInt(0, 0, 1, 1), Pixels = Pattern(1, 1, 11) }
                }
            });
            document.Layers.Add(new MapLabPsd.Layer { Name = "Grid", Visible = false, Locked = true });
            string path = FileUtil.GetUniqueTempPathInProject() + ".psd";
            try
            {
                MapLabPsd.Write(path, document, Pattern(5, 4, 1));
                MapLabPsd.Document read = MapLabPsd.Read(path);

                Assert.AreEqual((5, 4), (read.Width, read.Height));
                CollectionAssert.AreEqual(new[] { "Ground", "ground_paint_หญ้า", "Grid" }, read.Layers.Select(layer => layer.Name), "bottom to top, with Thai names");
                Assert.IsTrue(read.Layers[0].Locked);
                CollectionAssert.AreEqual(document.Layers[0].Pixels, read.Layers[0].Pixels);
                MapLabPsd.Layer group = read.Layers[1];
                Assert.IsTrue(group.IsGroup);
                Assert.AreEqual(200, group.Opacity);
                CollectionAssert.AreEqual(new[] { "stroke", "hidden" }, group.Children.Select(layer => layer.Name));
                Assert.AreEqual(new RectInt(1, 1, 2, 2), group.Children[0].Rect);
                CollectionAssert.AreEqual(Pattern(2, 2, 7), group.Children[0].Pixels);
                Assert.IsFalse(group.Children[1].Visible);
                Assert.IsFalse(read.Layers[2].Visible);
                Assert.AreEqual(0, read.Layers[2].Rect.width, "an empty layer has no pixels");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void PaintCanvas_CutsAndPastesOnTheWorldGrid()
        {
            var canvas = new MapLabPaintCanvas(new Vector2Int(-1, -1), new Vector2Int(2, 2), 1f, 4);
            Assert.AreEqual((8, 8, 4), (canvas.Width, canvas.Height, canvas.ChunkPixels));
            Assert.IsTrue(MapLabPaintCanvas.TryParse($"{canvas.Label}.png", out MapLabPaintCanvas parsed));
            Assert.AreEqual((canvas.Origin, canvas.Count, canvas.ChunkSize, canvas.PixelsPerUnit), (parsed.Origin, parsed.Count, parsed.ChunkSize, parsed.PixelsPerUnit));

            var image = new Color32[16];
            image[0] = new Color32(255, 0, 0, 255);
            image[15] = new Color32(0, 0, 255, 255);
            var pixels = new byte[canvas.Width * canvas.Height * 4];
            Assert.IsTrue(canvas.Paste(pixels, Vector2Int.zero, image, 4));
            Assert.IsFalse(canvas.Paste(pixels, Vector2Int.zero, new Color32[64], 8), "a chunk made at another size is refused");
            Assert.AreEqual(255, pixels[(3 * 8 + 4) * 4], "chunk 0,0 is the top right quarter; its bottom left pixel is row 3, column 4");

            Color32[] cut = canvas.Cut(pixels, Vector2Int.zero, 1f, out bool empty);
            Assert.IsFalse(empty);
            CollectionAssert.AreEqual(image, cut);
            canvas.Cut(pixels, new Vector2Int(-1, -1), 1f, out bool otherEmpty);
            Assert.IsTrue(otherEmpty);

            Color32[] larger = canvas.Cut(pixels, Vector2Int.zero, 2f, out _);
            Assert.AreEqual(64, larger.Length, "a bigger chunk size cuts the same pixels at the same place");
            Assert.AreEqual(image[0], larger[0]);
            Assert.AreEqual(image[15], larger[3 * 8 + 3]);
            Assert.AreEqual(0, larger[7 * 8 + 7].a, "outside the canvas is transparent");
        }

        [Test]
        public void PaintCanvas_BlendsNormalOver()
        {
            var white = new byte[] { 255, 255, 255, 255 };
            MapLabPaintCanvas.Over(white, 1, 1, new byte[] { 0, 0, 0, 128 }, new RectInt(0, 0, 1, 1), 255);
            Assert.AreEqual(255, white[3]);
            Assert.That(white[0], Is.InRange(126, 128));
            var clear = new byte[4];
            MapLabPaintCanvas.Over(clear, 1, 1, new byte[] { 10, 20, 30, 200 }, new RectInt(0, 0, 1, 1), 128);
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 100 }, clear, "layer opacity scales the alpha");
        }
        #endregion

        #region [Private Methods]
        private MapLabMap CreateMap()
        {
            var root = new GameObject("Map");
            _created.Add(root);
            return root.AddComponent<MapLabMap>();
        }

        private static SpriteRenderer Sprite(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        private static ExtrudedShape Shape(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<ExtrudedShape>();
        }

        private static ExtrudedShape FlatShape(Transform parent, string name)
        {
            ExtrudedShape shape = Shape(parent, name);
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_height").floatValue = 0f;
            serialized.FindProperty("_ySort").boolValue = false;
            serialized.FindProperty("_sortingOrder").intValue = -90;
            serialized.FindProperty("_castShadow").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
            return shape;
        }

        private static List<string> Names(List<MapLabLayerStack.Entry> entries)
        {
            return entries.Select(entry => entry.Name).ToList();
        }

        private static int[] Orders(Vector2Int?[] bases, Vector2Int actor)
        {
            return MapLabLayerStack.AssignKeys(bases, actor).Select(key => key.y).ToArray();
        }

        private static byte[] Pattern(int width, int height, int seed)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i * seed + seed) % 251);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = (byte)(i % 3 == 0 ? 255 : 90 + i % 7);
            return pixels;
        }
        #endregion
    }
}
