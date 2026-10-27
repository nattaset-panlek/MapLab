using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class SortedLayerTests
    {
        #region [Variables/Fields]
        private static readonly MapLabPaintCanvas Canvas = new MapLabPaintCanvas(Vector2Int.zero, Vector2Int.one, 1f, 8);
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
        public void DrawOrder_FollowsLayerOrderThenDepth()
        {
            var keys = new[]
            {
                new MapLabPieceOwners.SortKey(0, 6, 1f),
                new MapLabPieceOwners.SortKey(0, 6, 3f),
                new MapLabPieceOwners.SortKey(0, 7, 9f),
                new MapLabPieceOwners.SortKey(0, 6, 2f)
            };
            CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, MapLabPieceOwners.DrawOrder(keys), "back to front: higher y first, a higher order always after");
        }

        [Test]
        public void Owners_TheFrontPieceOwnsWhereItIsOpaque()
        {
            var back = Square(0f, 0f, 1f, 1f, true);
            var front = Square(0f, 0f, 0.5f, 1f, true);
            var glass = Square(0f, 0f, 1f, 1f, false);
            var pieces = new List<List<MapLabPieceOwners.Triangle>> { back, front, glass };
            var order = new List<int> { 0, 1, 2 };
            byte[] composite = Filled(255);

            int[] owners = MapLabPieceOwners.Owners(Canvas, pieces, order, composite);

            Assert.AreEqual(2, owners[Index(1, 4)], "left half: the front piece");
            Assert.AreEqual(1, owners[Index(6, 4)], "right half: the back piece");
            Assert.IsFalse(owners.Contains(3), "a see-through piece drawn last owns nothing");

            int[] onlyGlass = MapLabPieceOwners.Owners(Canvas, new List<List<MapLabPieceOwners.Triangle>> { glass }, new List<int> { 0 }, composite);
            Assert.IsTrue(onlyGlass.All(owner => owner == 1), "with nothing opaque under it, a see-through piece keeps its pixels");
        }

        [Test]
        public void Cut_KeepsOwnPixelsAndAOnePixelSeam()
        {
            var pieces = new List<List<MapLabPieceOwners.Triangle>> { Square(0f, 0f, 1f, 1f, true), Square(0f, 0f, 0.5f, 1f, true) };
            byte[] composite = Filled(255);
            int[] owners = MapLabPieceOwners.Owners(Canvas, pieces, new List<int> { 0, 1 }, composite);
            RectInt[] rects = MapLabPieceOwners.PieceRects(owners, Canvas.Width, Canvas.Height, 2);

            Assert.AreEqual(new RectInt(3, 0, 5, 8), rects[0], "the back piece owns columns 4 to 7, plus one column of seam");
            byte[] back = MapLabPieceOwners.Cut(owners, composite, Canvas.Width, rects[0], 1, out bool empty);
            Assert.IsFalse(empty);
            Assert.AreEqual(255, back[(0 * 5 + 0) * 4 + 3], "column 3 belongs to the front piece but touches the back one: kept as seam");
            byte[] front = MapLabPieceOwners.Cut(owners, composite, Canvas.Width, new RectInt(0, 0, 8, 8), 2, out _);
            Assert.AreEqual(0, front[(0 * 8 + 6) * 4 + 3], "column 6 is two pixels away from the front piece: not in it");
        }

        [Test]
        public void Owners_SurviveAPngRoundTrip()
        {
            var owners = new[] { 0, 1, 255, 256, 4097, 65535 };
            CollectionAssert.AreEqual(owners, MapLabPieceOwners.DecodeOwners(MapLabPieceOwners.EncodeOwners(owners)));
        }

        [Test]
        public void Manifest_MapsPixelsToTheWorld()
        {
            MapLabSortedManifest manifest = MapLabSortedManifest.For(new MapLabPaintCanvas(new Vector2Int(-1, 0), new Vector2Int(2, 1), 4f, 16));
            var piece = new MapLabSortedManifest.Piece { x = 16, y = 32, width = 8, height = 16 };
            Assert.AreEqual(new Rect(-3f, 1f, 0.5f, 1f), manifest.WorldRect(piece), "pixel rows count from the top of the canvas (4 units high)");
        }

        [Test]
        public void Claims_YSortedPiecesGoOnlyToSortedLayers()
        {
            var root = new GameObject("Map");
            _created.Add(root);
            var map = root.AddComponent<MapLabMap>();
            var go = new GameObject("Plateau");
            go.transform.SetParent(root.transform, false);
            var plateau = go.AddComponent<ExtrudedShape>();
            plateau.Rebuild();
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All));

            MapLabLayerClaims.ClaimAll(map, out _, out List<Renderer> live);
            int pieces = MapLabStaticBaker.CollectSorted(map).Count;
            Assert.Greater(pieces, 1, "a y-sorted plateau is cut into several pieces");
            Assert.AreEqual(pieces, live.Count, "without a sorted layer every piece stays live");

            map.BakeLayers.Add(new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true });
            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out _, out live);
            Assert.AreEqual(pieces, claims["Walls"].Count);
            Assert.IsEmpty(live);
            CollectionAssert.AreEquivalent(new[] { plateau.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>() }, claims["Ground"], "the ground shadow stays with the ground layer");
            Assert.AreEqual(1, MapLabLayerClaims.CountShapes(claims["Walls"]));
        }

        [Test]
        public void SortedLayer_ShowsOnlyPiecesNearTheView()
        {
            var root = new GameObject("Walls");
            _created.Add(root);
            var near = new GameObject("Piece 0000");
            near.transform.SetParent(root.transform, false);
            SpriteRenderer nearImage = near.AddComponent<SpriteRenderer>();
            var far = new GameObject("Piece 0001");
            far.transform.SetParent(root.transform, false);
            SpriteRenderer farImage = far.AddComponent<SpriteRenderer>();
            var layer = root.AddComponent<MapLabSortedLayer>();
            layer.Setup("Walls", 64, new List<MapLabSortedLayer.Piece>
            {
                new MapLabSortedLayer.Piece(near, new Rect(0f, 0f, 2f, 3f)),
                new MapLabSortedLayer.Piece(far, new Rect(50f, 0f, 2f, 3f))
            });

            Assert.AreEqual(1, layer.Show(new Rect(-5f, -5f, 10f, 10f)));
            Assert.IsTrue(nearImage.enabled);
            Assert.IsFalse(farImage.enabled, "far pieces stop drawing but stay active, so their sorting group never has to be rebuilt");
            Assert.IsTrue(far.activeSelf);
            Assert.AreEqual(2, layer.ShowAll());
            Assert.IsTrue(farImage.enabled);
        }
        #endregion

        #region [Private Methods]
        private static List<MapLabPieceOwners.Triangle> Square(float x, float y, float width, float height, bool opaque)
        {
            var a = new Vector2(x, y);
            var b = new Vector2(x + width, y);
            var c = new Vector2(x + width, y + height);
            var d = new Vector2(x, y + height);
            return new List<MapLabPieceOwners.Triangle> { new MapLabPieceOwners.Triangle(a, b, c, opaque), new MapLabPieceOwners.Triangle(a, c, d, opaque) };
        }

        private static byte[] Filled(byte alpha)
        {
            var pixels = new byte[Canvas.Width * Canvas.Height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 100;
                pixels[i + 1] = 120;
                pixels[i + 2] = 140;
                pixels[i + 3] = alpha;
            }
            return pixels;
        }

        private static int Index(int x, int y)
        {
            return y * Canvas.Width + x;
        }
        #endregion
    }
}
