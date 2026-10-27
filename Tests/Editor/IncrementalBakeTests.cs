using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class IncrementalBakeTests
    {
        #region [Variables/Fields]
        private const float ChunkSize = 16f;
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
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void Coverage_FollowsTheTrianglesNotTheBounds()
        {
            var go = new GameObject("Road");
            _created.Add(go);
            var path = go.AddComponent<PathShape>();
            path.SetPoints(new[] { new Vector2(1f, 1f), new Vector2(63f, 63f) });
            Renderer strip = go.transform.Find(PathShape.StripName).GetComponent<Renderer>();

            HashSet<Vector2Int> covered = MapLabChunkCoverage.Of(strip, ChunkSize);
            Assert.IsNotEmpty(covered);
            Assert.Less(covered.Count, 16, "a diagonal road touches a band of chunks, not the whole 4 x 4 box around it");
            Assert.IsTrue(covered.Contains(new Vector2Int(0, 0)) && covered.Contains(new Vector2Int(3, 3)));
            Assert.IsFalse(covered.Contains(new Vector2Int(3, 0)) || covered.Contains(new Vector2Int(0, 3)), "corners away from the road are left out");
        }

        [Test]
        public void Coverage_IgnoresTheStretchedSortBoundsOfShapes()
        {
            var go = new GameObject("Plateau");
            _created.Add(go);
            go.transform.position = new Vector3(40f, 40f, 0f);
            var plateau = go.AddComponent<ExtrudedShape>();
            plateau.Rebuild();
            Renderer shadow = go.transform.Find(ExtrudedShape.ShadowName).GetComponent<Renderer>();
            Mesh mesh = shadow.GetComponent<MeshFilter>().sharedMesh;
            List<Vector3> world = mesh.vertices.Select(vertex => shadow.transform.TransformPoint(vertex)).ToList();
            var tight = Rect.MinMaxRect(world.Min(v => v.x), world.Min(v => v.y), world.Max(v => v.x), world.Max(v => v.y));
            RectInt range = MapLabChunkLayer.ChunkRange(tight, ChunkSize);

            foreach (var index in MapLabChunkCoverage.Of(shadow, ChunkSize)) Assert.IsTrue(range.Contains(index), $"chunk {index} is outside the shadow itself");
        }

        [Test]
        public void Signatures_ChangeOnlyWhereSomethingChanged()
        {
            SpriteRenderer near = Sprite("Near", new Vector2(4f, 4f));
            SpriteRenderer far = Sprite("Far", new Vector2(36f, 4f));
            var renderers = new List<Renderer> { near, far };
            string settings = MapLabChunkSignatures.Settings(ChunkSize, 64);
            Dictionary<Vector2Int, Hash128> before = MapLabChunkSignatures.Compute(renderers, MapLabChunkCoverage.Map(renderers, ChunkSize), settings);

            far.transform.position += new Vector3(1f, 0f, 0f);
            Dictionary<Vector2Int, Hash128> after = MapLabChunkSignatures.Compute(renderers, MapLabChunkCoverage.Map(renderers, ChunkSize), settings);
            Assert.AreEqual(before[new Vector2Int(0, 0)], after[new Vector2Int(0, 0)], "the chunk of the sprite that did not move keeps its signature");
            Assert.AreNotEqual(before[new Vector2Int(2, 0)], after[new Vector2Int(2, 0)], "the chunk of the moved sprite changes");

            far.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            Dictionary<Vector2Int, Hash128> tinted = MapLabChunkSignatures.Compute(renderers, MapLabChunkCoverage.Map(renderers, ChunkSize), settings);
            Assert.AreNotEqual(after[new Vector2Int(2, 0)], tinted[new Vector2Int(2, 0)], "a new colour counts as a change");

            Dictionary<Vector2Int, Hash128> finer = MapLabChunkSignatures.Compute(renderers, MapLabChunkCoverage.Map(renderers, ChunkSize), MapLabChunkSignatures.Settings(ChunkSize, 96));
            Assert.AreNotEqual(tinted[new Vector2Int(0, 0)], finer[new Vector2Int(0, 0)], "another px/unit redraws everything");
        }

        [Test]
        public void Signatures_RoundTripAndRejectOtherSettings()
        {
            string folder = FileUtil.GetUniqueTempPathInProject();
            try
            {
                string settings = MapLabChunkSignatures.Settings(ChunkSize, 64);
                var chunks = new Dictionary<Vector2Int, MapLabChunkSignatures.Entry>
                {
                    [new Vector2Int(-2, 3)] = new MapLabChunkSignatures.Entry(Hash128.Compute("a"), false),
                    [new Vector2Int(5, 0)] = new MapLabChunkSignatures.Entry(Hash128.Compute("b"), true)
                };
                MapLabChunkSignatures.Save(folder, settings, chunks);

                Dictionary<Vector2Int, MapLabChunkSignatures.Entry> loaded = MapLabChunkSignatures.Load(folder, settings);
                Assert.AreEqual(2, loaded.Count);
                Assert.AreEqual(Hash128.Compute("a"), loaded[new Vector2Int(-2, 3)].Hash);
                Assert.IsTrue(loaded[new Vector2Int(5, 0)].Empty, "an empty chunk is remembered so it is not drawn again");
                Assert.IsNull(MapLabChunkSignatures.Load(folder, MapLabChunkSignatures.Settings(8f, 64)), "a new chunk size starts over");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
        #endregion

        #region [Private Methods]
        private SpriteRenderer Sprite(string name, Vector2 position)
        {
            var texture = new Texture2D(4, 4);
            _created.Add(texture);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 2f);
            _created.Add(sprite);
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }
        #endregion
    }
}
