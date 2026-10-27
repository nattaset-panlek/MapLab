using System.Collections.Generic;
using System.Linq;
using MapLab.Editor;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class ExtrudedShapeOutlineTests
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
        public void Edge_FollowsTheGroundOutlineAsAClosedLoop()
        {
            var root = new GameObject("Set");
            _created.Add(root);
            ExtrudedShape plateau = Shape(ExtrudedShapeType.Plateau, root.transform, new Vector3(3f, 1f, 0f));
            GameObject edge = Make(plateau, ExtrudedShapeOutlineKind.EdgeCollider, ExtrudedShapeOutlineLevel.Ground);

            List<Vector2> footprint = plateau.Footprint();
            EdgeCollider2D collider = edge.GetComponent<EdgeCollider2D>();
            Assert.AreEqual(footprint.Count + 1, collider.pointCount, "closed: the first point comes back at the end");
            Assert.AreEqual(collider.points[0], collider.points[collider.pointCount - 1]);
            for (int i = 0; i < footprint.Count; i++) Assert.Less(Vector2.Distance(footprint[i], collider.points[i]), 0.0001f);
            Assert.AreEqual(root.transform, edge.transform.parent, "it sits next to the shape");
            Assert.AreEqual(plateau.transform.localPosition, edge.transform.localPosition);
            Assert.AreEqual(plateau.transform.GetSiblingIndex() + 1, edge.transform.GetSiblingIndex());
            Assert.AreEqual("Plateau Edge", edge.name);
            Assert.IsNull(edge.GetComponent<ExtrudedShape>(), "a plain GameObject, not a second shape");
        }

        [Test]
        public void Top_IsLiftedByTheHeight()
        {
            ExtrudedShape plateau = Shape(ExtrudedShapeType.Plateau, null, Vector3.zero);
            GameObject edge = Make(plateau, ExtrudedShapeOutlineKind.EdgeCollider, ExtrudedShapeOutlineLevel.Top);
            List<Vector2> footprint = plateau.Footprint();
            Vector2[] points = edge.GetComponent<EdgeCollider2D>().points;
            for (int i = 0; i < footprint.Count; i++) Assert.Less(Vector2.Distance(footprint[i] + Vector2.up * plateau.TopElevation, points[i]), 0.0001f);

            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau);
            List<List<Vector2>> ground = ExtrudedShapeOutlines.Loops(tier, ExtrudedShapeOutlineLevel.Ground);
            Assert.Less(Vector2.Distance(tier.Footprint()[0] + Vector2.up * plateau.TopElevation, ground[0][0]), 0.0001f, "a tier's ground is the top it stands on");
        }

        [Test]
        public void Ring_GetsBothOutlines()
        {
            ExtrudedShape ring = Shape(ExtrudedShapeType.Ring, null, Vector3.zero);
            GameObject edges = Make(ring, ExtrudedShapeOutlineKind.EdgeCollider, ExtrudedShapeOutlineLevel.Ground);
            Assert.AreEqual(2, edges.GetComponents<EdgeCollider2D>().Length, "outside and inside");

            GameObject polygon = Make(ring, ExtrudedShapeOutlineKind.PolygonCollider, ExtrudedShapeOutlineLevel.Ground);
            PolygonCollider2D collider = polygon.GetComponent<PolygonCollider2D>();
            Assert.AreEqual(2, collider.pathCount, "the hole stays open");
            Assert.AreEqual("Ring Collider", polygon.name);

            GameObject mesh = Make(ring, ExtrudedShapeOutlineKind.Mesh, ExtrudedShapeOutlineLevel.Ground);
            Mesh shared = mesh.GetComponent<MeshFilter>().sharedMesh;
            int count = ring.Footprint().Count;
            Assert.AreEqual(count * 2, shared.vertexCount);
            Assert.AreEqual(count * 6, shared.triangles.Length, "a band between the outside and the hole");
        }

        [Test]
        public void Mesh_FillsThePlateauAndDrawsAtItsOrder()
        {
            ExtrudedShape plateau = Shape(ExtrudedShapeType.Plateau, null, Vector3.zero);
            GameObject go = Make(plateau, ExtrudedShapeOutlineKind.Mesh, ExtrudedShapeOutlineLevel.Ground);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            int count = plateau.Footprint().Count;
            Assert.AreEqual(count, mesh.vertexCount);
            Assert.AreEqual((count - 2) * 3, mesh.triangles.Length);
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer.sharedMaterial);
            Assert.AreEqual(plateau.SortingOrder, renderer.sortingOrder);
            Assert.IsNull(go.GetComponent<Collider2D>(), "a mesh, no collider");
        }
        #endregion

        #region [Private Methods]
        private ExtrudedShape Shape(ExtrudedShapeType type, Transform parent, Vector3 position)
        {
            var points = new List<Vector2> { new Vector2(-2f, -1f), new Vector2(2f, -1f), new Vector2(2.5f, 1f), new Vector2(0f, 2f), new Vector2(-2.5f, 1f) };
            ExtrudedShape shape = ExtrudedShapeMenu.Create(type, type.ToString(), parent, position, points, 1.5f, 0.4f, 6);
            if (parent == null) _created.Add(shape.gameObject);
            return shape;
        }

        private GameObject Make(ExtrudedShape shape, ExtrudedShapeOutlineKind kind, ExtrudedShapeOutlineLevel level)
        {
            GameObject go = ExtrudedShapeOutlines.Create(shape, kind, level);
            _created.Add(go);
            return go;
        }
        #endregion
    }
}
