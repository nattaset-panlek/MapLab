using System.Collections.Generic;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class WalkablePageTests
    {
        #region [Variables/Fields]
        private const int IgnoredLayer = 2;

        private readonly List<GameObject> _created = new List<GameObject>();
        private MapLabMap _map;
        #endregion

        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            var root = new GameObject("WalkablePageMap");
            _created.Add(root);
            _map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_bounds").rectValue = new Rect(-5f, -5f, 10f, 10f);
            serialized.FindProperty("_cellSize").floatValue = 0.5f;
            serialized.FindProperty("_obstacleLayers").intValue = ~(1 << IgnoredLayer);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

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
        public void Fit_CoversEverythingWithAMarginOnTheCellGrid()
        {
            var parts = new[] { new Bounds(new Vector3(0.3f, 0.2f), new Vector3(4f, 2f)) };
            Assert.IsTrue(MapLabWalkableArea.TryFit(parts, 0.5f, 1f, out Rect area));
            Assert.AreEqual(Rect.MinMaxRect(-3f, -2f, 3.5f, 2.5f), area, "-1.7..2.3 x -0.8..1.2 plus 1 unit, then out to the 0.5 grid");
            Assert.IsFalse(MapLabWalkableArea.TryFit(new Bounds[0], 0.5f, 1f, out _), "nothing to fit");
            Assert.AreEqual(new Rect(0f, 0f, 0.5f, 0.5f), MapLabWalkableArea.SnapNearest(new Rect(0.1f, 0.1f, 0.1f, 0.1f), 0.5f), "a dragged frame never gets smaller than one cell");
            Assert.AreEqual(new Vector2Int(13, 9), MapLabWalkableArea.Cells(area, 0.5f));
        }

        [Test]
        public void Scan_CountsKindsAndSkipsWhatNeverBlocks()
        {
            Collider("Wall", Vector2.zero).AddComponent<BoxCollider2D>();
            Collider("Rock", new Vector2(2f, 2f)).AddComponent<CircleCollider2D>();
            Collider("Cliff", new Vector2(-2f, -2f)).AddComponent<PolygonCollider2D>();
            Collider("Edge Wall", new Vector2(4.8f, 0f)).AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            Collider("Zone", new Vector2(-3f, 3f)).AddComponent<BoxCollider2D>().isTrigger = true;
            GameObject ghost = Collider("Ghost", new Vector2(3f, -3f));
            ghost.layer = IgnoredLayer;
            ghost.AddComponent<BoxCollider2D>();
            Collider("Off", new Vector2(-3f, -3f)).AddComponent<BoxCollider2D>().enabled = false;
            Collider("Far Away", new Vector2(40f, 0f)).AddComponent<BoxCollider2D>();

            MapLabWalkableObstacles.Report report = MapLabWalkableObstacles.Scan(_map);

            Assert.AreEqual(4, report.Blocking.Count);
            CollectionAssert.AreEqual(new[] { new KeyValuePair<string, int>("Box", 2), new KeyValuePair<string, int>("Circle", 1), new KeyValuePair<string, int>("Polygon", 1) }, report.KindsByCount());
            Assert.AreEqual(1, report.Triggers);
            Assert.AreEqual(1, report.OtherLayers);
            Assert.AreEqual(1, report.TurnedOff);
            Assert.AreEqual(2, report.Outside.Count, "the edge wall reaches out, the far one is fully out");
            Assert.AreEqual("Triggers never block walking. Not counted: 1 trigger, 1 on other layers, 1 turned off.", MapLabWalkableObstacles.Ignored(report));
            CollectionAssert.AreEqual(report.Blocking, MapLabWalkableBaker.CollectObstacles(_map), "the bake blocks exactly what the page lists");
        }

        [Test]
        public void FitToMap_LeavesNoColliderOutside()
        {
            Collider("Wall", new Vector2(0.2f, 0.2f)).AddComponent<BoxCollider2D>();
            Collider("Far Wall", new Vector2(12.2f, -7.2f)).AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            Assert.AreEqual(1, MapLabWalkableObstacles.Scan(_map).Outside.Count);

            Assert.IsTrue(MapLabWalkableArea.FitToMap(_map));

            Assert.IsEmpty(MapLabWalkableObstacles.Scan(_map).Outside);
            Assert.AreEqual(Rect.MinMaxRect(-0.5f, -8.5f, 13.5f, 1f), _map.Bounds, "-0.3..13.2 x -8.2..0.7, out to the 0.5 grid");
        }

        [Test]
        public void Changes_NameWhatMovedWasAddedOrRemoved()
        {
            GameObject wall = Collider("Wall", Vector2.zero);
            wall.AddComponent<BoxCollider2D>();
            GameObject pillar = Collider("Pillar", new Vector2(2f, 0f));
            pillar.AddComponent<BoxCollider2D>();
            MapLabWalkableChanges.Snapshot before = MapLabWalkableChanges.Take(_map);
            Assert.IsNull(MapLabWalkableChanges.Describe(before, MapLabWalkableChanges.Take(_map)), "nothing changed");

            wall.transform.position = new Vector3(1f, 1f, 0f);
            Collider("Rock", new Vector2(-2f, 2f)).AddComponent<CircleCollider2D>();
            Object.DestroyImmediate(pillar);
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_cellSize").floatValue = 0.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual("Wall moved or changed, Rock added, Pillar removed, cell size changed", MapLabWalkableChanges.Describe(before, MapLabWalkableChanges.Take(_map)));
        }

        [Test]
        public void Changes_NameAShapeByItselfNotItsCollisionChild()
        {
            GameObject holder = Collider(ExtrudedShape.CollisionName, Vector2.zero);
            holder.transform.SetParent(Collider("Pillar", Vector2.zero).transform, true);
            Assert.AreEqual("Pillar", MapLabWalkableChanges.DisplayName(holder.AddComponent<PolygonCollider2D>()));
            Assert.AreEqual("Rock", MapLabWalkableChanges.DisplayName(Collider("Rock", Vector2.one).AddComponent<CircleCollider2D>()));
        }

        [Test]
        public void Names_GroupRepeatsAndCountTheRest()
        {
            Assert.AreEqual("Boulder x2 and Pillar", MapLabWalkableChanges.Names(new List<string> { "Boulder", "Pillar", "Boulder" }));
            Assert.AreEqual("Gate, Tree and 3 more", MapLabWalkableChanges.Names(new List<string> { "Gate", "Tree", "Rock", "Rock", "Wall" }));
        }

        [Test]
        public void Ids_TellSameNamedSiblingsApart()
        {
            var first = Collider("Rock", Vector2.zero).AddComponent<BoxCollider2D>();
            var second = Collider("Rock", Vector2.one).AddComponent<BoxCollider2D>();
            string a = MapLabWalkableChanges.Id(_map.transform, first);
            string b = MapLabWalkableChanges.Id(_map.transform, second);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual("Rock#1|BoxCollider2D#0", b);
        }

        [Test]
        public void BlockedRects_MergeCellsIntoFewRectangles()
        {
            var walkable = new bool[12];
            for (int i = 0; i < walkable.Length; i++) walkable[i] = true;
            foreach (var (column, row) in new[] { (1, 0), (2, 0), (1, 1), (2, 1), (3, 2) }) walkable[row * 4 + column] = false;
            var data = new WalkableAreaJson { bounds = new WalkableBounds(new Rect(10f, 20f, 4f, 3f)), cellSize = 1f, columns = 4, rows = 3, cells = MapLabWalkableBaker.EncodeCells(walkable) };

            CollectionAssert.AreEqual(new[] { new Rect(11f, 20f, 2f, 2f), new Rect(13f, 22f, 1f, 1f) }, MapLabWalkableScene.BlockedRects(data));
        }
        #endregion

        #region [Private Methods]
        private GameObject Collider(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_map.transform, false);
            go.transform.position = position;
            return go;
        }
        #endregion
    }
}
