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
    public class ElevationApiTests
    {
        #region [Variables/Fields]
        private const float Height = 1.5f;
        private const int RampId = 2;

        private static readonly Vector2[] Rectangle =
        {
            new Vector2(-4.2f, -3.2f), new Vector2(4.2f, -3.2f), new Vector2(4.2f, 3.2f), new Vector2(-4.2f, 3.2f)
        };

        private static readonly bool[] Corners = { true, true, true, true };

        private readonly List<Object> _created = new List<Object>();
        private MapLabMap _map;
        private ElevationMap _elevation;
        #endregion

        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            var root = new GameObject("ApiMap");
            _created.Add(root);
            _map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(_map);
            serialized.FindProperty("_bounds").rectValue = new Rect(-6f, -6f, 12f, 12f);
            serialized.FindProperty("_cellSize").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var go = new GameObject("Plateau");
            go.transform.SetParent(root.transform, false);
            var plateau = go.AddComponent<ExtrudedShape>();
            var shape = new SerializedObject(plateau);
            shape.FindProperty("_height").floatValue = Height;
            shape.FindProperty("_walkOnTop").boolValue = true;
            shape.ApplyModifiedPropertiesWithoutUndo();
            plateau.SetPoints(Rectangle, Corners);

            RampShape ramp = ExtrudedShapeMenu.AddRamp(plateau, RampFormula.Project(plateau.Footprint(), new Vector2(0f, -3.2f)));
            ramp.Configure(ramp.Position, 2f, 2f, 0f);
            _elevation = new ElevationMap(MapLabElevationBaker.Compute(_map, MapLabWalkableBaker.Compute(_map)));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
            _created.Clear();
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void Map_ReadsRegionsHeightsAndSortBias()
        {
            Assert.AreEqual(0, _elevation.RegionAt(new Vector2(-5.25f, -5.25f)), "ground");
            Assert.AreEqual(1, _elevation.RegionAt(new Vector2(-3.25f, -2.25f)), "plateau");
            Assert.AreEqual(RampId, _elevation.RegionAt(new Vector2(0.25f, -4.25f)), "ramp");
            Assert.IsTrue(_elevation.IsRamp(RampId));
            Assert.AreEqual(ElevationMap.Outside, _elevation.RegionAt(new Vector2(20f, 0f)));

            Assert.AreEqual(Height, _elevation.HeightAt(new Vector2(-3.25f, -2.25f)), 0.0001f);
            Assert.AreEqual(Height * (1f - 1.05f / 2f), _elevation.HeightAt(new Vector2(0.25f, -4.25f)), 0.001f);
            Assert.AreEqual(0f, _elevation.SortBias(0), 0.0001f);
            Assert.AreEqual(0.51f, _elevation.SortBias(1), 0.0001f);
            Assert.AreEqual(0.51f, _elevation.SortBias(RampId), 0.0001f);
        }

        [Test]
        public void CanStep_UpTheRampButNotTheCliffOrTheSides()
        {
            Assert.IsFalse(_elevation.CanStep(new Vector2(-3.25f, -3.75f), new Vector2(-3.25f, -2.75f)), "ground straight onto the plateau");
            Assert.IsTrue(_elevation.CanStep(new Vector2(0.25f, -5.75f), new Vector2(0.25f, -5f)), "onto the ramp from below");
            Assert.IsFalse(_elevation.CanStep(new Vector2(0.75f, -4.25f), new Vector2(1.75f, -4.25f)), "off the side of the ramp");
            Assert.IsTrue(_elevation.CanStep(new Vector2(0.25f, -3.4f), new Vector2(0.25f, -2.75f)), "from the top of the ramp onto the plateau");
            Assert.IsTrue(_elevation.CanStep(new Vector2(0.25f, -2.75f), new Vector2(0.25f, -3.4f)), "from the plateau down the ramp");
        }

        [Test]
        public void Constrain_SlidesAlongTheCliff()
        {
            Vector2 end = _elevation.Constrain(new Vector2(-3.25f, -4.75f), new Vector2(-2.25f, -1.75f));

            Assert.AreNotEqual(1, _elevation.RegionAt(end), "never climbs the cliff");
            Assert.AreEqual(-2.25f, end.x, 0.01f, "keeps the sideways part of the move");
        }

        [Test]
        public void FindPath_ClimbsByTheRamp()
        {
            var path = new List<Vector2>();
            Vector2 goal = new Vector2(-2.25f, 1.75f);
            Assert.IsTrue(ElevationPathFormula.FindPath(_elevation, new Vector2(-5.25f, -5.25f), goal, path));
            Assert.AreEqual(goal, path.Last());

            bool usedRamp = false;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Assert.IsTrue(_elevation.CanWalkStraight(path[i], path[i + 1]), $"segment {i}");
                for (float t = 0f; t <= 1f; t += 0.05f) usedRamp |= _elevation.RegionAt(Vector2.Lerp(path[i], path[i + 1], t)) == RampId;
            }
            Assert.IsTrue(usedRamp);
        }

        [Test]
        public void ScreenToGround_PicksTheSurfaceInFront()
        {
            AssertPick(new Vector2(-2f, 1f + Height), new Vector2(-2f, 1f), 1, "plateau top");
            AssertPick(new Vector2(-5f, -5f), new Vector2(-5f, -5f), 0, "open ground");
            AssertPick(new Vector2(0f, 4f), new Vector2(0f, 4f - Height), 1, "the plateau top in front of the ground behind it");
            var onRamp = new Vector2(0.25f, -4.25f);
            AssertPick(onRamp + Vector2.up * _elevation.HeightAt(onRamp), onRamp, RampId, "ramp");
            Assert.IsFalse(_elevation.ScreenToGround(new Vector2(-2f, -3.2f + Height * 0.5f), out _, out _), "a cliff wall has nothing to stand on");
        }

        [Test]
        public void Body_ClimbsTheRampAndRaisesEvents()
        {
            ElevationBody body = CreateBody(out Transform anchor, out Transform visual);
            body.Teleport(new Vector2(0.25f, -5.75f));
            var events = new List<string>();
            body.OnRampEntered += ramp => events.Add($"enter {ramp}");
            body.OnRampExited += ramp => events.Add($"exit {ramp}");
            body.OnLevelChanged += (from, to) => events.Add($"level {from}>{to}");

            for (int i = 0; i < 40; i++) body.Move(new Vector2(0f, 0.1f));

            CollectionAssert.AreEqual(new[] { $"enter {RampId}", $"exit {RampId}", "level 0>1" }, events);
            Assert.AreEqual(1, body.Level);
            Assert.AreEqual(Height, body.Height, 0.0001f);
            Assert.AreEqual(-0.51f, anchor.localPosition.y, 0.0001f);
            Assert.AreEqual(Height + 0.51f, visual.localPosition.y, 0.0001f);
        }

        [Test]
        public void Body_StaysOnTopAtTheCliffInGridMode()
        {
            ElevationBody body = CreateBody(out _, out _);
            body.Teleport(new Vector2(-2.25f, -2.25f));
            body.Move(new Vector2(0f, -2f));
            Assert.AreEqual(1, body.Level, "grid mode stops at the cliff");
            Assert.GreaterOrEqual(body.Ground.y, -3.2f);

            body.Collision = ElevationCollision.None;
            body.Move(new Vector2(0f, -2f));
            Assert.AreEqual(0, body.Level, "without collision the body drops to the ground");
            Assert.AreEqual(0f, body.Height, 0.0001f);
        }

        [Test]
        public void Load_ReadsATextAssetOnceAndRejectsOtherVersions()
        {
            string json = JsonUtility.ToJson(MapLabElevationBaker.Compute(_map, MapLabWalkableBaker.Compute(_map)));
            var file = new TextAsset(json);
            _created.Add(file);
            Assert.AreSame(ElevationMap.Load(file), ElevationMap.Load(file));
            Assert.Throws<InvalidOperationException>(() => ElevationMap.Parse(json.Replace("\"version\":1", "\"version\":2")));
        }
        #endregion

        #region [Private Methods]
        private ElevationBody CreateBody(out Transform anchor, out Transform visual)
        {
            var root = new GameObject("Body");
            _created.Add(root);
            anchor = new GameObject(ElevationBody.SortAnchorName).transform;
            anchor.SetParent(root.transform, false);
            visual = new GameObject(ElevationBody.VisualName).transform;
            visual.SetParent(anchor, false);
            var body = root.AddComponent<ElevationBody>();
            body.SetParts(anchor, visual);
            body.SetMap(_elevation);
            return body;
        }

        private void AssertPick(Vector2 visual, Vector2 ground, int region, string label)
        {
            Assert.IsTrue(_elevation.ScreenToGround(visual, out Vector2 picked, out int pickedRegion), label);
            Assert.AreEqual(region, pickedRegion, label);
            Assert.Less(Vector2.Distance(ground, picked), 0.02f, label);
        }
        #endregion
    }
}
