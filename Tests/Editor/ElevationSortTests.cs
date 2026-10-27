using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class ElevationSortTests
    {
        #region [Variables/Fields]
        private const float Height = 1.5f;
        private const float TierHeight = 1f;
        private const float TierScale = 0.45f;

        private static readonly Vector2[] Rectangle =
        {
            new Vector2(-4f, -2f), new Vector2(4f, -2f), new Vector2(4f, 3f), new Vector2(-4f, 3f)
        };

        private static readonly bool[] Corners = { true, true, true, true };

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
        public void WalkOnTop_SlicesATopTallerThanTheActors()
        {
            ExtrudedShape plateau = CreatePlateau(false);
            Assert.IsTrue(plateau.MergesTop, "a 1.5 high top is one piece when nobody walks on it");

            SetWalkOnTop(plateau, true);
            Assert.IsTrue(plateau.WalkOnTop);
            Assert.IsFalse(plateau.MergesTop);
            Assert.Greater(TopPieces(plateau), 1, "actors standing on the top need it in bands");
        }

        [Test]
        public void Void_IsNeverWalkable()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            Configure(plateau, serialized => serialized.FindProperty("_type").enumValueIndex = (int)ExtrudedShapeType.Void);
            Assert.IsFalse(plateau.WalkOnTop);
        }

        [Test]
        public void TierOnAWalkablePlateau_KeepsItsBands()
        {
            ExtrudedShape plateau = CreatePlateau(false);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            Assert.IsTrue(tier.MergesWalls, "a tier above the actors is one piece when nobody walks on the plateau");

            SetWalkOnTop(plateau, true);
            Assert.AreEqual(plateau.TopElevation, tier.ActorFloor, 0.0001f);
            Assert.IsFalse(tier.MergesWalls, "actors on the plateau stand next to the tier");
        }

        [Test]
        public void SurfaceSortBias_IsWhatTheTiersOfTheSurfaceUse()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            float slice = new SerializedObject(plateau).FindProperty("_sortSlice").floatValue;

            Assert.AreEqual(slice + 0.01f, plateau.SurfaceSortBias, 0.0001f);
            Assert.AreEqual(plateau.SurfaceSortBias + slice + 0.01f, tier.SurfaceSortBias, 0.0001f);
        }

        [Test]
        public void ActorsOnTop_SortLikeATier_HaveNoMisses()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            List<ElevationSortMiss> misses = Check(plateau, tier, ElevationSortVariant.LikeATier);

            Assert.IsEmpty(misses, string.Join("\n", misses.Take(5).Select(miss => $"{miss.Standee} {miss.Kind} by {miss.Other} gap {miss.DepthGap:0.###}")));
        }

        [Test]
        public void ActorsOnTop_WithoutABias_AreHiddenByTheTop()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            List<ElevationSortMiss> misses = Check(plateau, tier, ElevationSortVariant.None);

            Assert.IsNotEmpty(misses, "the check finds the top drawn over the feet");
            Assert.IsTrue(misses.All(miss => miss.Kind == ElevationSortMissKind.Hidden));
            Assert.IsTrue(misses.Any(miss => miss.Other.StartsWith(plateau.name)));
        }
        [Test]
        public void ActorsOnRampsAndStairs_SortLikeTheRamp_HaveNoMisses()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            RampShape ramp = AddRamp(plateau, new Vector2(0f, -2f), 2f, 2.5f, RampStyle.Ramp);
            RampShape stairs = AddRamp(plateau, new Vector2(2.6f, -2f), 1.6f, 2f, RampStyle.Stairs);
            List<ElevationSortMiss> misses = CheckRamps(plateau, new[] { ramp, stairs }, true);

            Assert.IsEmpty(misses, string.Join("\n", misses.Take(5).Select(miss => $"{miss.Standee} {miss.Kind} by {miss.Other} gap {miss.DepthGap:0.###}")));
        }

        [Test]
        public void ActorsOnARamp_WithoutABias_AreHiddenByIt()
        {
            ExtrudedShape plateau = CreatePlateau(true);
            RampShape ramp = AddRamp(plateau, new Vector2(0f, -2f), 2f, 2.5f, RampStyle.Ramp);
            List<ElevationSortMiss> misses = CheckRamps(plateau, new[] { ramp }, false);

            Assert.IsNotEmpty(misses);
            Assert.IsTrue(misses.All(miss => miss.Kind == ElevationSortMissKind.Hidden && miss.Other.StartsWith(ramp.name)));
        }
        #endregion

        #region [Private Methods]
        private RampShape AddRamp(ExtrudedShape plateau, Vector2 onEdge, float width, float length, RampStyle style)
        {
            RampShape ramp = ExtrudedShapeMenu.AddRamp(plateau, RampFormula.Project(plateau.Footprint(), onEdge));
            var serialized = new SerializedObject(ramp);
            serialized.FindProperty("_style").enumValueIndex = (int)style;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ramp.Configure(ramp.Position, width, length, 0f);
            return ramp;
        }

        private List<ElevationSortMiss> CheckRamps(ExtrudedShape plateau, IEnumerable<RampShape> ramps, bool biased)
        {
            Sprite sprite = MapLabElevationProbe.ActorSprite(Color.cyan);
            _created.Add(sprite);
            _created.Add(sprite.texture);
            var check = new MapLabElevationSortCheck();
            check.AddShape(plateau);
            int index = 0;
            foreach (var ramp in ramps)
            {
                check.AddRamp(ramp);
                RampCorners corners = ramp.WorldCorners();
                foreach (float down in new[] { 0.2f, 0.5f, 0.8f })
                {
                    Vector2 ground = corners.TopLeft + (corners.TopRight - corners.TopLeft) * 0.5f + (corners.BottomLeft - corners.TopLeft) * down;
                    float offset = biased ? MapLabElevationProbe.SortOffset(ElevationSortVariant.LikeATier, ramp, ground) : 0f;
                    SpriteRenderer actor = MapLabElevationProbe.CreateActor($"Ramp Actor {index++}", null, ground, ramp.HeightAt(ground), offset, sprite, ExtrudedShapeMenu.CharacterOrder);
                    _created.Add(actor.transform.root.gameObject);
                    check.AddStandee(actor.transform.root.name, ground.y, actor.transform.parent.position.y, actor);
                }
            }
            return check.Run();
        }

        private List<ElevationSortMiss> Check(ExtrudedShape plateau, ExtrudedShape tier, ElevationSortVariant variant)
        {
            Sprite sprite = MapLabElevationProbe.ActorSprite(Color.cyan);
            _created.Add(sprite);
            _created.Add(sprite.texture);

            Bounds tierArea = Footprint(tier);
            var spots = new List<(ExtrudedShape Surface, Vector2 Ground)>
            {
                (plateau, new Vector2(-2.5f, -1.7f)),
                (plateau, new Vector2(-2.5f, 0.4f)),
                (plateau, new Vector2(-2.5f, 2.6f)),
                (plateau, new Vector2(tierArea.center.x, tierArea.min.y - 0.3f)),
                (tier, new Vector2(tierArea.center.x + 0.4f, tierArea.center.y))
            };

            var check = new MapLabElevationSortCheck();
            check.AddShape(plateau);
            check.AddShape(tier);
            for (int i = 0; i < spots.Count; i++)
            {
                (ExtrudedShape surface, Vector2 ground) = spots[i];
                float offset = MapLabElevationProbe.SortOffset(variant, surface, ground);
                SpriteRenderer actor = MapLabElevationProbe.CreateActor($"Actor {i}", null, ground, surface.TopElevation, offset, sprite, ExtrudedShapeMenu.CharacterOrder);
                _created.Add(actor.transform.root.gameObject);
                check.AddStandee(actor.transform.root.name, ground.y, actor.transform.parent.position.y, actor);
            }
            return check.Run();
        }

        private ExtrudedShape CreatePlateau(bool walkOnTop)
        {
            var go = new GameObject("Plateau");
            _created.Add(go);
            var shape = go.AddComponent<ExtrudedShape>();
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_type").enumValueIndex = (int)ExtrudedShapeType.Plateau;
            serialized.FindProperty("_height").floatValue = Height;
            serialized.FindProperty("_sortingOrder").intValue = ExtrudedShapeMenu.CharacterOrder;
            serialized.FindProperty("_walkOnTop").boolValue = walkOnTop;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.SetPoints(Rectangle, Corners);
            return shape;
        }

        private static void SetWalkOnTop(ExtrudedShape shape, bool walkOnTop)
        {
            Configure(shape, serialized => serialized.FindProperty("_walkOnTop").boolValue = walkOnTop);
        }

        private static void Configure(ExtrudedShape shape, System.Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(shape);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private static int TopPieces(ExtrudedShape shape)
        {
            int count = 0;
            foreach (Transform piece in shape.transform.Find(ExtrudedShape.VisualName))
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.GetTriangles(ShapeMeshData.TopSubmesh).Length > 0) count++;
            }
            return count;
        }

        private static Bounds Footprint(ExtrudedShape shape)
        {
            List<Vector2> points = shape.Footprint();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            return bounds;
        }
        #endregion
    }
}
