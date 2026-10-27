using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Shapes
{
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    public class PathShape : PointShape
    {
        #region [Variables/Fields]
        public const string StripName = "Strip";

        private const string MainTexProperty = "_MainTex";
        private const string TintProperty = "_White";

        [SerializeField, Min(0.05f)] private float _width = 1.5f;
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private Texture2D _texture;
        [SerializeField, Min(0.1f)] private float _textureLength = 2f;
        [SerializeField] private Material _material;
        [SerializeField] private int _sortingOrder = -92;

        private MaterialPropertyBlock _block;
        #endregion

        #region [Properties]
        public override bool Closed => false;
        public float Width => _width;
        #endregion

        #region [Unity Methods]
        private void Reset()
        {
            SetPoints(new[] { new Vector2(-4f, 0f), new Vector2(0f, 1f), new Vector2(4f, 0f) });
        }

        private void OnDestroy()
        {
            Transform strip = transform.Find(StripName);
            if (strip != null) DestroyGenerated(strip.GetComponent<MeshFilter>()?.sharedMesh);
        }
        #endregion

        #region [Public Methods]
        public override void Rebuild()
        {
            if (Points.Count < MinPointCount) return;

            List<Vector2> line = SampleOutline();
            var left = new List<Vector2>(line.Count);
            var right = new List<Vector2>(line.Count);
            ExtrudeFormula.Strip(line, _width, left, right);

            var vertices = new List<Vector3>(line.Count * 2);
            var uvs = new List<Vector2>(line.Count * 2);
            var colors = new List<Color>(line.Count * 2);
            var triangles = new List<int>(Mathf.Max(0, line.Count - 1) * 6);
            float length = Mathf.Max(0.1f, _textureLength);
            float distance = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) distance += Vector2.Distance(line[i - 1], line[i]);
                vertices.Add(right[i]);
                vertices.Add(left[i]);
                uvs.Add(new Vector2(distance / length, 0f));
                uvs.Add(new Vector2(distance / length, 1f));
                colors.Add(_color);
                colors.Add(_color);
                if (i == 0) continue;
                int start = (i - 1) * 2;
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }

            GameObject strip = EnsureChild(StripName);
            var filter = Ensure<MeshFilter>(strip);
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) mesh = new Mesh { name = "MapLab Path", hideFlags = HideFlags.DontSave };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(ShapeMeshData.SurfaceChannel, new Vector2[vertices.Count]);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;

            var renderer = Ensure<MeshRenderer>(strip);
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = _sortingOrder;
            renderer.enabled = !VisualBaked;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetTexture(MainTexProperty, _texture != null ? _texture : Texture2D.whiteTexture);
            _block.SetColor(TintProperty, Color.white);
            renderer.SetPropertyBlock(_block);
        }
        #endregion
    }
}
