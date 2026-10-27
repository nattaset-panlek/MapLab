using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace MapLab.Tests
{
    public class PresetTests
    {
        #region [Variables/Fields]
        private const string Folder = "Assets/MapLabPresetTests";
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
            AssetDatabase.DeleteAsset(Folder);
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void Kind_ComesFromTheShape()
        {
            Assert.AreEqual(MapLabPresetKind.Plateau, MapLabPresets.KindOf(Shape(ExtrudedShapeType.Plateau, 1.5f, false)));
            Assert.AreEqual(MapLabPresetKind.Box, MapLabPresets.KindOf(Shape(ExtrudedShapeType.Plateau, 2f, true)));
            Assert.AreEqual(MapLabPresetKind.Flat, MapLabPresets.KindOf(Shape(ExtrudedShapeType.Plateau, 0f, false)));
            Assert.AreEqual(MapLabPresetKind.Ring, MapLabPresets.KindOf(Shape(ExtrudedShapeType.Ring, 1.5f, false)));
            Assert.AreEqual(MapLabPresetKind.Void, MapLabPresets.KindOf(Shape(ExtrudedShapeType.Void, 1.5f, false)));
            Assert.AreEqual(MapLabPresetKind.Environment, MapLabPresets.KindOf(Map("Map")));
        }

        [Test]
        public void Preset_CopiesSettingsButNeverTheFootprint()
        {
            ExtrudedShape source = Shape(ExtrudedShapeType.Plateau, 2.5f, false);
            SetFloat(source, "_sortSlice", 0.8f);
            MapLabPreset preset = MapLabPresets.Create(source, "Cliff Granite", Folder);
            Assert.IsTrue(AssetDatabase.Contains(preset));
            Assert.IsInstanceOf<Preset>(preset.Values, "the values live inside the preset asset");
            Assert.AreEqual(AssetDatabase.GetAssetPath(preset), AssetDatabase.GetAssetPath(preset.Values));
            Assert.AreEqual(MapLabPresetKind.Plateau, preset.Kind);
            Assert.AreSame(preset, source.Preset, "the source becomes a user");

            ExtrudedShape target = Shape(ExtrudedShapeType.Plateau, 0.5f, true);
            List<Vector2> footprint = target.Points.ToList();
            Assert.IsTrue(MapLabPresets.Apply(preset, target));
            Assert.AreEqual(2.5f, target.Height, 0.0001f);
            Assert.AreEqual(0.8f, GetFloat(target, "_sortSlice"), 0.0001f);
            CollectionAssert.AreEqual(footprint, target.Points.ToList(), "points stay");
            Assert.IsTrue(target.IsSharp(0), "sharp corners stay");
            Assert.AreSame(preset, target.Preset);
            Assert.IsFalse(MapLabPresets.Apply(preset, Path()), "a plateau preset does not fit a path");
            CollectionAssert.AreEquivalent(new Component[] { source, target }, MapLabPresets.Users(preset));
        }

        [Test]
        public void Preset_KeepsUsersInSync()
        {
            ExtrudedShape source = Shape(ExtrudedShapeType.Ring, 1.5f, false);
            MapLabPreset preset = MapLabPresets.Create(source, "Ruin Wall", Folder);
            ExtrudedShape other = Shape(ExtrudedShapeType.Ring, 1f, false);
            MapLabPresets.Apply(preset, other);

            Component template = MapLabPresets.Template(preset);
            try
            {
                SetFloat(template, "_height", 3f);
                MapLabPresets.Store(preset, template);
            }
            finally
            {
                MapLabPresets.Release(template);
            }
            preset.SyncUsers = false;
            Assert.AreEqual(0, MapLabPresets.Sync(preset));
            Assert.AreEqual(1.5f, other.Height, 0.0001f, "no sync: users keep their values");
            preset.SyncUsers = true;
            Assert.AreEqual(2, MapLabPresets.Sync(preset));
            Assert.AreEqual(3f, other.Height, 0.0001f);
            Assert.AreEqual(3f, source.Height, 0.0001f);
        }

        [Test]
        public void Environment_AddsLayersAndKeepsTheMapArea()
        {
            MapLabMap source = Map("Highlands");
            source.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.All));
            source.BakeLayers.Add(new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true });
            SetFloat(source, "_cellSize", 0.25f);
            MapLabPreset preset = MapLabPresets.Create(source, "Highlands", Folder);
            Assert.AreEqual(2, preset.Layers.Count);

            MapLabMap target = Map("New Map");
            target.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites));
            Rect bounds = target.Bounds;
            Assert.IsTrue(MapLabPresets.Apply(preset, target));
            Assert.AreEqual(0.25f, target.CellSize, 0.0001f);
            Assert.AreEqual(bounds, target.Bounds, "the walkable area of the target stays");
            Assert.AreEqual(2, target.BakeLayers.Count, "missing layers are added");
            Assert.AreEqual(MapLabBakeKinds.All, target.BakeLayers[0].Kinds, "a layer with the same name takes the kinds");
            Assert.IsTrue(target.BakeLayers[1].Sorted);
        }
        #endregion

        #region [Private Methods]
        private ExtrudedShape Shape(ExtrudedShapeType type, float height, bool box)
        {
            var points = box
                ? new List<Vector2> { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) }
                : new List<Vector2> { new Vector2(-2f, -1f), new Vector2(2f, -1f), new Vector2(2.5f, 1f), new Vector2(0f, 2f), new Vector2(-2.5f, 1f) };
            ExtrudedShape shape = ExtrudedShapeMenu.Create(type, $"{type} {height}", null, Vector3.zero, points, height, 0.4f, 6, box ? new[] { true, true, true, true } : null);
            _created.Add(shape.gameObject);
            return shape;
        }

        private PathShape Path()
        {
            PathShape path = ExtrudedShapeMenu.CreatePath("Road", null, Vector3.zero, new[] { new Vector2(-2f, 0f), new Vector2(2f, 0f) }, 1f, -92);
            _created.Add(path.gameObject);
            return path;
        }

        private MapLabMap Map(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<MapLabMap>();
        }

        private static void SetFloat(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static float GetFloat(Object target, string property)
        {
            return new SerializedObject(target).FindProperty(property).floatValue;
        }
        #endregion
    }
}
