using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Shapes
{
    public class ShapeMeshData
    {
        #region [Variables/Fields]
        public const int WallSubmesh = 0;
        public const int TopSubmesh = 1;
        public const int SubmeshCount = 2;
        public const int SurfaceChannel = 1;
        public const int GroundChannel = 2;

        private const float WallBaseShade = 0.84f;
        private static readonly Vector2 Textured = Vector2.zero;
        private static readonly Vector2 FlatColor = Vector2.right;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _surfaces = new List<Vector2>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<Vector2> _grounds = new List<Vector2>();
        private readonly List<int>[] _triangles = new List<int>[SubmeshCount];
        private readonly float _height;
        private readonly float _elevation;
        private readonly float _topTextureSize;
        private readonly float _wallTextureSize;
        private readonly Color _wallColor;
        private float _minY = float.MaxValue;
        private float _maxBaseY = float.MinValue;
        private float _minBaseY = float.MaxValue;
        #endregion

        #region [Properties]
        public bool IsEmpty => _vertices.Count == 0;
        public bool HasWalls => _triangles[WallSubmesh].Count > 0;
        public float MinFootprintY => _minY;
        public float MaxWallBaseY => _maxBaseY;
        public float MinWallBaseY => _minBaseY;
        public float SortY { get; set; }
        #endregion

        #region [Public Methods]
        public ShapeMeshData(float elevation, float height, float topTextureSize, float wallTextureSize, Color wallColor)
        {
            _elevation = elevation;
            _height = height;
            _topTextureSize = Mathf.Max(0.1f, topTextureSize);
            _wallTextureSize = Mathf.Max(0.1f, wallTextureSize);
            _wallColor = wallColor;
            for (int i = 0; i < SubmeshCount; i++) _triangles[i] = new List<int>();
        }

        public void AddWall(WallQuad quad)
        {
            int start = _vertices.Count;
            float vBottom = 1f - _height / _wallTextureSize;
            _vertices.Add(quad.From + Vector2.up * _elevation);
            _vertices.Add(quad.To + Vector2.up * _elevation);
            _vertices.Add(quad.To + Vector2.up * (_elevation + _height));
            _vertices.Add(quad.From + Vector2.up * (_elevation + _height));
            _uvs.Add(new Vector2(quad.UFrom, vBottom));
            _uvs.Add(new Vector2(quad.UTo, vBottom));
            _uvs.Add(new Vector2(quad.UTo, 1f));
            _uvs.Add(new Vector2(quad.UFrom, 1f));
            for (int i = 0; i < 4; i++) _surfaces.Add(Textured);
            _colors.Add(Shade(_wallColor, quad.ShadeFrom * WallBaseShade));
            _colors.Add(Shade(_wallColor, quad.ShadeTo * WallBaseShade));
            _colors.Add(Shade(_wallColor, quad.ShadeTo));
            _colors.Add(Shade(_wallColor, quad.ShadeFrom));

            List<int> triangles = _triangles[WallSubmesh];
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            _maxBaseY = Mathf.Max(_maxBaseY, quad.MaxBaseY);
            _minBaseY = Mathf.Min(_minBaseY, Mathf.Min(quad.From.y, quad.To.y));
        }

        public void AddPolygon(IReadOnlyList<Vector2> footprint, Color color, bool flat)
        {
            if (footprint.Count < 3) return;
            int start = _vertices.Count;
            foreach (var point in footprint)
            {
                _vertices.Add(point + Vector2.up * (_elevation + _height));
                _uvs.Add(point / _topTextureSize);
                _surfaces.Add(flat ? FlatColor : Textured);
                _colors.Add(color);
                _minY = Mathf.Min(_minY, point.y);
            }

            List<int> triangles = _triangles[TopSubmesh];
            for (int i = 1; i + 1 < footprint.Count; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
            }
        }

        public void AddFace(IReadOnlyList<Vector2> ground, IReadOnlyList<float> heights, IReadOnlyList<Vector2> uvs, Color color, int submesh, bool flat)
        {
            if (ground.Count < 3) return;
            int start = _vertices.Count;
            for (int i = 0; i < ground.Count; i++)
            {
                _vertices.Add(ground[i] + Vector2.up * heights[i]);
                _uvs.Add(uvs[i]);
                _surfaces.Add(flat ? FlatColor : Textured);
                _colors.Add(color);
                _grounds.Add(ground[i]);
                _minY = Mathf.Min(_minY, ground[i].y);
                _maxBaseY = Mathf.Max(_maxBaseY, ground[i].y);
                _minBaseY = Mathf.Min(_minBaseY, ground[i].y);
            }

            List<int> triangles = _triangles[submesh];
            for (int i = 1; i + 1 < ground.Count; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
            }
        }

        public void AddShadow(ShadowLoop loop, Color color)
        {
            int count = loop.Base.Length;
            if (count < 3) return;
            List<int> triangles = _triangles[WallSubmesh];
            for (int i = 0; i < count; i++)
            {
                if (!loop.Casts[i]) continue;
                int next = (i + 1) % count;
                int start = _vertices.Count;
                AddShadowCorner(loop.Base[i], color);
                AddShadowCorner(loop.Base[next], color);
                AddShadowCorner(loop.Reach[next], color);
                AddShadowCorner(loop.Reach[i], color);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            var shifted = new List<Vector3>(_vertices.Count);
            foreach (var vertex in _vertices) shifted.Add(vertex - Vector3.up * SortY);
            mesh.SetVertices(shifted);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(SurfaceChannel, _surfaces);
            mesh.SetColors(_colors);
            if (_grounds.Count > 0 && _grounds.Count == _vertices.Count) mesh.SetUVs(GroundChannel, _grounds);
            mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++) mesh.SetTriangles(_triangles[i], i);

            var normals = new Vector3[shifted.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.normals = normals;
            mesh.bounds = SortCenteredBounds(shifted);
        }
        #endregion

        #region [Private Methods]
        private void AddShadowCorner(Vector2 point, Color color)
        {
            _vertices.Add(point + Vector2.up * _elevation);
            _uvs.Add(Vector2.zero);
            _surfaces.Add(FlatColor);
            _colors.Add(color);
        }

        private static Bounds SortCenteredBounds(List<Vector3> vertices)
        {
            if (vertices.Count == 0) return new Bounds();
            float minX = float.MaxValue, maxX = float.MinValue, reachY = 0f;
            foreach (var vertex in vertices)
            {
                minX = Mathf.Min(minX, vertex.x);
                maxX = Mathf.Max(maxX, vertex.x);
                reachY = Mathf.Max(reachY, Mathf.Abs(vertex.y));
            }
            return new Bounds(new Vector3((minX + maxX) * 0.5f, 0f, 0f), new Vector3(maxX - minX, reachY * 2f, 0.01f));
        }

        private static Color Shade(Color color, float shade)
        {
            return new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
        }
        #endregion
    }
}
