using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class WorldFloorTests
    {
        #region [Variables/Fields]
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
        public void Floor_BuildsOneMeshPerTileAtTheMegaChunkOrigin()
        {
            MapLabMap map = CreateWorldMap(out MapLabWorldData world);
            world.Add(new Vector2Int(-1, 2), 1);
            MapLabFloor floor = MapLabWorldEdits.EnsureFloor(map);
            floor.Rebuild();

            Transform mega = floor.transform.Find(MapLabFloor.MegaName(new Vector2Int(-1, 2)));
            Assert.IsNotNull(mega, "one object per mega chunk");
            Assert.AreEqual(new Vector3(-64f, 128f, 0f), mega.position, "mega chunks sit on the world grid");
            Assert.AreEqual(1, mega.childCount);
            Mesh mesh = mega.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.vertexCount, "an unpainted mega chunk is one quad");
            Assert.AreEqual(new Vector3(64f, 64f, 0f), mesh.bounds.size);

            MapLabWorldEdits.SetTiles(map, new[] { new Vector2Int(-60, 130), new Vector2Int(-59, 130) }, 3, "Paint");
            Assert.AreEqual(2, mega.childCount, "a second tile adds a second mesh");
            Assert.AreEqual(MapLabWorldEdits.DefaultFloorOrder, mega.GetComponentsInChildren<MeshRenderer>().First().sortingOrder, "the floor draws under every ground sprite");

            floor.SetBaked(true);
            Assert.IsTrue(mega.GetComponentsInChildren<MeshRenderer>(true).All(renderer => !renderer.enabled), "a baked floor hides its live meshes");
            floor.SetBaked(false);

            MapLabWorldEdits.RemoveMegas(map, new[] { new Vector2Int(-1, 2) });
            Assert.IsNull(floor.transform.Find(MapLabFloor.MegaName(new Vector2Int(-1, 2))), "removing a mega chunk removes its floor");
        }

        [Test]
        public void Edits_PaintAcrossMegaChunksAndOnlyInsideTheMap()
        {
            MapLabMap map = CreateWorldMap(out MapLabWorldData world);
            MapLabWorldEdits.AddMegas(map, new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }, 1);
            Assert.AreEqual(1f, MapLabWorldEdits.TileSize(map), 0.0001f, "64 tiles over a 64 unit mega chunk");

            int changed = MapLabWorldEdits.Paint(map, new Vector2(64f, 10.5f), 3, 2, "Paint");
            Assert.AreEqual(9, changed);
            Assert.AreEqual(2, world.TileAt(new Vector2Int(0, 0), 63, 10), "the brush reaches the left mega chunk");
            Assert.AreEqual(2, world.TileAt(new Vector2Int(1, 0), 1, 11), "and the right one");
            Assert.AreEqual(2, MapLabWorldEdits.Pick(map, new Vector2(63.5f, 10.5f)));

            Assert.AreEqual(0, MapLabWorldEdits.Paint(map, new Vector2(10f, -20f), 3, 2, "Paint"), "nothing is painted outside the mega chunks");

            int filled = MapLabWorldEdits.FillChunk(map, new Vector2(20f, 20f), 4);
            Assert.AreEqual(256, filled, "a chunk of 16 units holds 16 x 16 tiles");
            Assert.AreEqual(4, world.TileAt(new Vector2Int(0, 0), 16, 16));
            Assert.AreEqual(4, world.TileAt(new Vector2Int(0, 0), 31, 31));
            Assert.AreEqual(1, world.TileAt(new Vector2Int(0, 0), 32, 16));

            MapLabWorldEdits.FillMegas(map, new[] { new Vector2Int(0, 0) }, 5);
            MapLabMegaChunk left = world.Find(new Vector2Int(0, 0));
            Assert.AreEqual(5, left.BaseTile);
            Assert.IsFalse(left.IsPainted, "filling a mega chunk replaces its paint");
            Assert.IsTrue(world.Find(new Vector2Int(1, 0)).IsPainted, "the neighbour keeps its paint");

            int version = world.Find(new Vector2Int(1, 0)).Version;
            Assert.AreEqual(1, MapLabWorldEdits.Paint(map, new Vector2(64.5f, 10.5f), 1, 0, "Erase"));
            Assert.AreEqual(1, world.TileAt(new Vector2Int(1, 0), 0, 10), "erased tiles show the base tile again");
            Assert.Greater(world.Find(new Vector2Int(1, 0)).Version, version, "edits move the version so the bake goes out of date");
        }

        [Test]
        public void Claims_FloorIsItsOwnKind()
        {
            MapLabMap map = CreateWorldMap(out MapLabWorldData world);
            world.Add(Vector2Int.zero, 1);
            MapLabFloor floor = MapLabWorldEdits.EnsureFloor(map);
            floor.Rebuild();
            Renderer renderer = floor.GetComponentInChildren<MeshRenderer>();
            Assert.AreEqual(MapLabBakeKinds.Floor, MapLabLayerClaims.KindOf(renderer, map));

            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites | MapLabBakeKinds.Paths));
            Assert.IsFalse(MapLabLayerClaims.Claimed(map, "Ground").Contains(renderer), "a layer without Floor tiles leaves the floor live");
            Assert.IsNotNull(MapLabWorldEdits.EnsureFloorLayer(map));
            Assert.IsTrue((map.BakeLayers[0].Kinds & MapLabBakeKinds.Floor) != 0, "creating the floor makes the lowest ground layer take it");
            Assert.IsTrue(MapLabLayerClaims.Claimed(map, "Ground").Contains(renderer));
            Assert.IsNull(MapLabWorldEdits.EnsureFloorLayer(map), "only once");
        }
        #endregion

        #region [Private Methods]
        private MapLabMap CreateWorldMap(out MapLabWorldData world)
        {
            var root = new GameObject("Map");
            _created.Add(root);
            MapLabMap map = root.AddComponent<MapLabMap>();
            world = ScriptableObject.CreateInstance<MapLabWorldData>();
            _created.Add(world);
            var palette = ScriptableObject.CreateInstance<MapLabFloorPalette>();
            _created.Add(palette);
            foreach (var (name, color) in MapLabWorldEdits.DefaultTiles) palette.Tiles.Add(new MapLabFloorTile(name, color));
            world.Palette = palette;
            map.World = world;
            return map;
        }
        #endregion
    }
}
