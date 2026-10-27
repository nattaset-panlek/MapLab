using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Tests
{
    public class DrawLayerTests
    {
        #region [Variables/Fields]
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
        public void Stack_KeepsTheDecorateLineAndPutsNewLayersAboveIt()
        {
            var layers = new[] { "Ground", "Decor", "Shadows", "Extra" };
            var paints = new[] { new MapLabLayerStack.Entry("Decor_paint", "Decor", true) };
            List<MapLabLayerStack.Entry> stack = MapLabLayerStack.Resolve(layers, paints, new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground", "Shadows" }, out int line);
            CollectionAssert.AreEqual(new[] { "Decor", "Decor_paint", "Ground", "Shadows", "Extra" }, stack.Select(entry => entry.Name).ToList(), "a new layer goes on top, paint stays with its layer");
            Assert.AreEqual(2, line, "Decor and its paint are under the line");

            MapLabLayerStack.Resolve(layers, new MapLabLayerStack.Entry[0], new[] { "Ground", "Decor" }, out int noLine);
            Assert.AreEqual(0, noLine, "without a saved line no layer is Decorate");
            List<MapLabLayerStack.Entry> legacy = MapLabLayerStack.Resolve(layers, new MapLabLayerStack.Entry[0], new[] { "Ground", MapLabLayerStack.LegacyActorsMarker, "Decor" }, out int legacyLine);
            Assert.AreEqual(0, legacyLine, "the old Actors line means nothing any more");
            Assert.IsFalse(legacy.Any(entry => entry.Name.StartsWith("[")));
            Assert.IsFalse(MapLabLayerNames.IsValidLayerName(MapLabLayerStack.DecorateMarker, out _), "nobody can name a layer like the line");
        }

        [Test]
        public void Stack_MovingUnderTheLineMakesALayerDecorate()
        {
            MapLabMap map = Map("Highlands");
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All));
            var decor = new MapLabBakeLayerSettings("Decor", MapLabBakeKinds.None);
            map.BakeLayers.Add(decor);
            var walls = new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true };
            map.BakeLayers.Add(walls);
            Assert.AreEqual(0, MapLabLayerStack.DecorateLayers(map).Count);

            MapLabLayerEdits.SetSide(map, decor, true);
            Assert.IsTrue(MapLabLayerStack.IsDecorate(map, "Decor"));
            CollectionAssert.AreEqual(new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground", "Shadows", "Walls" }, MapLabLayerStack.NamesWithLine(map), "the shadows get their own layer so they fall on the decor");
            Assert.AreEqual(MapLabDrawLayers.ShadowKinds, MapLabLayerStack.Find(map, "Shadows").Kinds);
            Assert.AreEqual(MapLabBakeKinds.None, MapLabLayerStack.Find(map, "Ground").Kinds & MapLabDrawLayers.ShadowKinds);

            MapLabLayerStack.Save(map, new[] { "Decor", "Ground", "Shadows", "Walls" });
            CollectionAssert.AreEqual(new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground", "Shadows", "Walls" }, map.LayerStack, "saving without the line keeps which layers were under it");

            MapLabLayerEdits.SetSide(map, walls, true);
            Assert.IsFalse(MapLabLayerStack.IsDecorate(map, "Walls"), "a sorted layer never goes under the line");

            MapLabLayerEdits.Move(map, "Decor", 1);
            Assert.IsFalse(MapLabLayerStack.IsDecorate(map, "Decor"), "one step up crosses the line back");
            Assert.AreEqual(0, MapLabLayerStack.DecorateLine(map));
        }

        [Test]
        public void Upgrade_TurnsTheOldActorsLineIntoTheDecorateLine()
        {
            MapLabMap map = Map("Highlands");
            MapLabSet terrace = MapLabSets.CreateSet(map, "Terrace");
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites | MapLabBakeKinds.Paths | MapLabBakeKinds.FlatShapes | MapLabBakeKinds.Floor));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Shadows", MapLabDrawLayers.ShadowKinds));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true });
            var old = new MapLabBakeLayerSettings("Ground Decorate", MapLabBakeKinds.PropShadows | MapLabBakeKinds.PulledShapes | MapLabBakeKinds.PulledProps);
            old.Sets.Add(terrace);
            map.BakeLayers.Add(old);
            map.LayerStack.AddRange(new[] { "Ground", "Shadows", "Ground Decorate", MapLabLayerStack.LegacyActorsMarker, "Walls" });

            Assert.IsTrue(MapLabLayerStack.Upgrade(map));
            CollectionAssert.AreEqual(new[] { "Ground Decorate", MapLabLayerStack.DecorateMarker, "Ground", "Shadows", "Walls" }, MapLabLayerStack.NamesWithLine(map));
            Assert.IsTrue(MapLabLayerStack.IsDecorate(map, "Ground Decorate"));
            Assert.AreEqual(MapLabBakeKinds.None, old.Kinds, "a Decorate layer takes sets, not types");
            Assert.IsFalse(map.BakeLayers.Any(layer => (layer.Kinds & MapLabLayerStack.LegacyPullKinds) != 0));
            Assert.IsFalse(MapLabLayerStack.Upgrade(map), "only once");
        }

        [Test]
        public void Plateau_AsGroundHasNoSortingGroupNoColliderAndNoTop()
        {
            ExtrudedShape plateau = Plateau("Terrace", null, Vector3.zero);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau);
            Assert.IsNotNull(plateau.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>());
            Assert.IsTrue(Pieces(plateau).All(piece => piece.GetComponent<SortingGroup>() != null), "a normal plateau sorts with the actors");

            Assert.IsTrue(plateau.SetDrawAsGround(true, 2));
            Assert.IsFalse(plateau.YSort);
            Assert.IsTrue(Pieces(plateau).All(piece => piece.GetComponent<SortingGroup>() == null && piece.GetComponent<MeshRenderer>().sortingOrder == 2));
            Assert.IsNull(plateau.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>(), "floor art does not block walking");
            Assert.IsTrue(tier.DrawsAsGround, "tiers follow their plateau");
            Assert.AreEqual(3, tier.SortingOrder, "a tier draws just above its plateau");
            Assert.AreEqual(1, plateau.ShadowOrder, "the shadow goes just under it");
            Assert.IsFalse(plateau.SetDrawAsGround(true, 2), "no change, no rebuild");

            Assert.IsTrue(plateau.SetDrawAsGround(true, 2, false));
            Assert.IsFalse(plateau.CastsShadow, "the shadow switch is off");
            Assert.IsFalse(tier.CastsShadow, "tiers follow the switch");
            plateau.SetDrawAsGround(true, 2, true);
            Assert.IsTrue(plateau.CastsShadow);

            plateau.SetDrawAsGround(false, 0);
            Assert.IsNotNull(plateau.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>(), "it comes back as it was");
            Assert.AreEqual(6, plateau.SortingOrder);
        }

        [Test]
        public void Sync_DecorateTakesEverythingInItsSets()
        {
            MapLabMap map = Map("Highlands");
            MapLabSet terrace = MapLabSets.CreateSet(map, "Terrace");
            MapLabSet props = MapLabSets.CreateSet(map, "Props");
            ExtrudedShape inside = Plateau("Low Terrace", terrace.transform, Vector3.zero);
            ExtrudedShape outside = Plateau("Cliff", map.transform, new Vector3(20f, 0f, 0f));
            GameObject tree = Tree("Tree", props.transform);
            var decal = new GameObject("Decal").AddComponent<SpriteRenderer>();
            decal.transform.SetParent(terrace.transform, false);

            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All));
            var decor = new MapLabBakeLayerSettings("Ground Decor", MapLabBakeKinds.None);
            decor.Sets.Add(terrace);
            decor.Sets.Add(props);
            map.BakeLayers.Add(decor);
            MapLabLayerStack.Save(map, new[] { "Ground Decor", MapLabLayerStack.DecorateMarker, "Ground" });

            MapLabDrawLayers.Sync(map);
            Assert.IsTrue(inside.DrawsAsGround);
            Assert.AreEqual(-3, inside.SortingOrder, "the layer right under the line draws 4 under the lowest shadow (1)");
            Assert.IsFalse(outside.DrawsAsGround, "a plateau outside the sets stays a cliff");
            var ground = tree.GetComponent<MapLabGroundProp>();
            Assert.IsNotNull(ground);
            Assert.AreEqual(-3, tree.GetComponent<SpriteRenderer>().sortingOrder);
            Assert.AreEqual(-3 + MapLabGroundProp.MaxSpread, tree.transform.Find("Canopy").GetComponent<SpriteRenderer>().sortingOrder, "the canopy stays above the trunk but under every shadow");
            Assert.IsFalse(tree.GetComponent<BoxCollider2D>().enabled);
            CollectionAssert.AreEquivalent(new Component[] { inside, ground }, MapLabDrawLayers.Members(map, "Ground Decor"));

            Renderer piece = Pieces(inside).First().GetComponent<MeshRenderer>();
            Assert.AreEqual(MapLabBakeKinds.PulledShapes, MapLabLayerClaims.KindOf(piece, map));
            List<Renderer> claimed = MapLabLayerClaims.Claimed(map, "Ground Decor");
            Assert.IsTrue(claimed.Contains(piece), "the Decorate layer bakes its plateaus");
            Assert.IsTrue(claimed.Contains(tree.GetComponent<SpriteRenderer>()));
            Assert.IsTrue(claimed.Contains(decal), "and every other ground sprite in its sets");
            Assert.IsFalse(MapLabLayerClaims.Claimed(map, "Ground").Contains(decal));

            MapLabLayerStack.SetSide(map, "Ground Decor", false);
            MapLabDrawLayers.Sync(map);
            Assert.IsFalse(inside.DrawsAsGround, "above the line it sorts again");
            Assert.IsNull(tree.GetComponent<MapLabGroundProp>());
            Assert.AreEqual(6, tree.GetComponent<SpriteRenderer>().sortingOrder);
            Assert.AreEqual(100, tree.transform.Find("Canopy").GetComponent<SpriteRenderer>().sortingOrder);
            Assert.IsTrue(tree.GetComponent<BoxCollider2D>().enabled);
        }

        [Test]
        public void CastShadows_TurnsTheDecorShadowsOffAndOn()
        {
            MapLabMap map = Map("Highlands");
            MapLabSet terrace = MapLabSets.CreateSet(map, "Terrace");
            ExtrudedShape plateau = Plateau("Low Terrace", terrace.transform, Vector3.zero);
            GameObject tree = Tree("Tree", terrace.transform, true);
            SpriteRenderer shadow = tree.transform.Find("Shadow").GetComponent<SpriteRenderer>();
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites));
            var decor = new MapLabBakeLayerSettings("Decor", MapLabBakeKinds.None) { CastShadows = false };
            decor.Sets.Add(terrace);
            map.BakeLayers.Add(decor);
            MapLabLayerStack.Save(map, new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground" });

            MapLabDrawLayers.Sync(map);
            Assert.IsFalse(plateau.CastsShadow);
            Assert.IsFalse(shadow.enabled, "the prop's own shadow is hidden");
            Assert.IsTrue(tree.GetComponent<SpriteRenderer>().enabled, "the prop itself stays");

            decor.CastShadows = true;
            MapLabDrawLayers.Sync(map);
            Assert.IsTrue(plateau.CastsShadow);
            Assert.IsTrue(shadow.enabled);

            decor.CastShadows = false;
            MapLabDrawLayers.Sync(map);
            MapLabLayerStack.SetSide(map, "Decor", false);
            MapLabDrawLayers.Sync(map);
            Assert.IsTrue(shadow.enabled, "above the line the shadow comes back");
        }

        [Test]
        public void Shadows_FallOnTheDecor()
        {
            MapLabMap map = Map("Highlands");
            MapLabSet terrace = MapLabSets.CreateSet(map, "Terrace");
            ExtrudedShape ground = Plateau("Low Terrace", terrace.transform, Vector3.zero);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(ground);
            ExtrudedShape cliff = Plateau("Cliff", map.transform, new Vector3(-3f, 2f, 0f));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites | MapLabBakeKinds.Paths));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Shadows", MapLabDrawLayers.ShadowKinds));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Top", MapLabBakeKinds.Floor));
            var decor = new MapLabBakeLayerSettings("Ground Decor", MapLabBakeKinds.None);
            decor.Sets.Add(terrace);
            map.BakeLayers.Add(decor);
            MapLabLayerStack.Save(map, new[] { "Ground Decor", MapLabLayerStack.DecorateMarker, "Ground", "Shadows", "Top" });
            MapLabDrawLayers.Sync(map);

            Renderer cliffShadow = cliff.transform.Find(ExtrudedShape.ShadowName).GetComponent<MeshRenderer>();
            Assert.Less(tier.SortingOrder, cliffShadow.sortingOrder, "the cliff's shadow draws over the terrace and its tier");
            Assert.IsTrue(tier.CastsShadow, "a tier on a decor terrace still shades it");
            Renderer ownShadow = ground.transform.Find(ExtrudedShape.ShadowName).GetComponent<MeshRenderer>();
            Assert.Less(ownShadow.sortingOrder, ground.SortingOrder, "its own shadow stays under it");
            Assert.AreEqual(MapLabBakeKinds.PulledShapes, MapLabLayerClaims.KindOf(ownShadow, map), "its own shadow bakes with it");
            List<string> order = MapLabLayerStack.DrawOrder(map, MapLabLayerStack.Resolve(map, false)).Select(entry => entry.Name).ToList();
            CollectionAssert.AreEqual(new[] { "Ground", "Ground Decor", "Shadows", "Top" }, order, "baked, the decor sits over the floor and under the shadows");
        }

        [Test]
        public void DrawOrder_KeepsTheDecorOverAFloorThatAlsoHoldsShadows()
        {
            MapLabMap map = Map("Highlands");
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Top", MapLabBakeKinds.None));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Decor", MapLabBakeKinds.None));
            MapLabLayerStack.Save(map, new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground", "Top" });
            List<string> order = MapLabLayerStack.DrawOrder(map, MapLabLayerStack.Resolve(map, false)).Select(entry => entry.Name).ToList();
            CollectionAssert.AreEqual(new[] { "Ground", "Decor", "Top" }, order, "the floor never covers the decor");
            Assert.IsTrue(MapLabLayerStack.MixesShadows(MapLabLayerStack.Find(map, "Ground")));
        }

        [Test]
        public void Types_EachTypeIsDrawnIntoOneLayer()
        {
            MapLabMap map = Map("Highlands");
            var ground = new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All);
            var mid = new MapLabBakeLayerSettings("Mid", MapLabBakeKinds.None);
            var walls = new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true };
            var decor = new MapLabBakeLayerSettings("Decor", MapLabBakeKinds.None);
            map.BakeLayers.AddRange(new[] { ground, mid, walls, decor });
            MapLabLayerStack.Save(map, new[] { "Decor", MapLabLayerStack.DecorateMarker, "Ground", "Mid", "Walls" });

            Assert.AreEqual(ground, MapLabLayerTypes.Owner(map, MapLabBakeKinds.Paths));
            CollectionAssert.AreEqual(new[] { walls }, MapLabLayerTypes.Targets(map, MapLabBakeKinds.SortedShapes));
            Assert.IsFalse(MapLabLayerTypes.Targets(map, MapLabBakeKinds.Paths).Contains(decor), "Decorate layers take sets, not types");

            MapLabLayerTypes.Route(map, MapLabBakeKinds.Paths, "Mid");
            Assert.AreEqual(mid, MapLabLayerTypes.Owner(map, MapLabBakeKinds.Paths));
            Assert.AreEqual(MapLabBakeKinds.None, ground.Kinds & MapLabBakeKinds.Paths, "it leaves its old layer");
            CollectionAssert.AreEqual(new[] { MapLabBakeKinds.Paths }, MapLabLayerTypes.Held(map, mid));

            MapLabLayerTypes.Route(map, MapLabBakeKinds.Paths, null);
            Assert.IsNull(MapLabLayerTypes.Owner(map, MapLabBakeKinds.Paths), "Keep live");
            Assert.IsFalse(map.BakeLayers.Any(layer => (layer.Kinds & MapLabBakeKinds.Paths) != 0));

            MapLabLayerTypes.Route(map, MapLabBakeKinds.Paths, "Mid");
            ground.Kinds |= MapLabBakeKinds.Paths;
            MapLabLayerTypes.Release(map, ground);
            Assert.AreEqual(MapLabBakeKinds.None, ground.Kinds & MapLabBakeKinds.Paths, "a layer coming back above the line gives up what another layer holds");
        }
        #endregion

        #region [Private Methods]
        private MapLabMap Map(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<MapLabMap>();
        }

        private ExtrudedShape Plateau(string name, Transform parent, Vector3 position)
        {
            var points = new List<Vector2> { new Vector2(-2f, -1f), new Vector2(2f, -1f), new Vector2(2.5f, 1f), new Vector2(0f, 2f), new Vector2(-2.5f, 1f) };
            ExtrudedShape shape = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, name, parent, position, points, 1.5f, 0.4f, 6);
            if (parent == null) _created.Add(shape.gameObject);
            return shape;
        }

        private GameObject Tree(string name, Transform parent, bool withShadow = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var trunk = go.AddComponent<SpriteRenderer>();
            trunk.sortingOrder = 6;
            go.AddComponent<BoxCollider2D>();
            var top = new GameObject("Canopy");
            top.transform.SetParent(go.transform, false);
            top.AddComponent<SpriteRenderer>().sortingOrder = 100;
            if (withShadow)
            {
                var shadow = new GameObject("Shadow");
                shadow.transform.SetParent(go.transform, false);
                shadow.AddComponent<SpriteRenderer>().sortingOrder = 1;
            }
            if (parent == null) _created.Add(go);
            return go;
        }

        private static List<GameObject> Pieces(ExtrudedShape shape)
        {
            Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
            var pieces = new List<GameObject>();
            foreach (Transform child in visual) pieces.Add(child.gameObject);
            return pieces;
        }
        #endregion
    }
}
