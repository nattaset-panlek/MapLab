using System;
using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Elevation;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Tests
{
    public class ElevationBakeTests
    {
        #region [Variables/Fields]
        private const string Folder = "Assets/MapLabElevationTests";
        private const float Height = 1.5f;
        private const float TierHeight = 1f;
        private const float TierScale = 0.45f;

        private static readonly Vector2[] Rectangle =
        {
            new Vector2(-4.2f, -3.2f), new Vector2(4.2f, -3.2f), new Vector2(4.2f, 3.2f), new Vector2(-4.2f, 3.2f)
        };

        private static readonly bool[] Corners = { true, true, true, true };

        private readonly List<GameObject> _created = new List<GameObject>();
        private MapLabMap _map;

        [Serializable]
        private class Reader
        {
            public int version;
            public float cellSize;
            public int columns;
            public int rows;
            public float stepHeight;
            public ReaderLevel[] levels;
            public string cellLevel;
        }

        [Serializable]
        private struct ReaderLevel
        {
            public int id;
            public string name;
            public float height;
            public float sortBias;
            public int below;
        }
        #endregion

        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            var root = new GameObject("ElevationMap");
            _created.Add(root);
            _map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_bounds").rectValue = new Rect(-6f, -6f, 12f, 12f);
            serialized.FindProperty("_cellSize").floatValue = 0.5f;
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
            AssetDatabase.DeleteAsset(Folder);
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void WithoutWalkOnTop_ThereIsNoElevation()
        {
            Plateau("Plateau", Height, false);
            Assert.IsNull(MapLabElevationBaker.Compute(_map, MapLabWalkableBaker.Compute(_map)));
            Assert.IsEmpty(MapLabElevationBaker.StampText(_map));
        }

        [Test]
        public void WalkOnTop_LeavesWalkableJsonUnchanged()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, false);
            string before = JsonUtility.ToJson(MapLabWalkableBaker.Compute(_map), true);
            SetWalkOnTop(plateau, true);
            Assert.AreEqual(before, JsonUtility.ToJson(MapLabWalkableBaker.Compute(_map), true));
        }

        [Test]
        public void Levels_ListGroundSurfacesAndTiers()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            WalkableElevationJson data = Compute();

            Assert.AreEqual(WalkableElevationJson.CurrentVersion, data.version);
            Assert.AreEqual(3, data.levels.Length);
            AssertLevel(data.levels[0], 0, MapLabElevationBaker.GroundName, 0f, 0f, 0);
            AssertLevel(data.levels[1], 1, "Plateau", Height, plateau.SurfaceSortBias, 0);
            AssertLevel(data.levels[2], 2, $"Plateau/{tier.name}", Height + TierHeight, tier.SurfaceSortBias, 1);
            Assert.AreEqual(0.51f, data.levels[1].sortBias, 0.0001f);
            Assert.AreEqual(1.02f, data.levels[2].sortBias, 0.0001f);
        }

        [Test]
        public void Cells_TakeTheTopmostSurface()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            WalkableElevationJson data = Compute();

            Assert.AreEqual(1, Level(data, -3.25f, -2.25f), "plateau top");
            Assert.AreEqual(2, Level(data, 0.25f, 1.25f), "tier top");
            Assert.AreEqual(0, Level(data, -5.25f, -5.25f), "open ground");
            Assert.AreEqual(WalkableElevationJson.Blocked, Level(data, 4.25f, 0.25f), "the ground cell that touches the cliff");
        }

        [Test]
        public void PropsBlockOnlyTheSurfaceTheyStandOn()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            Box("Rock On Top", plateau.transform, new Vector2(-3f, 2f));
            Box("Rock On The Ground", _map.transform, new Vector2(5.25f, -5.25f));
            WalkableElevationJson data = Compute();

            Assert.AreEqual(WalkableElevationJson.Blocked, Level(data, -2.75f, 2.25f), "rock on the plateau");
            Assert.AreEqual(1, Level(data, -1.25f, 2.25f), "next to the rock on the plateau");
            Assert.AreEqual(WalkableElevationJson.Blocked, Level(data, 5.25f, -5.25f), "rock on the ground");
        }

        [Test]
        public void TierYouCannotWalkOn_BlocksThePlateauUnderIt()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            SetWalkOnTop(tier, false);
            WalkableElevationJson data = Compute();

            Assert.AreEqual(2, data.levels.Length);
            Assert.AreEqual(WalkableElevationJson.Blocked, Level(data, 0.25f, 1.25f), "under the tier");
            Assert.AreEqual(1, Level(data, -3.25f, -2.25f), "plateau around the tier");
        }

        [Test]
        public void LowSurfaces_AreSteppedOnWithoutACliff()
        {
            Plateau("Curb", 0.2f, true);
            MapLabElevationBaker.Report report = MapLabElevationBaker.Scan(_map);
            WalkableElevationJson data = Compute();

            Assert.IsEmpty(report.Islands);
            Assert.IsEmpty(data.cliffs);
            Assert.AreEqual(0, Level(data, 4.25f, 0.25f), "the ground next to a curb stays walkable");
            Assert.AreEqual(1, Level(data, 3.75f, 0.25f));
        }

        [Test]
        public void TallSurfaces_HaveCliffsAndNoWayUpYet()
        {
            Plateau("Plateau", Height, true);
            MapLabElevationBaker.Report report = MapLabElevationBaker.Scan(_map);
            WalkableElevationJson data = Compute();

            CollectionAssert.AreEqual(new[] { "Plateau" }, report.Islands);
            Assert.AreEqual(1, data.cliffs.Length);
            Assert.AreEqual(1, data.cliffs[0].level);
            Assert.Greater(MapLabWalkableBaker.SignedArea(data.cliffs[0].points), 0f, "outer cliffs run counter-clockwise");
        }

        [Test]
        public void Ring_RimIsTheSurfaceAndTheCourtIsGround()
        {
            ExtrudedShape ring = Plateau("Ring", Height, true);
            Configure(ring, serialized =>
            {
                serialized.FindProperty("_type").enumValueIndex = (int)ExtrudedShapeType.Ring;
                serialized.FindProperty("_rimWidth").floatValue = 1f;
            });
            WalkableElevationJson data = Compute();

            Assert.AreEqual(0, Level(data, 0.25f, 0.25f), "court");
            Assert.AreEqual(1, Level(data, -3.75f, 0.25f), "rim");
            Assert.AreEqual(2, data.cliffs.Length);
            Assert.AreEqual(1, data.cliffs.Count(cliff => cliff.inner));
        }

        [Test]
        public void OverlappingSurfaces_AreReported()
        {
            Plateau("West", Height, true);
            ExtrudedShape east = Plateau("East", Height + 0.5f, true);
            east.transform.position = new Vector3(3f, 0f, 0f);

            MapLabElevationBaker.Report report = MapLabElevationBaker.Scan(_map);
            CollectionAssert.AreEqual(new[] { "West and East" }, report.Overlaps);
            Assert.AreEqual(2, Level(Compute(), 2.25f, 0.25f), "the higher one wins");
        }

        [Test]
        public void Changes_NameWalkOnTopAndStepHeight()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, false);
            MapLabWalkableChanges.Snapshot before = MapLabWalkableChanges.Take(_map);
            SetWalkOnTop(plateau, true);
            Assert.AreEqual("Plateau (walk on top) added", MapLabWalkableChanges.Describe(before, MapLabWalkableChanges.Take(_map)));

            MapLabWalkableChanges.Snapshot walkable = MapLabWalkableChanges.Take(_map);
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_stepHeight").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual("step height changed", MapLabWalkableChanges.Describe(walkable, MapLabWalkableChanges.Take(_map)));
        }

        [Test]
        public void Stamp_FollowsWalkOnTopAndIgnoresStepHeightWithoutIt()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, false);
            string flat = MapLabBakeStatus.WalkableStamp(_map);
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_stepHeight").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(flat, MapLabBakeStatus.WalkableStamp(_map), "step height alone does not make old bakes out of date");

            SetWalkOnTop(plateau, true);
            Assert.AreNotEqual(flat, MapLabBakeStatus.WalkableStamp(_map));
            SetWalkOnTop(plateau, false);
            Assert.AreEqual(flat, MapLabBakeStatus.WalkableStamp(_map));
        }

        [Test]
        public void Ramp_LinksTheGroundToThePlateau()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            Ramp(plateau, new Vector2(0f, -3.2f), 2f, 2f);
            MapLabElevationBaker.Report report = MapLabElevationBaker.Scan(_map);
            WalkableAreaJson walkable = MapLabWalkableBaker.Compute(_map);
            WalkableElevationJson data = MapLabElevationBaker.Compute(_map, walkable);

            Assert.IsEmpty(report.Islands, "the ramp leads up to the plateau");
            Assert.AreEqual(1, data.ramps.Length);
            ElevationRamp ramp = data.ramps[0];
            Assert.AreEqual(2, ramp.id);
            Assert.AreEqual(0, ramp.lower);
            Assert.AreEqual(1, ramp.upper);
            Assert.AreEqual(Height, ramp.highHeight, 0.0001f);
            Assert.AreEqual(Height * 0.5f, MapLabElevationBaker.RampHeight(ramp, new Vector2(0f, -4.2f)), 0.001f);

            Assert.AreEqual(ramp.id, Level(data, 0.25f, -4.25f), "middle of the ramp");
            Assert.AreEqual(ramp.id, Level(data, 0.25f, -3.25f), "top row: the plateau's own footprint does not block its ramp");
            Assert.AreEqual(1, Level(data, 0.25f, -2.75f), "the plateau above the ramp");
            Assert.AreEqual(0, Level(data, 0.25f, -5.75f), "the ground below the ramp");
            Assert.AreEqual(0, Level(data, 1.75f, -4.25f), "the ground beside the ramp");
            Assert.AreEqual(1, data.cliffs[0].openings.Length);
            Assert.AreEqual(ramp.id, data.cliffs[0].openings[0].ramp);

            bool[] cells = MapLabWalkableBaker.DecodeCells(walkable.cells, walkable.CellCount);
            Assert.IsFalse(cells[(Mathf.FloorToInt((-4.25f + 6f) / 0.5f)) * walkable.columns + Mathf.FloorToInt((0.25f + 6f) / 0.5f)], "Walkable.json still blocks the ramp");
        }

        [Test]
        public void RampOnATopYouCannotWalkOn_IsReported()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, false);
            Ramp(plateau, new Vector2(0f, -3.2f), 2f, 2f);
            MapLabElevationBaker.Report report = MapLabElevationBaker.Scan(_map);

            CollectionAssert.AreEqual(new[] { "Plateau/Ramp" }, report.DeadRamps);
            Assert.IsEmpty(report.Ramps);
        }

        [Test]
        public void RampOnATier_ClimbsFromThePlateauToTheTier()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            Ramp(tier, new Vector2(0f, -0.21f), 1f, 1.5f);
            MapLabElevationBaker.Report onlyTier = MapLabElevationBaker.Scan(_map);
            CollectionAssert.AreEqual(new[] { "Plateau", $"Plateau/{tier.name}" }, onlyTier.Islands, "nothing leads up to the plateau, so the tier is cut off too");

            Ramp(plateau, new Vector2(-3f, -3.2f), 2f, 2f);
            WalkableElevationJson data = Compute();
            Assert.IsEmpty(MapLabElevationBaker.Scan(_map).Islands);
            ElevationRamp upper = data.ramps.Single(ramp => ramp.upper == 2);
            Assert.AreEqual(1, upper.lower);
            Assert.AreEqual(Height, upper.lowHeight, 0.0001f);
            Assert.AreEqual(Height + TierHeight, upper.highHeight, 0.0001f);
        }

        [Test]
        public void Changes_NameAMovedRamp()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            RampShape ramp = Ramp(plateau, new Vector2(0f, -3.2f), 2f, 2f);
            MapLabWalkableChanges.Snapshot before = MapLabWalkableChanges.Take(_map);
            ramp.Configure(ramp.Position, 3f, ramp.Length, ramp.Angle);

            StringAssert.Contains("Ramp moved or changed", MapLabWalkableChanges.Describe(before, MapLabWalkableChanges.Take(_map)));
        }

        [Test]
        public void Files_AreReadableWithoutMapLabTypes()
        {
            ExtrudedShape plateau = Plateau("Plateau", Height, true);
            ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            WalkableElevationJson data = Compute();
            string json = $"{Folder}/Walkable_Elevation.json";
            string mask = $"{Folder}/Walkable_Elevation.png";
            MapLabElevationBaker.WriteFiles(data, json, mask);

            var reader = JsonUtility.FromJson<Reader>(AssetDatabase.LoadAssetAtPath<TextAsset>(json).text);
            Assert.AreEqual(1, reader.version);
            Assert.AreEqual(3, reader.levels.Length);
            Assert.AreEqual(Height + TierHeight, reader.levels[2].height, 0.0001f);
            Assert.AreEqual(1, reader.levels[2].below);
            Assert.AreEqual(0.25f, reader.stepHeight, 0.0001f);
            byte[] cells = Convert.FromBase64String(reader.cellLevel);
            Assert.AreEqual(reader.columns * reader.rows, cells.Length);
            Assert.AreEqual(2, cells[Index(reader.cellSize, reader.columns, 0.25f, 1.25f)]);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(mask);
            Assert.AreEqual(reader.columns, texture.width);
            Assert.AreEqual(reader.rows, texture.height);
        }
        #endregion

        #region [Private Methods]
        private ExtrudedShape Plateau(string name, float height, bool walkOnTop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_map.transform, false);
            var shape = go.AddComponent<ExtrudedShape>();
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_type").enumValueIndex = (int)ExtrudedShapeType.Plateau;
            serialized.FindProperty("_height").floatValue = height;
            serialized.FindProperty("_sortingOrder").intValue = ExtrudedShapeMenu.CharacterOrder;
            serialized.FindProperty("_walkOnTop").boolValue = walkOnTop;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.SetPoints(Rectangle, Corners);
            return shape;
        }

        private static RampShape Ramp(ExtrudedShape parent, Vector2 onEdge, float width, float length)
        {
            RampShape ramp = ExtrudedShapeMenu.AddRamp(parent, RampFormula.Project(parent.Footprint(), parent.transform.InverseTransformPoint(onEdge)));
            ramp.Configure(ramp.Position, width, length, 0f);
            return ramp;
        }

        private static void Box(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>();
        }

        private static void SetWalkOnTop(ExtrudedShape shape, bool walkOnTop)
        {
            Configure(shape, serialized => serialized.FindProperty("_walkOnTop").boolValue = walkOnTop);
        }

        private static void Configure(ExtrudedShape shape, Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(shape);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private WalkableElevationJson Compute()
        {
            WalkableElevationJson data = MapLabElevationBaker.Compute(_map, MapLabWalkableBaker.Compute(_map));
            Assert.IsNotNull(data);
            return data;
        }

        private static int Level(WalkableElevationJson data, float x, float y)
        {
            int column = Mathf.FloorToInt((x - data.bounds.x) / data.cellSize);
            int row = Mathf.FloorToInt((y - data.bounds.y) / data.cellSize);
            return MapLabElevationBaker.DecodeLevels(data.cellLevel, data.CellCount)[row * data.columns + column];
        }

        private static int Index(float cellSize, int columns, float x, float y)
        {
            int column = Mathf.FloorToInt((x + 6f) / cellSize);
            int row = Mathf.FloorToInt((y + 6f) / cellSize);
            return row * columns + column;
        }

        private static void AssertLevel(ElevationLevel level, int id, string name, float height, float sortBias, int below)
        {
            Assert.AreEqual(id, level.id);
            Assert.AreEqual(name, level.name);
            Assert.AreEqual(height, level.height, 0.0001f);
            Assert.AreEqual(sortBias, level.sortBias, 0.0001f);
            Assert.AreEqual(below, level.below);
        }
        #endregion
    }
}
