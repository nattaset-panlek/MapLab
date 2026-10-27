using System.Collections.Generic;
using MapLab.Editor;
using MapLab.Samples.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MapLab.Tests
{
    public class WalkableAreaBakeTests
    {
        #region [Variables/Fields]
        private static readonly Rect Area = new Rect(-5f, -5f, 10f, 10f);
        private const float CellSize = 0.5f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private MapLabMap _map;
        #endregion

        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            GameObject root = Create("BakeTestMap", null, Vector2.zero);
            _map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_bounds").rectValue = Area;
            serialized.FindProperty("_cellSize").floatValue = CellSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Create("Wall", root.transform, Vector2.zero).AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            Create("Trigger", root.transform, new Vector2(3f, 3f)).AddComponent<BoxCollider2D>().isTrigger = true;
            Create("Outside The Map", null, new Vector2(-3f, 3f)).AddComponent<BoxCollider2D>();
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
        public void Grid_BlocksOnlySolidCollidersUnderTheMap()
        {
            WalkableAreaJson data = MapLabWalkableBaker.Compute(_map);
            bool[] cells = MapLabWalkableBaker.DecodeCells(data.cells, data.CellCount);

            Assert.AreEqual(20, data.columns);
            Assert.AreEqual(20, data.rows);
            Assert.IsFalse(cells[Index(data, 0.25f, 0.25f)], "wall");
            Assert.IsFalse(cells[Index(data, -0.75f, -0.75f)], "wall corner");
            Assert.IsTrue(cells[Index(data, 1.25f, 0.25f)], "cell next to the wall");
            Assert.IsTrue(cells[Index(data, -4.75f, -4.75f)], "open ground");
            Assert.IsTrue(cells[Index(data, 3.25f, 3.25f)], "trigger");
            Assert.IsTrue(cells[Index(data, -2.75f, 3.25f)], "collider outside the map root");
        }

        [Test]
        public void Polygons_OutlineIsCounterClockwiseAndHolesClockwise()
        {
            WalkableAreaJson data = MapLabWalkableBaker.Compute(_map);

            Assert.AreEqual(4, data.outline.Length);
            Assert.Greater(MapLabWalkableBaker.SignedArea(data.outline), 0f);
            Assert.AreEqual(1, data.holes.Length);
            Assert.AreEqual(4, data.holes[0].points.Length);
            Assert.AreEqual(-4f, MapLabWalkableBaker.SignedArea(data.holes[0].points), 0.001f);
        }

        [Test]
        public void Cells_SurviveEncodeAndDecode()
        {
            var cells = new[] { true, false, false, true, true, true, false, true, true, false, true };
            Assert.AreEqual(cells, MapLabWalkableBaker.DecodeCells(MapLabWalkableBaker.EncodeCells(cells), cells.Length));
        }

        [Test]
        public void SampleReader_ReadsTheBakedJsonWithoutMapLabTypes()
        {
            string json = JsonUtility.ToJson(MapLabWalkableBaker.Compute(_map), true);
            WalkableAreaData area = WalkableAreaData.Parse(json);

            Assert.AreEqual(WalkableAreaJson.CurrentVersion, area.version);
            Assert.AreEqual(Area, area.Bounds);
            Assert.IsFalse(area.IsWalkable(new Vector2(0.25f, 0.25f)));
            Assert.IsTrue(area.IsWalkable(new Vector2(-4.75f, -4.75f)));
            Assert.AreEqual(1, area.HoleOutlines().Length);

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(NavMesh2DFactory.Build(area.Bounds, area.HoleOutlines(), 0.3f));
            try
            {
                Assert.IsFalse(NavMesh.SamplePosition(Vector3.zero, out _, 0.3f, NavMesh.AllAreas));
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(-3f, 0f, 0f), out _, 0.5f, NavMesh.AllAreas));
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
            }
        }
        #endregion

        #region [Private Methods]
        private GameObject Create(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            if (parent == null) _created.Add(go);
            return go;
        }

        private static int Index(WalkableAreaJson data, float x, float y)
        {
            int column = Mathf.FloorToInt((x - data.bounds.x) / data.cellSize);
            int row = Mathf.FloorToInt((y - data.bounds.y) / data.cellSize);
            return row * data.columns + column;
        }
        #endregion
    }
}
