using System.Collections.Generic;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Tests
{
    public class StaticBakeTests
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
        public void StaticGround_OnlyWhatIsDrawnBelowTheActors()
        {
            MapLabMap map = CreateMap();
            SpriteRenderer grass = Sprite(map.transform, "Grass", -100);
            SpriteRenderer propShadow = Sprite(map.transform, "Prop Shadow", 1);
            Sprite(map.transform, "Prop", 6);
            var grouped = new GameObject("Character");
            grouped.transform.SetParent(map.transform, false);
            grouped.AddComponent<SortingGroup>().sortingOrder = 6;
            Sprite(grouped.transform, "Character Feet", -100);
            var animated = new GameObject("Animated Water");
            animated.transform.SetParent(map.transform, false);
            animated.AddComponent<Animator>();
            Sprite(animated.transform, "Water", -100);
            var mist = new GameObject("Mist");
            mist.transform.SetParent(map.transform, false);
            mist.AddComponent<ParticleSystem>();
            ExtrudedShape plateau = Shape(map.transform, "Plateau");

            List<Renderer> ground = MapLabStaticBaker.CollectGround(map);

            Renderer plateauShadow = plateau.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>();
            CollectionAssert.AreEquivalent(new Renderer[] { grass, propShadow, plateauShadow }, ground,
                "ground, prop shadows and shape shadows bake; actors, sorting groups at the actor order, animated objects and particles stay live");
        }

        [Test]
        public void BakedSources_StayHiddenUntilUnbake()
        {
            MapLabMap map = CreateMap();
            SpriteRenderer grass = Sprite(map.transform, "Grass", -100);
            ExtrudedShape plateau = Shape(map.transform, "Plateau");
            ExtrudedShape plaza = Shape(map.transform, "Plaza");
            var serialized = new SerializedObject(plaza);
            serialized.FindProperty("_height").floatValue = 0f;
            serialized.FindProperty("_ySort").boolValue = false;
            serialized.FindProperty("_sortingOrder").intValue = -90;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            plaza.Rebuild();
            var road = new GameObject("Road");
            road.transform.SetParent(map.transform, false);
            var path = road.AddComponent<PathShape>();
            path.SetPoints(new[] { new Vector2(-3f, 0f), new Vector2(3f, 0f) });

            var layer = new GameObject(MapLabLayerBaker.LayerPrefix + "Ground");
            layer.transform.SetParent(MapLabLayerBaker.Root(map, true), false);
            layer.AddComponent<MapLabChunkLayer>().Setup("Ground", null, 16f, 64, new List<Vector2Int>(), new List<SpriteRenderer>());
            MapLabStaticBaker.HideSources(layer.AddComponent<MapLabBakedLayer>(), MapLabStaticBaker.CollectGround(map));
            plateau.Rebuild();
            plaza.Rebuild();
            path.Rebuild();

            Assert.IsFalse(grass.enabled, "plain renderers are switched off");
            Assert.IsTrue(plateau.ShadowBaked);
            Assert.IsFalse(plateau.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>().enabled, "a rebuild keeps the baked shadow hidden");
            Assert.IsTrue(AllEnabled(plateau.transform.Find(ExtrudedShape.VisualName)), "the y-sorted plateau stays live");
            Assert.IsTrue(plaza.VisualBaked);
            Assert.IsFalse(AllEnabled(plaza.transform.Find(ExtrudedShape.VisualName)));
            Assert.IsTrue(path.VisualBaked);
            Assert.IsFalse(road.transform.Find(PathShape.StripName).GetComponent<Renderer>().enabled);

            MapLabStaticBaker.Unbake(map);

            Assert.IsTrue(grass.enabled);
            Assert.IsFalse(plateau.ShadowBaked || plaza.VisualBaked || path.VisualBaked);
            Assert.IsTrue(plateau.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>().enabled);
            Assert.IsTrue(AllEnabled(plaza.transform.Find(ExtrudedShape.VisualName)));
            Assert.IsTrue(road.transform.Find(PathShape.StripName).GetComponent<Renderer>().enabled);
            Assert.IsNull(map.GetComponentInChildren<MapLabBakedLayer>(true), "the baked layer is removed");
            Assert.IsNull(MapLabLayerBaker.Root(map, false), "an empty bake root is removed too");
        }

        [Test]
        public void Streamer_KeepsOnlyChunksAroundTheViewInEveryLayer()
        {
            var root = new GameObject("Bake Layers");
            _created.Add(root);
            var streamer = root.AddComponent<MapLabChunkStreamer>();
            var serialized = new SerializedObject(streamer);
            serialized.FindProperty("_margin").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var indices = new List<Vector2Int>();
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 3; x++) indices.Add(new Vector2Int(x, y));
            }
            List<SpriteRenderer> ground = Layer(root.transform, "Ground", null, indices, 5);
            List<SpriteRenderer> paint = Layer(root.transform, "Ground", "Ground_paint", new List<Vector2Int> { new Vector2Int(1, 1) }, -1);

            streamer.ShowArea(new Rect(4.5f, 4.5f, 1f, 1f));
            Assert.AreEqual(2, streamer.ActiveChunks, "chunk 1,1 of the bake and of its paint");
            Assert.IsTrue(ground[4].enabled && paint[0].enabled);
            Assert.IsFalse(ground[0].enabled);

            streamer.ShowArea(new Rect(3f, 3f, 6f, 2f));
            Assert.AreEqual(6, streamer.ActiveChunks, "five bake chunks (one slot is empty) and the paint chunk");

            streamer.ShowArea(new Rect(40f, 40f, 2f, 2f));
            Assert.AreEqual(0, streamer.ActiveChunks, "far from the map nothing is active");

            streamer.ShowAll();
            Assert.AreEqual(6, streamer.ActiveChunks);
        }

        [Test]
        public void ChunkGrid_IsAnchoredToTheWorld()
        {
            Assert.AreEqual(new RectInt(-1, -2, 2, 2), MapLabChunkLayer.ChunkRange(new Rect(-0.5f, -4.1f, 1f, 0.2f), 4f), "negative chunks floor down");
            Assert.AreEqual(new RectInt(0, 0, 1, 1), MapLabChunkLayer.ChunkRange(new Rect(0f, 0f, 4f, 4f), 4f), "an edge on the grid line does not add a chunk");
            Assert.AreEqual(new Rect(-8f, 4f, 4f, 4f), MapLabChunkLayer.ChunkRect(new Vector2Int(-2, 1), 4f));
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

        private static List<SpriteRenderer> Layer(Transform root, string layer, string paint, List<Vector2Int> indices, int emptySlot)
        {
            var go = new GameObject(layer);
            go.transform.SetParent(root, false);
            var chunks = new List<SpriteRenderer>();
            for (int i = 0; i < indices.Count; i++)
            {
                var chunk = new GameObject($"Chunk {i}");
                chunk.transform.SetParent(go.transform, false);
                chunks.Add(i == emptySlot ? null : chunk.AddComponent<SpriteRenderer>());
            }
            go.AddComponent<MapLabChunkLayer>().Setup(layer, paint, 4f, 64, indices, chunks);
            return chunks;
        }

        private static ExtrudedShape Shape(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<ExtrudedShape>();
        }

        private static bool AllEnabled(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) return false;
            }
            return true;
        }
        #endregion
    }
}
