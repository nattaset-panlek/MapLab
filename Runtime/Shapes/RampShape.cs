using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Shapes
{
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    public class RampShape : MonoBehaviour
    {
        #region [Variables/Fields]
        public const string VisualName = "Visual";
        public const string CollisionName = "Collision";

        private const HideFlags GeneratedFlags = HideFlags.DontSave | HideFlags.NotEditable;
        private const string MainTexProperty = "_MainTex";
        private const string TintProperty = "_White";
        private const float TopSortBias = 0.0005f;
        private const float StackSortGap = 0.01f;
        private const float FacingCamera = -0.01f;
        private const float WallBaseShade = 0.84f;

        [SerializeField, Range(0f, 1f)] private float _position = 0.75f;
        [SerializeField, Range(0.5f, 12f)] private float _width = 2f;
        [SerializeField, Range(0.25f, 20f)] private float _length = 2.5f;
        [SerializeField, Range(-RampFormula.MaxAngle, RampFormula.MaxAngle)] private float _angle;
        [SerializeField] private RampStyle _style = RampStyle.Ramp;
        [SerializeField, Range(2, 32)] private int _steps = 6;
        [SerializeField] private bool _sideWalls = true;
        [SerializeField] private Color _surfaceColor = new Color(0.86f, 0.84f, 0.92f);
        [SerializeField] private Color _wallColor = new Color(0.72f, 0.7f, 0.8f);
        [SerializeField] private Texture2D _surfaceTexture;
        [SerializeField, Min(0.1f)] private float _surfaceTextureSize = 2f;
        [SerializeField] private Texture2D _wallTexture;
        [SerializeField, Min(0.1f)] private float _wallTextureSize = 1.5f;
        [SerializeField] private Material _material;
        [SerializeField] private int _sortingOrder = 6;
        [SerializeField, Range(0.25f, 4f)] private float _sortSlice = 0.5f;
        [SerializeField] private bool _generateCollision = true;

        private MaterialPropertyBlock _block;
        private Material[] _materials;
        #endregion

        #region [Properties]
        public ExtrudedShape Parent => transform.parent != null ? transform.parent.GetComponent<ExtrudedShape>() : null;
        public float Position => _position;
        public float Width => _width;
        public float Length => _length;
        public float Angle => _angle;
        public RampStyle Style => _style;
        public int Steps => _steps;
        public float LowElevation => Parent != null ? Parent.BaseElevation : 0f;
        public float HighElevation => Parent != null ? Parent.TopElevation : 0f;
        public float Rise => HighElevation - LowElevation;
        public float SlopeDegrees => RampFormula.SlopeDegrees(Rise, _length);
        public float BaseSortBias => Parent != null ? Parent.BaseSortBias : 0f;
        public float SurfaceSortBias => BaseSortBias + _sortSlice + StackSortGap;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            Rebuild();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildIfAlive;
            UnityEditor.EditorApplication.delayCall += RebuildIfAlive;
        }
#endif

        private void OnDestroy()
        {
            Transform visual = transform.Find(VisualName);
            if (visual == null) return;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true)) DestroyGenerated(filter.sharedMesh);
        }
        #endregion

        #region [Public Methods]
        public void Configure(float position, float width, float length, float angle)
        {
            _position = Mathf.Repeat(position, 1f);
            _width = Mathf.Clamp(width, 0.5f, 12f);
            _length = Mathf.Clamp(length, 0.25f, 20f);
            _angle = Mathf.Clamp(angle, -RampFormula.MaxAngle, RampFormula.MaxAngle);
            Rebuild();
        }

        public RampCorners LocalCorners()
        {
            ExtrudedShape parent = Parent;
            List<Vector2> outline = parent != null ? parent.Footprint() : new List<Vector2> { Vector2.zero, Vector2.right, Vector2.up };
            return RampFormula.Corners(outline, _position, _width, _length, _angle);
        }

        public RampCorners WorldCorners()
        {
            RampCorners local = LocalCorners();
            Transform space = Parent != null ? Parent.transform : transform;
            Vector2 World(Vector2 point) => space.TransformPoint(point);
            Vector2 direction = space.TransformVector(local.Direction).normalized;
            Vector2 outward = space.TransformVector(local.Outward).normalized;
            return new RampCorners(World(local.TopLeft), World(local.TopRight), World(local.BottomRight), World(local.BottomLeft), direction, outward);
        }

        public float HeightAt(Vector2 worldPoint)
        {
            return RampFormula.Height(WorldCorners(), LowElevation, HighElevation, _style == RampStyle.Stairs ? _steps : 0, worldPoint);
        }

        public void Rebuild()
        {
            if (Parent == null || !isActiveAndEnabled) return;
            RampCorners corners = LocalCorners();
            List<ShapeMeshData> pieces = BuildPieces(corners);
            ApplyPieces(pieces);
            BuildCollision(corners);
        }
        #endregion

        #region [Private Methods]
        private List<ShapeMeshData> BuildPieces(RampCorners corners)
        {
            Vector2[] footprint = corners.Footprint();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var point in footprint)
            {
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            float slice = Mathf.Max(0.05f, _sortSlice);
            int count = Mathf.Max(1, Mathf.CeilToInt((maxY - minY) / slice));
            var tops = new ShapeMeshData[count];
            var walls = new ShapeMeshData[count];

            foreach (var tread in Treads(corners))
            {
                for (int index = 0; index < count; index++)
                {
                    float bandMin = index == 0 ? float.MinValue : minY + index * slice;
                    float bandMax = index == count - 1 ? float.MaxValue : minY + (index + 1) * slice;
                    List<Vector2> clipped = ExtrudeFormula.ClipBand(tread.Footprint, bandMin, bandMax);
                    if (clipped.Count < 3) continue;
                    var heights = new float[clipped.Count];
                    var uvs = new Vector2[clipped.Count];
                    for (int i = 0; i < clipped.Count; i++)
                    {
                        heights[i] = tread.Flat ? tread.Height : RampFormula.Height(corners, LowElevation, HighElevation, clipped[i]);
                        uvs[i] = clipped[i] / _surfaceTextureSize;
                    }
                    (tops[index] ??= NewPiece()).AddFace(clipped, heights, uvs, _surfaceColor, ShapeMeshData.TopSubmesh, _surfaceTexture == null);
                }
            }

            foreach (var face in Faces(corners))
            {
                float baseY = Mathf.Max(face.Base[0].y, face.Base[1].y);
                int index = Mathf.Clamp(Mathf.FloorToInt((baseY - minY) / slice), 0, count - 1);
                (walls[index] ??= NewPiece()).AddFace(face.Ground, face.Heights, face.Uvs, face.Color, ShapeMeshData.WallSubmesh, _wallTexture == null);
            }

            var pieces = new List<ShapeMeshData>();
            for (int index = 0; index < count; index++)
            {
                if (walls[index] != null)
                {
                    walls[index].SortY = walls[index].MaxWallBaseY - BaseSortBias;
                    pieces.Add(walls[index]);
                }
                if (tops[index] != null)
                {
                    tops[index].SortY = minY + index * slice - TopSortBias - BaseSortBias;
                    pieces.Add(tops[index]);
                }
            }
            return pieces;
        }

        private List<(Vector2[] Footprint, bool Flat, float Height)> Treads(RampCorners corners)
        {
            var treads = new List<(Vector2[] Footprint, bool Flat, float Height)>();
            if (_style == RampStyle.Ramp)
            {
                treads.Add((corners.Footprint(), false, 0f));
                return treads;
            }
            for (int i = 0; i < _steps; i++)
            {
                float near = i / (float)_steps;
                float far = (i + 1) / (float)_steps;
                Vector2[] strip = Strip(corners, near, far);
                treads.Add((strip, true, StepHeight(i)));
            }
            return treads;
        }

        private List<(Vector2[] Ground, float[] Heights, Vector2[] Uvs, Vector2[] Base, Color Color)> Faces(RampCorners corners)
        {
            var faces = new List<(Vector2[] Ground, float[] Heights, Vector2[] Uvs, Vector2[] Base, Color Color)>();
            Vector2 run = corners.BottomLeft - corners.TopLeft;
            float low = LowElevation;

            if (_style == RampStyle.Stairs && corners.Direction.y < FacingCamera)
            {
                for (int i = 1; i <= _steps; i++)
                {
                    float at = i / (float)_steps;
                    Vector2 left = corners.TopLeft + run * at;
                    Vector2 right = corners.TopRight + run * at;
                    float upper = StepHeight(i - 1);
                    float lower = i == _steps ? low : StepHeight(i);
                    faces.Add(VerticalFace(left, right, lower, upper, corners.Direction));
                }
            }

            if (!_sideWalls) return faces;
            AddSide(faces, corners.TopLeft, corners.BottomLeft, corners.TopRight - corners.TopLeft, corners, low);
            AddSide(faces, corners.TopRight, corners.BottomRight, corners.TopLeft - corners.TopRight, corners, low);
            return faces;
        }

        private void AddSide(List<(Vector2[] Ground, float[] Heights, Vector2[] Uvs, Vector2[] Base, Color Color)> faces, Vector2 top, Vector2 bottom, Vector2 inward, RampCorners corners, float low)
        {
            Vector2 normal = new Vector2(bottom.y - top.y, top.x - bottom.x).normalized;
            if (Vector2.Dot(normal, inward) > 0f) normal = -normal;
            if (normal.y >= FacingCamera) return;
            if (_style == RampStyle.Ramp)
            {
                float high = HighElevation;
                Color shade = Shade(normal);
                faces.Add((new[] { top, bottom, top }, new[] { low, low, high }, new[] { new Vector2(0f, 0f), new Vector2(Vector2.Distance(top, bottom) / _wallTextureSize, 0f), new Vector2(0f, (high - low) / _wallTextureSize) }, new[] { top, bottom }, shade));
                return;
            }
            for (int i = 0; i < _steps; i++)
            {
                Vector2 near = Vector2.Lerp(top, bottom, i / (float)_steps);
                Vector2 far = Vector2.Lerp(top, bottom, (i + 1) / (float)_steps);
                faces.Add(VerticalFace(near, far, low, StepHeight(i), normal));
            }
        }

        private (Vector2[] Ground, float[] Heights, Vector2[] Uvs, Vector2[] Base, Color Color) VerticalFace(Vector2 from, Vector2 to, float bottom, float top, Vector2 normal)
        {
            float span = Vector2.Distance(from, to) / _wallTextureSize;
            float rise = (top - bottom) / _wallTextureSize;
            return (new[] { from, to, to, from }, new[] { bottom, bottom, top, top },
                new[] { new Vector2(0f, 0f), new Vector2(span, 0f), new Vector2(span, rise), new Vector2(0f, rise) },
                new[] { from, to }, Shade(normal));
        }

        private float StepHeight(int index)
        {
            return Mathf.Lerp(HighElevation, LowElevation, index / (float)_steps);
        }

        private static Vector2[] Strip(RampCorners corners, float near, float far)
        {
            Vector2 run = corners.BottomLeft - corners.TopLeft;
            var points = new[] { corners.TopLeft + run * near, corners.TopRight + run * near, corners.TopRight + run * far, corners.TopLeft + run * far };
            if (ExtrudeFormula.SignedArea(points) < 0f) System.Array.Reverse(points);
            return points;
        }

        private Color Shade(Vector2 normal)
        {
            float shade = ExtrudeFormula.WallShade(normal) * WallBaseShade;
            return new Color(_wallColor.r * shade, _wallColor.g * shade, _wallColor.b * shade, _wallColor.a);
        }

        private ShapeMeshData NewPiece()
        {
            return new ShapeMeshData(0f, 0f, _surfaceTextureSize, _wallTextureSize, _wallColor);
        }

        private void ApplyPieces(List<ShapeMeshData> pieces)
        {
            Transform visual = EnsureChild(VisualName).transform;
            _block ??= new MaterialPropertyBlock();
            if (_materials == null || _materials.Length != ShapeMeshData.SubmeshCount) _materials = new Material[ShapeMeshData.SubmeshCount];
            for (int i = 0; i < _materials.Length; i++) _materials[i] = _material != null ? _material : Parent.SharedMaterial;
            Vector3 toLocal = transform.InverseTransformPoint(Parent.transform.position);

            for (int i = 0; i < pieces.Count; i++)
            {
                string pieceName = $"Piece {i:000}";
                Transform existing = visual.Find(pieceName);
                GameObject piece = existing != null ? existing.gameObject : CreatePiece(visual, pieceName);
                piece.transform.localPosition = new Vector3(toLocal.x, toLocal.y + pieces[i].SortY, 0f);

                var filter = Ensure<MeshFilter>(piece);
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) mesh = new Mesh { name = pieceName, hideFlags = HideFlags.DontSave };
                pieces[i].ApplyTo(mesh);
                filter.sharedMesh = mesh;

                var renderer = Ensure<MeshRenderer>(piece);
                renderer.sharedMaterials = _materials;
                SetTexture(renderer, ShapeMeshData.WallSubmesh, _wallTexture);
                SetTexture(renderer, ShapeMeshData.TopSubmesh, _surfaceTexture);
                bool ground = Parent != null && Parent.DrawsAsGround;
                int order = ground ? Parent.GroundOrder : _sortingOrder;
                renderer.sortingOrder = order;
                renderer.enabled = Parent == null || !Parent.VisualBaked;
                var group = Ensure<SortingGroup>(piece);
                group.sortingOrder = order;
            }

            for (int i = visual.childCount - 1; i >= pieces.Count; i--)
            {
                GameObject extra = visual.GetChild(i).gameObject;
                var filter = extra.GetComponent<MeshFilter>();
                if (filter != null) DestroyGenerated(filter.sharedMesh);
                DestroyGenerated(extra);
            }
        }

        private void SetTexture(MeshRenderer renderer, int submesh, Texture texture)
        {
            _block.Clear();
            _block.SetTexture(MainTexProperty, texture != null ? texture : Texture2D.whiteTexture);
            _block.SetColor(TintProperty, Color.white);
            renderer.SetPropertyBlock(_block, submesh);
        }

        private void BuildCollision(RampCorners corners)
        {
            GameObject holder = EnsureChild(CollisionName);
            holder.layer = gameObject.layer;
            var collider = holder.GetComponent<PolygonCollider2D>();
            if (!_generateCollision || (Parent != null && Parent.DrawsAsGround))
            {
                DestroyGenerated(collider);
                return;
            }
            if (collider == null) collider = holder.AddComponent<PolygonCollider2D>();
            Vector2[] footprint = corners.Footprint();
            var local = new Vector2[footprint.Length];
            for (int i = 0; i < footprint.Length; i++) local[i] = holder.transform.InverseTransformPoint(Parent.transform.TransformPoint(footprint[i]));
            collider.pathCount = 1;
            collider.SetPath(0, local);
        }

        private GameObject EnsureChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(childName) { hideFlags = GeneratedFlags, layer = gameObject.layer };
            child.transform.SetParent(transform, false);
            return child;
        }

        private GameObject CreatePiece(Transform parent, string pieceName)
        {
            var piece = new GameObject(pieceName) { hideFlags = GeneratedFlags, layer = gameObject.layer };
            piece.transform.SetParent(parent, false);
            return piece;
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void DestroyGenerated(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void RebuildIfAlive()
        {
            if (this != null && isActiveAndEnabled) Rebuild();
        }
        #endregion
    }
}
