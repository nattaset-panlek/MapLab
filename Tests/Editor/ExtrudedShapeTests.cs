using System.Collections.Generic;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Tests
{
    public class ExtrudedShapeTests
    {
        #region [Variables/Fields]
        private const float Height = 1.5f;
        private const float RimWidth = 1f;

        private static readonly Vector2[] Box =
        {
            new Vector2(-4f, -3f), new Vector2(0f, -3.2f), new Vector2(4f, -3f),
            new Vector2(4.2f, 3f), new Vector2(0f, 3.2f), new Vector2(-4.2f, 3f)
        };

        private static readonly Vector2[] Square =
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f)
        };

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
        public void Plateau_CollidesOnItsFootprintOnly()
        {
            Create(ExtrudedShapeType.Plateau, null);

            Assert.IsTrue(Blocked(Vector2.zero), "footprint");
            Assert.IsFalse(Blocked(new Vector2(0f, 3.4f)), "ground behind the plateau is walkable");
            Assert.IsFalse(Blocked(new Vector2(0f, -3.4f)), "ground in front of the wall is walkable");
        }

        [Test]
        public void Height_RaisesTheTopAndKeepsTheWallBase()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            FootprintRange(shape, out float minY, out float maxY);
            Bounds before = VisualBounds(shape);

            Configure(shape, serialized => serialized.FindProperty("_height").floatValue = Height * 2f);
            Bounds after = VisualBounds(shape);

            Assert.AreEqual(minY, before.min.y, 0.01f, "wall stands on the footprint");
            Assert.AreEqual(before.min.y, after.min.y, 0.01f, "the base does not move");
            Assert.AreEqual(before.max.y + Height, after.max.y, 0.01f, "the top rises by the extra height");
            Assert.AreEqual(maxY + Height * 2f, after.max.y, 0.01f);
        }

        [Test]
        public void YSort_SplitsIntoSortingGroupsOnTheFootprint()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            Configure(shape, serialized => serialized.FindProperty("_actorHeight").floatValue = Height * 2f);
            FootprintRange(shape, out float minY, out float maxY);
            Transform visual = shape.transform.Find(ExtrudedShape.VisualName);

            Assert.Greater(visual.childCount, 2);
            float lowest = float.MaxValue;
            foreach (Transform piece in visual)
            {
                var group = piece.GetComponent<SortingGroup>();
                Assert.IsNotNull(group, piece.name);
                Assert.AreEqual(ExtrudedShapeMenu.CharacterOrder, group.sortingOrder);
                Assert.AreEqual(group.sortingOrder, piece.GetComponent<MeshRenderer>().sortingOrder, "Unity compares the renderer's own order against renderers outside the group");
                Assert.GreaterOrEqual(piece.position.y, minY - 0.01f, "sort point lies on the footprint");
                Assert.LessOrEqual(piece.position.y, maxY + 0.01f, "sort point lies on the footprint");
                lowest = Mathf.Min(lowest, piece.position.y);
            }
            Assert.LessOrEqual(lowest, minY + 0.5f, "the front of the shape sorts near its base");
        }

        [Test]
        public void TallShape_DrawsItsTopAsOnePieceAndSlicesOnlyTheWalls()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            FootprintRange(shape, out float minY, out _);
            Assert.IsTrue(shape.MergesTop, "1.5 high is taller than the default actor height");

            List<Transform> tops = TopPieces(shape);
            Assert.AreEqual(1, tops.Count, "one top piece");
            Assert.AreEqual(minY, tops[0].position.y, 0.01f, "the top sorts at the front of the footprint");
            foreach (Transform piece in shape.transform.Find(ExtrudedShape.VisualName))
            {
                if (piece == tops[0]) continue;
                Assert.AreEqual(0, piece.GetComponent<MeshFilter>().sharedMesh.GetTriangles(ShapeMeshData.TopSubmesh).Length, piece.name);
                Assert.Greater(piece.position.y, tops[0].position.y, "every wall band draws behind the top");
            }

            Configure(shape, serialized => serialized.FindProperty("_actorHeight").floatValue = Height + 0.1f);
            Assert.IsFalse(shape.MergesTop);
            Assert.Greater(TopPieces(shape).Count, 1, "a top an actor can stand in front of stays in bands");
        }

        [Test]
        public void LipAndVoid_ShareTheTopDrawCall()
        {
            foreach (ExtrudedShapeType type in new[] { ExtrudedShapeType.Plateau, ExtrudedShapeType.Void })
            {
                ExtrudedShape shape = Create(type, null);
                Configure(shape, serialized => serialized.FindProperty("_borderWidth").floatValue = 0.2f);

                Mesh top = TopPieces(shape)[0].GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(ShapeMeshData.SubmeshCount, top.subMeshCount, type.ToString());
                Assert.AreEqual(0, top.GetTriangles(ShapeMeshData.WallSubmesh).Length, type.ToString());
                var surfaces = new List<Vector2>();
                top.GetUVs(ShapeMeshData.SurfaceChannel, surfaces);
                Assert.AreEqual(top.vertexCount, surfaces.Count, type.ToString());
                Assert.IsTrue(surfaces.Exists(surface => surface.x > 0.5f), $"{type}: flat colour vertices");
                Assert.IsTrue(surfaces.Exists(surface => surface.x < 0.5f), $"{type}: textured vertices");
            }
        }

        [Test]
        public void Shadow_LiesOnTheGroundUnderActors()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            Transform shadow = shape.transform.Find(ExtrudedShape.ShadowName);

            Assert.IsNotNull(shadow);
            var renderer = shadow.GetComponent<MeshRenderer>();
            Assert.AreEqual(shape.ShadowOrder, renderer.sortingOrder);
            Assert.Less(renderer.sortingOrder, ExtrudedShapeMenu.CharacterOrder, "actors stand on the shadow");
            Assert.IsNull(shadow.GetComponentInParent<SortingGroup>(), "the shadow is not y-sorted with the shape");
            Assert.Greater(shadow.GetComponent<MeshFilter>().sharedMesh.GetTriangles(ShapeMeshData.WallSubmesh).Length, 0);
        }

        [Test]
        public void Shadow_IsAFlatCardCastAwayFromTheLight()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            var serialized = new SerializedObject(shape);
            Vector2 cast = serialized.FindProperty("_shadowDirection").vector2Value.normalized * (serialized.FindProperty("_shadowLength").floatValue * Height);
            Rect footprint = Extent(shape.Footprint());
            Mesh mesh = shape.transform.Find(ExtrudedShape.ShadowName).GetComponent<MeshFilter>().sharedMesh;
            Rect shadow = Extent(new List<Vector3>(mesh.vertices).ConvertAll(vertex => (Vector2)vertex));

            Assert.Greater(cast.x, 0f, "the default card falls to the right like the prop shadows");
            Assert.AreEqual(footprint.xMax + cast.x, shadow.xMax, 0.01f, "the far side is the footprint moved by the cast");
            Assert.AreEqual(footprint.yMin + cast.y, shadow.yMin, 0.01f);
            Assert.GreaterOrEqual(shadow.xMin, footprint.xMin - 0.001f, "nothing on the lit side");
            var colors = new List<Color>();
            mesh.GetColors(colors);
            Assert.IsTrue(colors.TrueForAll(color => Mathf.Approximately(color.a, colors[0].a)), "one flat colour, no gradient");
        }

        [Test]
        public void Shadow_OnlyForRaisedShapesThatCastOne()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            Configure(shape, serialized => serialized.FindProperty("_castShadow").boolValue = false);
            Assert.IsNull(shape.transform.Find(ExtrudedShape.ShadowName), "switched off");

            Configure(shape, serialized =>
            {
                serialized.FindProperty("_castShadow").boolValue = true;
                serialized.FindProperty("_height").floatValue = 0f;
            });
            Assert.IsNull(shape.transform.Find(ExtrudedShape.ShadowName), "flat ground casts nothing");
        }

        [Test]
        public void Ring_CastsIntoItsFloorToo()
        {
            ExtrudedShape ring = Create(ExtrudedShapeType.Ring, null);
            Mesh shadow = ring.transform.Find(ExtrudedShape.ShadowName).GetComponent<MeshFilter>().sharedMesh;
            Vector2[] floor = ExtrudeFormula.Offset(ring.Footprint(), -RimWidth).ToArray();

            Assert.IsTrue(System.Array.Exists(shadow.vertices, vertex => Inside(floor, vertex) && DistanceToEdge(new List<Vector2>(floor), vertex) > 0.05f), "the inner wall casts onto the floor");
        }

        [Test]
        public void TierShadow_StaysOnTheParentTopInsideTheTierPiece()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(parent, 0.5f, 1f);
            List<Vector2> ground = parent.Footprint();

            Assert.IsNull(tier.transform.Find(ExtrudedShape.ShadowName), "no ground shadow for a tier");
            Transform piece = tier.transform.Find(ExtrudedShape.VisualName).GetChild(0);
            Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
            var surfaces = new List<Vector2>();
            mesh.GetUVs(ShapeMeshData.SurfaceChannel, surfaces);
            Vector3[] vertices = mesh.vertices;
            int shadowVertices = 0;
            foreach (int index in mesh.GetTriangles(ShapeMeshData.WallSubmesh))
            {
                if (surfaces[index].x < 0.5f) continue;
                shadowVertices++;
                Vector2 onTop = (Vector2)piece.TransformPoint(vertices[index]) - Vector2.up * tier.BaseElevation;
                Assert.IsTrue(Inside(ground.ToArray(), onTop) || DistanceToEdge(ground, onTop) < 0.01f, $"shadow point {onTop} hangs off the parent");
            }
            Assert.Greater(shadowVertices, 0, "the tier shadow is drawn in the tier's own piece");
        }

        [Test]
        public void Pieces_SortAtTheirTransformWhateverUnityUses()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            foreach (Transform piece in shape.transform.Find(ExtrudedShape.VisualName))
            {
                Bounds bounds = piece.GetComponent<MeshRenderer>().bounds;
                Assert.AreEqual(piece.position.y, bounds.center.y, 0.001f, piece.name);
            }
        }

        [Test]
        public void FlatShape_IsOnePieceWithoutWalls()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            Configure(shape, serialized =>
            {
                serialized.FindProperty("_height").floatValue = 0f;
                serialized.FindProperty("_ySort").boolValue = false;
            });

            Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
            Assert.AreEqual(1, visual.childCount);
            Assert.IsNull(visual.GetChild(0).GetComponent<SortingGroup>());
            Mesh mesh = visual.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(0, mesh.GetTriangles(ShapeMeshData.WallSubmesh).Length);
            Assert.Greater(mesh.GetTriangles(ShapeMeshData.TopSubmesh).Length, 0);
        }

        [Test]
        public void Ring_FloorAndSurroundingsAreWalkable()
        {
            Create(ExtrudedShapeType.Ring, null);

            Assert.IsTrue(Blocked(new Vector2(0f, -3.2f + RimWidth * 0.5f)), "south band");
            Assert.IsTrue(Blocked(new Vector2(0f, 3.2f - RimWidth * 0.5f)), "north band");
            Assert.IsFalse(Blocked(new Vector2(0f, -1f)), "floor inside");
            Assert.IsFalse(Blocked(new Vector2(0f, 1.6f)), "floor right in front of the inner wall");
        }

        [Test]
        public void SharpCorners_MakeAStraightBox()
        {
            ExtrudedShape shape = Create(ExtrudedShapeType.Plateau, null);
            shape.SetPoints(Square, new[] { true, true, true, true });

            Assert.AreEqual(4, shape.Footprint().Count);
            var collider = shape.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>();
            Assert.AreEqual(4, collider.GetPath(0).Length);
            Assert.IsTrue(Blocked(new Vector2(0.95f, -0.95f)), "corner stays square");
        }

        [Test]
        public void Tier_StandsOnItsParentWithoutGroundCollision()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            Configure(parent, serialized => serialized.FindProperty("_castShadow").boolValue = false);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(parent, 0.5f, 1f);
            FootprintRange(tier, out float minY, out float maxY);

            Assert.IsTrue(tier.IsStacked);
            Assert.AreEqual(1, tier.TierIndex);
            Assert.AreEqual(Height, tier.BaseElevation, 0.0001f);
            Assert.AreEqual(Height + 1f, tier.TopElevation, 0.0001f);
            Assert.IsNull(tier.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>(), "a tier is in the air");
            Assert.AreEqual(minY + Height, VisualBounds(tier).min.y, 0.01f, "the tier's walls start on the parent's top");

            Configure(parent, serialized => serialized.FindProperty("_height").floatValue = Height + 1f);
            Assert.AreEqual(minY + Height + 1f, VisualBounds(tier).min.y, 0.01f, "raising the parent lifts the tier");
        }

        [Test]
        public void Tier_SortsInFrontOfTheParentTopAroundIt()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            Configure(parent, serialized => serialized.FindProperty("_actorHeight").floatValue = 10f);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(parent, 0.5f, 1f);
            Assert.IsFalse(tier.MergesWalls, "tiers below the actor height keep their bands");
            FootprintRange(tier, out float tierMinY, out _);

            float tierFront = float.MaxValue;
            foreach (Transform piece in tier.transform.Find(ExtrudedShape.VisualName)) tierFront = Mathf.Min(tierFront, piece.position.y);
            float parentUnderTier = float.MinValue;
            foreach (Transform piece in parent.transform.Find(ExtrudedShape.VisualName))
            {
                if (piece.position.y <= tierMinY) parentUnderTier = Mathf.Max(parentUnderTier, piece.position.y);
            }

            Assert.Greater(parentUnderTier, float.MinValue, "a parent top band reaches the tier's front edge");
            Assert.Less(tierFront, parentUnderTier, "the tier's front draws after the parent band under it");
        }

        [Test]
        public void TierAboveTheActors_IsOnePieceInFrontOfTheWholeParent()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            ExtrudedShape middle = ExtrudedShapeMenu.AddTier(parent, 0.6f, 1f);
            ExtrudedShape upper = ExtrudedShapeMenu.AddTier(middle, 0.5f, 1f);

            float below = float.MaxValue;
            foreach (ExtrudedShape tier in new[] { middle, upper })
            {
                Transform visual = tier.transform.Find(ExtrudedShape.VisualName);
                Assert.IsTrue(tier.MergesWalls, tier.name);
                Assert.AreEqual(1, visual.childCount, $"{tier.name}: walls and top in one piece");
                Mesh mesh = visual.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
                Assert.Greater(mesh.GetTriangles(ShapeMeshData.WallSubmesh).Length, 0, tier.name);
                Assert.Greater(mesh.GetTriangles(ShapeMeshData.TopSubmesh).Length, 0, tier.name);

                Transform under = tier.ParentShape.transform.Find(ExtrudedShape.VisualName);
                foreach (Transform piece in under)
                {
                    Assert.Less(visual.GetChild(0).position.y, piece.position.y, $"{tier.name} draws after {tier.ParentShape.name}/{piece.name}");
                }
                Assert.Less(visual.GetChild(0).position.y, below);
                below = visual.GetChild(0).position.y;
            }
        }

        [Test]
        public void Tier_UsesTheActorHeightOfTheGroundShape()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(parent, 0.5f, 1f);
            Configure(tier, serialized => serialized.FindProperty("_actorHeight").floatValue = 50f);
            Configure(parent, serialized => serialized.FindProperty("_actorHeight").floatValue = 0.4f);

            Assert.AreEqual(0.4f, tier.ActorHeight, 0.0001f);
            Assert.IsTrue(tier.MergesWalls);
        }

        [Test]
        public void MoveAll_MovesTheOutlineAndItsTiersButNotThePivot()
        {
            ExtrudedShape parent = Create(ExtrudedShapeType.Plateau, null);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(parent, 0.5f, 1f);
            Vector2 first = parent.Points[0];
            Vector2 tierFirst = tier.Points[0];
            Vector3 position = parent.transform.position;
            Assert.IsTrue(Blocked(new Vector2(-3.5f, 0f)), "west side before the move");

            ShapePointHandles.MoveAll(parent, new Vector3(2f, -1f, 0f));

            Assert.Less(Vector2.Distance(first + new Vector2(2f, -1f), parent.Points[0]), 0.0001f);
            Assert.Less(Vector2.Distance(tierFirst + new Vector2(2f, -1f), tier.Points[0]), 0.0001f, "tiers come along");
            Assert.AreEqual(position, parent.transform.position, "pivot and position stay where they were");
            Assert.IsFalse(Blocked(new Vector2(-3.5f, 0f)), "the collider moved with the outline");
            Assert.IsTrue(Blocked(new Vector2(5.5f, -1f)));
        }

        [Test]
        public void Path_BuildsAStripAsWideAsSet()
        {
            var go = new GameObject("Test Path");
            _created.Add(go);
            var path = go.AddComponent<PathShape>();
            path.SetPoints(new[] { new Vector2(-3f, 0f), new Vector2(3f, 0f) }, new[] { true, true });

            Mesh mesh = go.transform.Find(PathShape.StripName).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.vertexCount);
            Assert.AreEqual(path.Width, mesh.bounds.size.y, 0.001f);
            Assert.AreEqual(6f, mesh.bounds.size.x, 0.001f);
            var surfaces = new List<Vector2>();
            mesh.GetUVs(ShapeMeshData.SurfaceChannel, surfaces);
            Assert.AreEqual(mesh.vertexCount, surfaces.Count, "the shared shader reads this channel");
            Assert.IsTrue(surfaces.TrueForAll(surface => surface == Vector2.zero), "a road is always textured");
        }

        [Test]
        public void Bake_RingFloorStaysWalkableInGridAndPolygons()
        {
            var root = new GameObject("RingMap");
            _created.Add(root);
            var map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(map);
            serialized.FindProperty("_bounds").rectValue = new Rect(-8f, -8f, 16f, 16f);
            serialized.FindProperty("_cellSize").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Create(ExtrudedShapeType.Ring, root.transform);

            WalkableAreaJson data = MapLabWalkableBaker.Compute(map);
            bool[] cells = MapLabWalkableBaker.DecodeCells(data.cells, data.CellCount);
            int column = Mathf.FloorToInt((0.25f - data.bounds.x) / data.cellSize);
            int row = Mathf.FloorToInt((-0.75f - data.bounds.y) / data.cellSize);
            Assert.IsTrue(cells[row * data.columns + column], "grid: floor inside the ring");

            foreach (var hole in data.holes)
            {
                Assert.IsFalse(Inside(hole.points, new Vector2(0.25f, -0.75f)), "a baked obstacle polygon covers the open floor");
            }
        }
        #endregion

        #region [Private Methods]
        private ExtrudedShape Create(ExtrudedShapeType type, Transform parent)
        {
            var go = new GameObject($"Test {type}");
            if (parent != null) go.transform.SetParent(parent, false);
            else _created.Add(go);

            var shape = go.AddComponent<ExtrudedShape>();
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_type").enumValueIndex = (int)type;
            serialized.FindProperty("_height").floatValue = Height;
            serialized.FindProperty("_rimWidth").floatValue = RimWidth;
            serialized.FindProperty("_sortingOrder").intValue = ExtrudedShapeMenu.CharacterOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.SetPoints(Box);
            return shape;
        }

        private static void Configure(ExtrudedShape shape, System.Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(shape);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private static void FootprintRange(ExtrudedShape shape, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            foreach (var point in shape.Footprint())
            {
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
        }

        private static Rect Extent(List<Vector2> points)
        {
            Rect extent = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (var point in points)
            {
                extent.xMin = Mathf.Min(extent.xMin, point.x);
                extent.yMin = Mathf.Min(extent.yMin, point.y);
                extent.xMax = Mathf.Max(extent.xMax, point.x);
                extent.yMax = Mathf.Max(extent.yMax, point.y);
            }
            return extent;
        }

        private static float DistanceToEdge(List<Vector2> polygon, Vector2 point)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                float t = Mathf.Clamp01(Vector2.Dot(point - a, b - a) / Mathf.Max(1e-6f, (b - a).sqrMagnitude));
                nearest = Mathf.Min(nearest, Vector2.Distance(point, a + (b - a) * t));
            }
            return nearest;
        }

        private static List<Transform> TopPieces(ExtrudedShape shape)
        {
            var tops = new List<Transform>();
            foreach (Transform piece in shape.transform.Find(ExtrudedShape.VisualName))
            {
                if (piece.GetComponent<MeshFilter>().sharedMesh.GetTriangles(ShapeMeshData.TopSubmesh).Length > 0) tops.Add(piece);
            }
            return tops;
        }

        private static Bounds VisualBounds(ExtrudedShape shape)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var filter in shape.transform.Find(ExtrudedShape.VisualName).GetComponentsInChildren<MeshFilter>())
            {
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(vertex);
                    if (first) bounds = new Bounds(world, Vector3.zero);
                    else bounds.Encapsulate(world);
                    first = false;
                }
            }
            return bounds;
        }

        private static bool Blocked(Vector2 point)
        {
            Physics2D.SyncTransforms();
            return Physics2D.OverlapPoint(point) != null;
        }

        private static bool Inside(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y)
                    && point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }
        #endregion
    }
}
