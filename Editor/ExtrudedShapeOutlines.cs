using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLab.Editor
{
    public enum ExtrudedShapeOutlineKind
    {
        EdgeCollider,
        PolygonCollider,
        Mesh
    }

    public enum ExtrudedShapeOutlineLevel
    {
        Ground,
        Top
    }

    public static class ExtrudedShapeOutlines
    {
        #region [Variables/Fields]
        public const string MeshFolderName = "Outlines";
        public static readonly string[] KindLabels = { "Edge Collider 2D", "Polygon Collider 2D", "Mesh" };
        public static readonly string[] LevelLabels = { "Ground outline", "Top outline" };
        private const string SpriteMaterial = "Sprites-Default.mat";
        #endregion

        #region [Public Methods]
        public static string Suffix(ExtrudedShapeOutlineKind kind)
        {
            switch (kind)
            {
                case ExtrudedShapeOutlineKind.EdgeCollider: return "Edge";
                case ExtrudedShapeOutlineKind.PolygonCollider: return "Collider";
                default: return "Mesh";
            }
        }

        public static List<List<Vector2>> Loops(ExtrudedShape shape, ExtrudedShapeOutlineLevel level)
        {
            List<Vector2> footprint = shape.Footprint();
            var loops = new List<List<Vector2>> { footprint };
            if (shape.Type == ExtrudedShapeType.Ring)
            {
                List<Vector2> hole = ExtrudeFormula.Offset(footprint, -shape.RimWidth);
                hole.Reverse();
                loops.Add(hole);
            }
            float lift = level == ExtrudedShapeOutlineLevel.Top ? shape.TopElevation : shape.BaseElevation;
            if (Mathf.Approximately(lift, 0f)) return loops;
            return loops.Select(loop => loop.Select(point => point + Vector2.up * lift).ToList()).ToList();
        }

        public static GameObject Create(ExtrudedShape shape, ExtrudedShapeOutlineKind kind, ExtrudedShapeOutlineLevel level)
        {
            List<List<Vector2>> loops = Loops(shape, level);
            Transform source = shape.transform;
            var go = new GameObject($"{shape.name} {Suffix(kind)}") { layer = shape.gameObject.layer };
            if (source.parent == null) SceneManager.MoveGameObjectToScene(go, shape.gameObject.scene);
            go.transform.SetParent(source.parent, false);
            go.transform.localPosition = source.localPosition;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = source.localScale;
            go.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);

            switch (kind)
            {
                case ExtrudedShapeOutlineKind.EdgeCollider:
                    foreach (var loop in loops) go.AddComponent<EdgeCollider2D>().SetPoints(new List<Vector2>(loop) { loop[0] });
                    break;
                case ExtrudedShapeOutlineKind.PolygonCollider:
                    var polygon = go.AddComponent<PolygonCollider2D>();
                    polygon.pathCount = loops.Count;
                    for (int i = 0; i < loops.Count; i++) polygon.SetPath(i, loops[i]);
                    break;
                default:
                    Mesh mesh = BuildMesh(loops, go.name);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>(SpriteMaterial);
                    MeshRenderer visual = source.Find(ExtrudedShape.VisualName)?.GetComponentInChildren<MeshRenderer>(true);
                    if (visual != null) renderer.sortingLayerID = visual.sortingLayerID;
                    renderer.sortingOrder = shape.SortingOrder;
                    SaveMesh(shape, mesh);
                    break;
            }
            Undo.RegisterCreatedObjectUndo(go, $"Create {KindLabels[(int)kind]} From {shape.name}");
            return go;
        }

        public static Mesh BuildMesh(List<List<Vector2>> loops, string name)
        {
            List<Vector2> outer = loops[0];
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int count = outer.Count;
            if (loops.Count > 1 && loops[1].Count == count)
            {
                List<Vector2> inner = Enumerable.Reverse(loops[1]).ToList();
                vertices.AddRange(outer.Select(point => (Vector3)point));
                vertices.AddRange(inner.Select(point => (Vector3)point));
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    triangles.AddRange(new[] { i, next, count + next, i, count + next, count + i });
                }
            }
            else
            {
                vertices.AddRange(outer.Select(point => (Vector3)point));
                triangles.AddRange(ExtrudeFormula.Triangulate(outer));
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, vertices.Select(vertex => (Vector2)vertex).ToList());
            mesh.SetColors(Enumerable.Repeat(Color.white, vertices.Count).ToList());
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        #endregion

        #region [Private Methods]
        private static void SaveMesh(ExtrudedShape shape, Mesh mesh)
        {
            MapLabMap map = shape.GetComponentInParent<MapLabMap>();
            if (map == null || string.IsNullOrEmpty(shape.gameObject.scene.path)) return;
            string folder = $"{MapLabOutputPaths.MapFolder(map)}/{MeshFolderName}";
            MapLabOutputPaths.EnsureFolder(folder);
            string file = new string(mesh.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{file}.asset"));
        }
        #endregion
    }
}
