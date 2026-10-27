using System;
using System.Collections.Generic;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Tests
{
    public class MapLabSetsTests
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
        public void Sets_AddRemoveAndDeleteKeepObjectsInTheMap()
        {
            MapLabMap map = CreateMap("Map");
            ExtrudedShape plateau = Shape(map.transform, "Plateau");
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, 0.5f, 1f);
            PathShape road = Path(map.transform, "Road");
            CollectionAssert.AreEquivalent(new[] { plateau.gameObject, road.gameObject }, MapLabSets.Unassigned(map), "tiers travel with their plateau");

            MapLabSet cliffs = MapLabSets.CreateSet(map, "Cliffs");
            Assert.AreEqual(2, MapLabSets.Add(cliffs, new[] { plateau.gameObject, tier.gameObject, road.gameObject }), "a tier cannot leave its plateau");
            CollectionAssert.AreEqual(new[] { plateau.gameObject, road.gameObject }, MapLabSets.Members(cliffs));
            Assert.AreEqual(plateau.transform, tier.transform.parent);
            Assert.IsEmpty(MapLabSets.Unassigned(map));
            Assert.AreEqual(cliffs, MapLabSets.SetOf(road.gameObject));

            Assert.AreEqual(1, MapLabSets.Remove(map, new[] { road.gameObject }));
            Assert.AreEqual(map.transform, road.transform.parent, "removed objects go back to the map");
            CollectionAssert.AreEqual(new[] { road.gameObject }, MapLabSets.Unassigned(map));

            MapLabSets.Delete(map, cliffs);
            Assert.IsTrue(cliffs == null, "the set object is gone");
            Assert.AreEqual(map.transform, plateau.transform.parent, "deleting a set keeps its objects");
            Assert.IsEmpty(MapLabSets.SetsOf(map));
        }

        [Test]
        public void Sets_RefuseWhatDoesNotBelong()
        {
            MapLabMap map = CreateMap("Map");
            MapLabMap other = CreateMap("Other");
            ExtrudedShape plateau = Shape(map.transform, "Plateau");
            ExtrudedShape stranger = Shape(other.transform, "Stranger");
            MapLabSet set = MapLabSets.CreateSet(map, "Set");

            Assert.IsFalse(MapLabSets.CanAdd(set, map.gameObject), "the map itself");
            Assert.IsFalse(MapLabSets.CanAdd(set, set.gameObject), "the set itself");
            Assert.IsFalse(MapLabSets.CanAdd(set, stranger.gameObject), "an object of another map");
            Assert.IsFalse(MapLabSets.CanAdd(set, plateau.transform.Find(ExtrudedShape.VisualName).gameObject), "generated pieces");
            Assert.IsTrue(MapLabSets.CanAdd(set, plateau.gameObject));
            MapLabSets.Add(set, new[] { plateau.gameObject });
            Assert.IsFalse(MapLabSets.CanAdd(set, plateau.gameObject), "already in the set");
        }

        [Test]
        public void Sets_GetUniqueNamesAndTheirOwnColour()
        {
            MapLabMap map = CreateMap("Map");
            MapLabSet first = MapLabSets.CreateSet(map, "New Set");
            MapLabSet second = MapLabSets.CreateSet(map, "New Set");

            Assert.AreEqual("New Set 2", second.name);
            Assert.AreNotEqual(first.Color, second.Color);
            MapLabSets.Rename(second, "  Village  ");
            Assert.AreEqual("Village", second.name);
        }

        [Test]
        public void TypeLabel_DescribesEachKindOfObject()
        {
            MapLabMap map = CreateMap("Map");
            ExtrudedShape plateau = Shape(map.transform, "Plateau");
            ExtrudedShapeMenu.AddTier(plateau, 0.5f, 1f);
            ExtrudedShape box = Shape(map.transform, "Box");
            box.SetPoints(new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) }, new[] { true, true, true, true });

            Assert.AreEqual("Plateau, 1 tier on top", MapLabSets.TypeLabel(plateau.gameObject));
            Assert.AreEqual("Box shape", MapLabSets.TypeLabel(box.gameObject));
            Assert.AreEqual("Path", MapLabSets.TypeLabel(Path(map.transform, "Road").gameObject));
            Assert.AreEqual("Set", MapLabSets.TypeLabel(MapLabSets.CreateSet(map, "Set").gameObject));
        }

        [Test]
        public void TierStack_ListsEveryLayerFromTheGroundUp()
        {
            MapLabMap map = CreateMap("Map");
            ExtrudedShape ground = Shape(map.transform, "Plateau");
            ExtrudedShape middle = ExtrudedShapeMenu.AddTier(ground, 0.6f, 1f);
            ExtrudedShape top = ExtrudedShapeMenu.AddTier(middle, 0.5f, 1f);
            ExtrudedShape side = ExtrudedShapeMenu.AddTier(ground, 0.3f, 0.5f);

            var stack = MapLabSets.TierStack(top);
            Assert.AreEqual(ground, stack[0].Shape, "any tier finds its ground shape");
            CollectionAssert.AreEqual(new[] { ground, middle, top, side }, stack.ConvertAll(entry => entry.Shape));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 1 }, stack.ConvertAll(entry => entry.Depth), "a second tier on the ground sits beside the first");
            Assert.AreEqual("Plateau, 3 tiers on top", MapLabSets.TypeLabel(ground.gameObject));
            Assert.AreEqual("Tier 1 (ground)", MapLabSets.TierLabel(0));
            Assert.AreEqual("Tier 3", MapLabSets.TierLabel(2));
        }

        [Test]
        public void OutputFolder_IsNamedAfterTheMapWithoutTags()
        {
            Assert.AreEqual("Environment", MapLabOutputPaths.MapFolderName(CreateMap("[MapLab] Environment")));
            Assert.AreEqual("Town North East", MapLabOutputPaths.MapFolderName(CreateMap("Town: North/East")));
            Assert.AreEqual("Map", MapLabOutputPaths.MapFolderName(CreateMap("[MapLab]")));
            Assert.Throws<InvalidOperationException>(() => MapLabOutputPaths.MapFolder(CreateMap("Unsaved")), "bakes need a saved scene");
            Assert.AreEqual("12 KB", MapLabOutputPaths.Size(12 * 1024));
            Assert.AreEqual("1.6 MB", MapLabOutputPaths.Size(1661218));
        }
        #endregion

        #region [Private Methods]
        private MapLabMap CreateMap(string name)
        {
            var root = new GameObject(name);
            _created.Add(root);
            return root.AddComponent<MapLabMap>();
        }

        private static ExtrudedShape Shape(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<ExtrudedShape>();
        }

        private static PathShape Path(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var path = go.AddComponent<PathShape>();
            path.SetPoints(new[] { new Vector2(-2f, 0f), new Vector2(2f, 0f) });
            return path;
        }
        #endregion
    }
}
