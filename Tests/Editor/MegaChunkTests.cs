using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class MegaChunkTests
    {
        #region [Variables/Fields]
        private const string TempFolder = "Temp/MapLabMegaTests~";
        private readonly List<Object> _created = new List<Object>();
        #endregion

        #region [Unity Methods]
        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
            _created.Clear();
            if (Directory.Exists(TempFolder)) Directory.Delete(TempFolder, true);
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void Formula_GroupsFourByFourChunksOf1024PixelsInto4096()
        {
            Assert.AreEqual(4, MegaChunkFormula.ChunksPerMega(1024, 4096), "16 units at 64 px/unit");
            Assert.AreEqual(2, MegaChunkFormula.ChunksPerMega(2048, 4096));
            Assert.AreEqual(1, MegaChunkFormula.ChunksPerMega(5000, 4096), "a chunk larger than a mega chunk is its own mega chunk");
            Assert.AreEqual(new Vector2Int(-1, -2), MegaChunkFormula.MegaOfChunk(new Vector2Int(-1, -5), 4), "negative chunks floor, not truncate");
            Assert.AreEqual(new Vector2Int(0, 1), MegaChunkFormula.MegaOfChunk(new Vector2Int(3, 4), 4));
            Assert.AreEqual(new RectInt(-4, 8, 4, 4), MegaChunkFormula.ChunksOf(new Vector2Int(-1, 2), 4));
            Assert.AreEqual(new Vector2Int(-1, 0), MegaChunkFormula.MegaAt(new Vector2(-0.5f, 63.9f), 64f));
            Assert.AreEqual(new Vector2Int(63, 0), MegaChunkFormula.CellAt(new Vector2(-0.5f, 0.2f), new Vector2Int(-1, 0), 64f, 64));
        }

        [Test]
        public void Formula_TilesSplitIntoMegaAndCell()
        {
            Assert.AreEqual(new Vector2Int(-1, 0), MegaChunkFormula.MegaOfTile(new Vector2Int(-1, 63), 64));
            Assert.AreEqual(new Vector2Int(63, 63), MegaChunkFormula.CellOfTile(new Vector2Int(-1, 63), 64));
            Assert.AreEqual(new Vector2Int(1, -1), MegaChunkFormula.MegaOfTile(new Vector2Int(64, -64), 64));
            Assert.AreEqual(new Vector2Int(0, 0), MegaChunkFormula.CellOfTile(new Vector2Int(64, -64), 64));
        }

        [Test]
        public void Formula_FolderNamesRoundTrip()
        {
            Assert.AreEqual("M_-3_12", MegaChunkFormula.FolderName(new Vector2Int(-3, 12)));
            Assert.IsTrue(MegaChunkFormula.TryParseFolder("M_-3_12", out Vector2Int mega));
            Assert.AreEqual(new Vector2Int(-3, 12), mega);
            Assert.IsFalse(MegaChunkFormula.TryParseFolder("Base", out _));
            Assert.IsFalse(MegaChunkFormula.TryParseFolder("M_a_1", out _));
        }

        [Test]
        public void Rectangles_CoverEveryTileOnceWithFewQuads()
        {
            byte[,] grid =
            {
                { 1, 1, 2, 2 },
                { 1, 1, 2, 0 },
                { 1, 1, 1, 1 },
                { 3, 1, 1, 1 }
            };
            byte TileAt(int x, int y) => grid[y, x];
            var covered = new int[4, 4];
            foreach (byte tile in new byte[] { 1, 2, 3 })
            {
                foreach (var rect in FloorFormula.Rectangles(TileAt, 4, tile))
                {
                    foreach (var cell in rect.allPositionsWithin)
                    {
                        Assert.AreEqual(tile, grid[cell.y, cell.x], $"a quad of tile {tile} covers {cell}");
                        covered[cell.y, cell.x]++;
                    }
                }
            }
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++) Assert.AreEqual(grid[y, x] == 0 ? 0 : 1, covered[y, x], $"tile {x},{y}");
            }
            Assert.AreEqual(1, FloorFormula.Rectangles((x, y) => 5, 64, 5).Count, "a full mega chunk of one tile is one quad");
            CollectionAssert.AreEquivalent(new byte[] { 1, 2, 3 }, FloorFormula.TilesUsed(TileAt, 4).ToArray());
        }

        [Test]
        public void Brush_IsCentredOnTheTile()
        {
            List<Vector2Int> three = FloorFormula.Brush(new Vector2Int(10, 10), 3);
            Assert.AreEqual(9, three.Count);
            Assert.IsTrue(three.Contains(new Vector2Int(9, 9)) && three.Contains(new Vector2Int(11, 11)));
            List<Vector2Int> two = FloorFormula.Brush(new Vector2Int(0, 0), 2);
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }, two);
        }

        [Test]
        public void MegaChunk_StoresPaintOnlyWhenPainted()
        {
            var mega = new MapLabMegaChunk(new Vector2Int(2, -1), 1);
            Assert.IsFalse(mega.SetCell(3, 3, 8, 0), "clearing an unpainted tile changes nothing");
            Assert.IsFalse(mega.IsPainted);
            Assert.AreEqual(1, mega.TileAt(3, 3, 8), "unpainted tiles show the base tile");
            Assert.IsTrue(mega.SetCell(3, 3, 8, 4));
            Assert.AreEqual(4, mega.TileAt(3, 3, 8));
            Assert.AreEqual(1, mega.PaintedCount());
            Assert.IsTrue(mega.SetBase(2));
            Assert.AreEqual(2, mega.TileAt(0, 0, 8));
            Assert.AreEqual(4, mega.TileAt(3, 3, 8), "paint stays when the base tile changes");
            Assert.IsTrue(mega.ClearCells());
            Assert.AreEqual(2, mega.TileAt(3, 3, 8));
            Assert.IsFalse(mega.BakedCurrent);
            mega.MarkBaked();
            Assert.IsTrue(mega.BakedCurrent);
            mega.Touch();
            Assert.IsFalse(mega.BakedCurrent, "an edit after the bake makes it out of date");
        }

        [Test]
        public void World_AddsRemovesAndFindsTheEdge()
        {
            var world = Track(ScriptableObject.CreateInstance<MapLabWorldData>());
            world.Add(new Vector2Int(0, 0), 1);
            world.Add(new Vector2Int(1, 0), 1);
            Assert.AreEqual(2, world.Count);
            Assert.AreSame(world.Find(new Vector2Int(1, 0)), world.Add(new Vector2Int(1, 0), 3), "adding twice keeps the first");
            Assert.IsTrue(world.IsEdge(new Vector2Int(2, 0)));
            Assert.IsTrue(world.IsEdge(new Vector2Int(0, -1)));
            Assert.IsFalse(world.IsEdge(new Vector2Int(2, 1)), "diagonal is not an edge");
            Assert.IsFalse(world.IsEdge(new Vector2Int(0, 0)), "an existing mega chunk is not an edge");
            Assert.AreEqual(new RectInt(0, 0, 2, 1), world.Extent());
            Assert.IsTrue(world.Remove(new Vector2Int(0, 0)));
            Assert.AreEqual(new RectInt(1, 0, 1, 1), world.Extent());
        }

        [Test]
        public void Files_ChunksLiveInTheirMegaChunkFolder()
        {
            string folder = $"{TempFolder}/Base";
            Assert.AreEqual($"{folder}/M_-1_1/Chunk_-1_5.png", MapLabLayerFiles.ChunkPath(folder, new Vector2Int(-1, 5), 4));
            var image = new Texture2D(4, 4);
            try
            {
                string written = MapLabLayerFiles.Write(folder, new Vector2Int(5, 0), image, 4);
                Assert.AreEqual($"{folder}/M_1_0/Chunk_5_0.png", written);
                Assert.IsTrue(File.Exists(written));
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
            File.WriteAllBytes($"{folder}/Chunk_-2_-3.png", new byte[] { 1 });
            Dictionary<Vector2Int, string> chunks = MapLabLayerFiles.Chunks(folder);
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(5, 0), new Vector2Int(-2, -3) }, chunks.Keys, "old flat files and mega folders are both found");
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(1, 0), new Vector2Int(-1, -1) }, MapLabLayerFiles.Megas(folder, 4));

            Assert.AreEqual(1, MapLabLayerFiles.Regroup(folder, 4));
            Assert.IsTrue(File.Exists($"{folder}/M_-1_-1/Chunk_-2_-3.png"), "regroup moves an old flat file into its mega chunk folder");
            Assert.IsFalse(File.Exists($"{folder}/Chunk_-2_-3.png"));

            MapLabLayerFiles.DeleteChunk(folder, new Vector2Int(-2, -3));
            MapLabLayerFiles.PruneEmptyFolders(folder);
            Assert.IsFalse(Directory.Exists($"{folder}/M_-1_-1"), "an empty mega chunk folder is removed");
            Assert.IsTrue(Directory.Exists($"{folder}/M_1_0"));
        }

        [Test]
        public void Camera_ZoomKeepsThePointUnderTheCursor()
        {
            var camera = new MapLabViewportCamera { Screen = new Rect(0f, 0f, 400f, 300f), Center = new Vector2(10f, 20f), Scale = 2f };
            Vector2 world = camera.ScreenToWorld(new Vector2(50f, 80f));
            Assert.That(Vector2.Distance(camera.WorldToScreen(world), new Vector2(50f, 80f)), Is.LessThan(0.001f));
            Assert.That(camera.ScreenToWorld(new Vector2(200f, 150f)), Is.EqualTo(new Vector2(10f, 20f)), "the centre of the screen is the camera centre");
            Assert.Greater(camera.ScreenToWorld(new Vector2(200f, 0f)).y, 20f, "screen up is world up");

            camera.ZoomAt(new Vector2(50f, 80f), 4f, 0.01f, 100f);
            Assert.AreEqual(8f, camera.Scale, 0.0001f);
            Assert.That(Vector2.Distance(camera.ScreenToWorld(new Vector2(50f, 80f)), world), Is.LessThan(0.001f));

            camera.Pan(new Vector2(16f, 8f));
            Assert.That(Vector2.Distance(camera.ScreenToWorld(new Vector2(66f, 88f)), world), Is.LessThan(0.001f), "the map follows the drag");

            camera.Frame(new Rect(0f, 0f, 128f, 64f), 0f, 0.01f, 100f);
            Assert.AreEqual(3.125f, camera.Scale, 0.0001f, "framing fits the wider side");
            Assert.AreEqual(new Vector2(64f, 32f), camera.Center);
            camera.ZoomAt(Vector2.zero, 1000f, 0.01f, 10f);
            Assert.AreEqual(10f, camera.Scale, 0.0001f, "zoom stops at the limit");
        }

        [Test]
        public void Canvas_OneMegaChunkPsdIs4096Pixels()
        {
            MapLabMap map = CreateMap();
            MapLabPaintCanvas canvas = MapLabPaintExchange.CanvasForMega(map, new Vector2Int(-1, 2));
            Assert.AreEqual(new Vector2Int(-4, 8), canvas.Origin);
            Assert.AreEqual(new Vector2Int(4, 4), canvas.Count);
            Assert.AreEqual(4096, canvas.Width);
            Assert.AreEqual(4096, canvas.Height);
            Assert.AreEqual("M_-1_2.psd", MapLabPaintExchange.MegaPsdName(new Vector2Int(-1, 2)));
            Assert.AreEqual(64f, map.MegaChunkSize, 0.0001f);
        }

        [Test]
        public void Status_FollowsFilesAndVersion()
        {
            var mega = new MapLabMegaChunk(Vector2Int.zero, 1);
            var baked = new HashSet<Vector2Int>();
            Assert.AreEqual(MapLabBakeStatus.State.NotBaked, MapLabMegaStatus.Of(mega, baked));
            baked.Add(Vector2Int.zero);
            Assert.AreEqual(MapLabBakeStatus.State.OutOfDate, MapLabMegaStatus.Of(mega, baked), "files from shapes but the floor never baked");
            mega.MarkBaked();
            Assert.AreEqual(MapLabBakeStatus.State.UpToDate, MapLabMegaStatus.Of(mega, baked));
        }
        #endregion

        #region [Private Methods]
        private T Track<T>(T item) where T : Object
        {
            _created.Add(item);
            return item;
        }

        private MapLabMap CreateMap()
        {
            var root = new GameObject("Map");
            _created.Add(root);
            return root.AddComponent<MapLabMap>();
        }
        #endregion
    }
}
