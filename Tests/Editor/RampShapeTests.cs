using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class RampShapeTests
    {
        #region [Variables/Fields]
        private const float Height = 1.5f;
        private const float Tolerance = 0.001f;

        private static readonly Vector2[] Rectangle =
        {
            new Vector2(-4.2f, -3.2f), new Vector2(4.2f, -3.2f), new Vector2(4.2f, 3.2f), new Vector2(-4.2f, 3.2f)
        };

        private static readonly bool[] Corners = { true, true, true, true };

        private readonly List<GameObject> _created = new List<GameObject>();
        private ExtrudedShape _plateau;
        #endregion

        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Plateau");
            _created.Add(go);
            _plateau = go.AddComponent<ExtrudedShape>();
            var serialized = new SerializedObject(_plateau);
            serialized.FindProperty("_height").floatValue = Height;
            serialized.FindProperty("_walkOnTop").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _plateau.SetPoints(Rectangle, Corners);
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
        public void Corners_SitOnTheEdgeAndRunOutward()
        {
            RampCorners corners = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f).WorldCorners();

            Assert.AreEqual(-3.2f, corners.TopLeft.y, Tolerance);
            Assert.AreEqual(-3.2f, corners.TopRight.y, Tolerance);
            Assert.AreEqual(0f, corners.TopCenter.x, Tolerance);
            Assert.AreEqual(2f, Vector2.Distance(corners.TopLeft, corners.TopRight), Tolerance);
            Assert.AreEqual(0f, corners.Direction.x, Tolerance);
            Assert.AreEqual(-1f, corners.Direction.y, Tolerance);
            Assert.AreEqual(-6.2f, corners.BottomCenter.y, Tolerance);
        }

        [Test]
        public void Angle_TurnsTheRunAwayFromTheEdge()
        {
            RampCorners corners = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 30f).WorldCorners();

            Assert.AreEqual(0.5f, corners.Direction.x, Tolerance);
            Assert.AreEqual(-0.866f, corners.Direction.y, Tolerance);
            Assert.AreEqual(3f, Vector2.Distance(corners.TopCenter, corners.BottomCenter), Tolerance);
            Assert.AreEqual(-3.2f, corners.TopCenter.y, Tolerance, "the top stays on the edge");
        }

        [Test]
        public void Height_GoesFromTheBaseUpToTheTop()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f);
            RampCorners corners = ramp.WorldCorners();

            Assert.AreEqual(Height, ramp.HeightAt(corners.TopCenter), Tolerance);
            Assert.AreEqual(0f, ramp.HeightAt(corners.BottomCenter), Tolerance);
            Assert.AreEqual(Height * 0.5f, ramp.HeightAt((corners.TopCenter + corners.BottomCenter) * 0.5f), Tolerance);
            Assert.AreEqual(26.57f, ramp.SlopeDegrees, 0.1f, "1.5 up over 3 along");
        }

        [Test]
        public void Stairs_StandOnFlatTreads()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 4f, 0f);
            SetStyle(ramp, RampStyle.Stairs, 4);
            RampCorners corners = ramp.WorldCorners();
            Vector2 down = corners.BottomCenter - corners.TopCenter;

            Assert.AreEqual(Height * 0.75f, ramp.HeightAt(corners.TopCenter + down * 0.3f), Tolerance, "second tread");
            Assert.AreEqual(Height * 0.75f, ramp.HeightAt(corners.TopCenter + down * 0.45f), Tolerance, "same tread");
            Assert.AreEqual(Height * 0.25f, ramp.HeightAt(corners.TopCenter + down * 0.9f), Tolerance, "last tread");
            Assert.Greater(Triangles(ramp, ShapeMeshData.WallSubmesh), 0, "risers face the camera on stairs that run south");
        }

        [Test]
        public void Ramp_FollowsItsPlateau()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f);
            var serialized = new SerializedObject(_plateau);
            serialized.FindProperty("_height").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _plateau.Rebuild();
            Assert.AreEqual(2f, ramp.HighElevation, Tolerance);
            Assert.AreEqual(2f, MaxLift(ramp), 0.01f, "the mesh reaches the new top");

            _plateau.OffsetPoints(new Vector2(1f, 0f));
            Assert.AreEqual(1f, ramp.WorldCorners().TopCenter.x, Tolerance, "moving the plateau's points moves the ramp");
        }

        [Test]
        public void Mesh_CarriesGroundPositionsUnderEveryVertex()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f);
            int checkedVertices = 0;
            foreach (Transform piece in ramp.transform.Find(RampShape.VisualName))
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                var grounds = new List<Vector2>();
                mesh.GetUVs(ShapeMeshData.GroundChannel, grounds);
                Vector3[] vertices = mesh.vertices;
                Assert.AreEqual(vertices.Length, grounds.Count, piece.name);
                foreach (int index in mesh.GetTriangles(ShapeMeshData.TopSubmesh))
                {
                    Vector2 ground = _plateau.transform.TransformPoint(grounds[index]);
                    Vector2 drawn = piece.TransformPoint(vertices[index]);
                    Assert.AreEqual(ground.x, drawn.x, Tolerance);
                    Assert.AreEqual(ramp.HeightAt(ground), drawn.y - ground.y, 0.01f);
                    checkedVertices++;
                }
            }
            Assert.Greater(checkedVertices, 0);
        }

        [Test]
        public void Collider_IsTheFootprint()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 20f);
            var collider = ramp.transform.Find(RampShape.CollisionName).GetComponent<PolygonCollider2D>();
            Vector2[] path = collider.GetPath(0).Select(point => (Vector2)collider.transform.TransformPoint(point)).ToArray();
            Vector2[] footprint = ramp.WorldCorners().Footprint();

            Assert.AreEqual(4, path.Length);
            for (int i = 0; i < 4; i++) Assert.Less(Vector2.Distance(footprint[i], path[i]), Tolerance);
        }

        [Test]
        public void Ramp_DrawsInFrontOfTheWallItLeansOn()
        {
            RampShape ramp = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f);
            float rampBack = float.MinValue;
            foreach (Transform piece in ramp.transform.Find(RampShape.VisualName)) rampBack = Mathf.Max(rampBack, piece.position.y);
            float plateauFront = float.MaxValue;
            foreach (Transform piece in _plateau.transform.Find(ExtrudedShape.VisualName)) plateauFront = Mathf.Min(plateauFront, piece.position.y);

            Assert.Less(rampBack, plateauFront);
        }

        [Test]
        public void SideWalls_OnlyWhereTheyFaceTheCamera()
        {
            RampShape south = Ramp(new Vector2(0f, -3.2f), 2f, 3f, 0f);
            RampShape east = Ramp(new Vector2(4.2f, 0f), 2f, 3f, 0f);

            Assert.AreEqual(0, Triangles(south, ShapeMeshData.WallSubmesh), "a ramp running south shows no side");
            Assert.Greater(Triangles(east, ShapeMeshData.WallSubmesh), 0, "a ramp running east shows its south side");
        }

        [Test]
        public void Project_FindsWhereAPointIsOnTheOutline()
        {
            List<Vector2> outline = _plateau.Footprint();
            float perimeter = RampFormula.Perimeter(outline);
            Vector2 point = RampFormula.PointAt(outline, perimeter * 0.3f);

            Assert.AreEqual(0.3f, RampFormula.Project(outline, point), Tolerance);
            Assert.AreEqual(0.3f, RampFormula.Project(outline, point + (point - Vector2.zero).normalized * 0.5f), 0.02f, "a point outside snaps to the nearest edge");
        }
        #endregion

        #region [Private Methods]
        private RampShape Ramp(Vector2 onEdge, float width, float length, float angle)
        {
            RampShape ramp = ExtrudedShapeMenu.AddRamp(_plateau, RampFormula.Project(_plateau.Footprint(), onEdge));
            ramp.Configure(ramp.Position, width, length, angle);
            return ramp;
        }

        private static void SetStyle(RampShape ramp, RampStyle style, int steps)
        {
            var serialized = new SerializedObject(ramp);
            serialized.FindProperty("_style").enumValueIndex = (int)style;
            serialized.FindProperty("_steps").intValue = steps;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ramp.Rebuild();
        }

        private static int Triangles(RampShape ramp, int submesh)
        {
            int count = 0;
            foreach (Transform piece in ramp.transform.Find(RampShape.VisualName)) count += piece.GetComponent<MeshFilter>().sharedMesh.GetTriangles(submesh).Length / 3;
            return count;
        }

        private float MaxLift(RampShape ramp)
        {
            float lift = float.MinValue;
            foreach (Transform piece in ramp.transform.Find(RampShape.VisualName))
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                var grounds = new List<Vector2>();
                mesh.GetUVs(ShapeMeshData.GroundChannel, grounds);
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) lift = Mathf.Max(lift, piece.TransformPoint(vertices[i]).y - _plateau.transform.TransformPoint(grounds[i]).y);
            }
            return lift;
        }
        #endregion
    }
}
