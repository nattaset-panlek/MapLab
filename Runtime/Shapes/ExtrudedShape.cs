using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Shapes
{
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    public class ExtrudedShape : PointShape
    {
        #region [Variables/Fields]
        public const string VisualName = "Visual";
        public const string CollisionName = "Collision";
        public const string ShadowName = "Shadow";

        private const string MainTexProperty = "_MainTex";
        private const string TintProperty = "_White";
        private const float HardEdgeDot = 0.87f;
        private const float TopSortBias = 0.0005f;
        private const float StackSortGap = 0.01f;
        private static readonly string[] LegacyChildNames = { "Walls", "Top" };

        [SerializeField] private ExtrudedShapeType _type = ExtrudedShapeType.Plateau;
        [SerializeField, Range(0f, 8f)] private float _height = 1.5f;
        [SerializeField, Range(0.05f, 3f)] private float _rimWidth = 0.6f;
        [SerializeField, Range(0f, 2f)] private float _borderWidth;
        [SerializeField] private Color _topColor = new Color(0.66f, 0.84f, 0.5f);
        [SerializeField] private Color _wallColor = new Color(0.72f, 0.7f, 0.8f);
        [SerializeField] private Color _voidColor = new Color(0.07f, 0.07f, 0.09f);
        [SerializeField] private Color _borderColor = new Color(0.7f, 0.68f, 0.77f);
        [SerializeField] private Texture2D _topTexture;
        [SerializeField, Min(0.1f)] private float _topTextureSize = 4f;
        [SerializeField] private Texture2D _wallTexture;
        [SerializeField, Min(0.1f)] private float _wallTextureSize = 1.5f;
        [SerializeField] private Material _material;
        [SerializeField] private int _sortingOrder = 6;
        [SerializeField] private bool _ySort = true;
        [SerializeField, Range(0.25f, 4f)] private float _sortSlice = 0.5f;
        [SerializeField, Min(0f)] private float _actorHeight = 1.2f;
        [SerializeField] private bool _walkOnTop;
        [SerializeField] private bool _castShadow = true;
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.22f);
        [SerializeField] private Vector2 _shadowDirection = new Vector2(1f, -0.25f);
        [SerializeField, Range(0f, 2f)] private float _shadowLength = 0.45f;
        [SerializeField] private int _shadowOrder = 1;
        [SerializeField, HideInInspector] private bool _shadowBaked;
        [SerializeField] private bool _generateCollision = true;
        [SerializeField, HideInInspector] private bool _drawAsGround;
        [SerializeField, HideInInspector] private int _groundOrder;
        [SerializeField, HideInInspector] private bool _groundShadow = true;

        private MaterialPropertyBlock _block;
        private Material[] _materials;
        #endregion

        #region [Properties]
        public override bool Closed => true;
        public ExtrudedShapeType Type => _type;
        public float Height => _height;
        public float RimWidth => _rimWidth;
        public bool DrawsAsGround => IsStacked ? ParentShape.DrawsAsGround : _drawAsGround;
        public int GroundOrder => IsStacked ? ParentShape.GroundOrder : _groundOrder;
        public bool GroundShadow => IsStacked ? ParentShape.GroundShadow : _groundShadow;
        public int SortingOrder => DrawsAsGround ? GroundOrder + TierIndex : _sortingOrder;
        public bool YSort => !DrawsAsGround && (_ySort || IsStacked);
        public bool HasRim => _type != ExtrudedShapeType.Plateau;
        public ExtrudedShape ParentShape => transform.parent != null ? transform.parent.GetComponent<ExtrudedShape>() : null;
        public bool IsStacked => ParentShape != null;
        public float BaseElevation => IsStacked ? ParentShape.TopElevation : 0f;
        public float TopElevation => BaseElevation + _height;
        public int TierIndex => IsStacked ? ParentShape.TierIndex + 1 : 0;
        public float ActorHeight => IsStacked ? ParentShape.ActorHeight : _actorHeight;
        public bool WalkOnTop => _walkOnTop && _type != ExtrudedShapeType.Void && !DrawsAsGround;
        public float ActorFloor => !IsStacked ? 0f : ParentShape.WalkOnTop ? ParentShape.TopElevation : ParentShape.ActorFloor;
        public bool MergesTop => YSort && !WalkOnTop && TopElevation - ActorFloor >= ActorHeight;
        public bool MergesWalls => YSort && !WalkOnTop && BaseElevation - ActorFloor >= ActorHeight;
        public bool CastsShadow => _castShadow && _height > 0f && (!IsStacked || MergesWalls || DrawsAsGround) && (!DrawsAsGround || GroundShadow);
        public int ShadowOrder => DrawsAsGround ? GroundOrder - 1 : _shadowOrder;
        public int BaseShadowOrder => _shadowOrder;
        public bool ShadowBaked => _shadowBaked;
        public float SurfaceSortBias => StackSortBias + _sortSlice + StackSortGap;
        public float BaseSortBias => StackSortBias;
        public Material SharedMaterial => _material;
        private float StackSortBias => IsStacked ? ParentShape.SurfaceSortBias : 0f;
        #endregion

        #region [Unity Methods]
        private void OnDestroy()
        {
            Transform shadow = transform.Find(ShadowName);
            if (shadow != null && shadow.TryGetComponent(out MeshFilter shadowFilter)) DestroyGenerated(shadowFilter.sharedMesh);
            Transform visual = transform.Find(VisualName);
            if (visual == null) return;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true)) DestroyGenerated(filter.sharedMesh);
        }
        #endregion

        #region [Public Methods]
        public void SetShadowBaked(bool baked)
        {
            _shadowBaked = baked;
            Rebuild();
        }

        public bool SetDrawAsGround(bool ground, int order, bool shadow = true)
        {
            if (!ground)
            {
                order = 0;
                shadow = true;
            }
            if (_drawAsGround == ground && _groundOrder == order && _groundShadow == shadow) return false;
            _drawAsGround = ground;
            _groundOrder = order;
            _groundShadow = shadow;
            Rebuild();
            return true;
        }

        public List<Vector2> Footprint()
        {
            List<Vector2> footprint = SampleOutline();
            ExtrudeFormula.MakeCounterClockwise(footprint);
            return footprint;
        }

        public override void Rebuild()
        {
            if (Points.Count < MinPointCount) return;
            RemoveLegacyChildren();

            List<Vector2> footprint = Footprint();
            List<Vector2> inner = HasRim ? ExtrudeFormula.Offset(footprint, -_rimWidth) : null;
            List<Vector2> hole = null;
            if (_type == ExtrudedShapeType.Ring)
            {
                hole = new List<Vector2>(inner);
                hole.Reverse();
            }

            List<WallQuad> walls = BuildWalls(footprint, hole);
            List<ShapeTriangle> top = BuildTop(footprint, inner);
            List<ShadowLoop> shadow = CastsShadow ? BuildShadow(footprint, hole) : null;
            List<ShapeMeshData> pieces = YSort ? Slice(footprint, walls, top, IsStacked ? shadow : null) : Single(walls, top, IsStacked && DrawsAsGround ? shadow : null);
            ApplyPieces(pieces);
            ApplyGroundShadow(IsStacked ? null : shadow);
            BuildCollision(footprint, hole);
            RebuildTiers();
        }
        #endregion

        #region [Private Methods]
        private List<WallQuad> BuildWalls(List<Vector2> footprint, List<Vector2> hole)
        {
            var quads = new List<WallQuad>();
            if (_height <= 0f) return quads;

            var runs = new List<WallRun>(ExtrudeFormula.WallRuns(footprint));
            if (hole != null) runs.AddRange(ExtrudeFormula.WallRuns(hole));
            runs.Sort((a, b) => b.AverageY.CompareTo(a.AverageY));

            float size = Mathf.Max(0.1f, _wallTextureSize);
            foreach (var run in runs)
            {
                int count = run.Points.Length;
                for (int i = 0; i + 1 < count; i++)
                {
                    Vector2 edge = ExtrudeFormula.EdgeNormal(run.Points[i], run.Points[i + 1]);
                    Vector2 fromNormal = i > 0 ? Corner(edge, ExtrudeFormula.EdgeNormal(run.Points[i - 1], run.Points[i])) : edge;
                    Vector2 toNormal = i + 2 < count ? Corner(edge, ExtrudeFormula.EdgeNormal(run.Points[i + 1], run.Points[i + 2])) : edge;
                    quads.Add(new WallQuad(
                        run.Points[i], run.Points[i + 1],
                        run.Distances[i] / size, run.Distances[i + 1] / size,
                        ExtrudeFormula.WallShade(fromNormal), ExtrudeFormula.WallShade(toNormal)));
                }
            }
            return quads;
        }

        private List<ShapeTriangle> BuildTop(List<Vector2> footprint, List<Vector2> inner)
        {
            var triangles = new List<ShapeTriangle>();
            if (_type == ExtrudedShapeType.Void) AddFill(triangles, footprint, _voidColor, true);
            if (_type == ExtrudedShapeType.Plateau) AddFill(triangles, footprint, _topColor, false);
            if (inner != null) AddBand(triangles, footprint, inner, _topColor, false);
            if (_borderWidth > 0f) AddBand(triangles, footprint, ExtrudeFormula.Offset(footprint, -_borderWidth), _borderColor, true);
            return triangles;
        }

        private List<ShadowLoop> BuildShadow(List<Vector2> footprint, List<Vector2> hole)
        {
            Vector2 cast = _shadowDirection.sqrMagnitude > 0f ? _shadowDirection.normalized * (_shadowLength * _height) : Vector2.zero;
            List<Vector2> standsOn = IsStacked ? ParentShape.Footprint() : null;
            var loops = new List<ShadowLoop>();
            foreach (var boundary in hole != null ? new[] { footprint, hole } : new[] { footprint })
            {
                Vector2[] reach = ExtrudeFormula.Translate(boundary, cast);
                if (standsOn != null) ExtrudeFormula.ClampInside(boundary, reach, standsOn);
                loops.Add(new ShadowLoop(boundary.ToArray(), reach, ExtrudeFormula.CastingEdges(boundary, cast)));
            }
            return loops;
        }

        private List<ShapeMeshData> Single(List<WallQuad> walls, List<ShapeTriangle> top, List<ShadowLoop> shadow)
        {
            ShapeMeshData piece = NewPiece();
            if (shadow != null) foreach (var loop in shadow) piece.AddShadow(loop, _shadowColor);
            foreach (var quad in walls) piece.AddWall(quad);
            foreach (var triangle in top) piece.AddPolygon(new[] { triangle.A, triangle.B, triangle.C }, triangle.Color, triangle.Flat);
            return new List<ShapeMeshData> { piece };
        }

        private List<ShapeMeshData> Slice(List<Vector2> footprint, List<WallQuad> walls, List<ShapeTriangle> top, List<ShadowLoop> shadow)
        {
            YRange(footprint, out float minY, out float maxY);
            if (MergesWalls)
            {
                List<ShapeMeshData> whole = Single(walls, top, shadow);
                whole[0].SortY = TopSortY(minY);
                return whole;
            }

            float slice = Mathf.Max(0.05f, _sortSlice);
            int count = Mathf.Max(1, Mathf.CeilToInt((maxY - minY) / slice));
            var wallPieces = new ShapeMeshData[count];
            var topPieces = new ShapeMeshData[count];

            foreach (var quad in walls)
            {
                int index = Mathf.Clamp(Mathf.FloorToInt((quad.MaxBaseY - minY) / slice), 0, count - 1);
                (wallPieces[index] ??= NewPiece()).AddWall(quad);
            }

            if (MergesTop) MergeTop(top, topPieces);
            else SliceTop(top, topPieces, minY, slice);

            float bias = StackSortBias;
            var pieces = new List<ShapeMeshData>();
            for (int i = 0; i < count; i++)
            {
                if (wallPieces[i] != null)
                {
                    wallPieces[i].SortY = (IsStacked ? wallPieces[i].MinWallBaseY : wallPieces[i].MaxWallBaseY) - bias;
                    pieces.Add(wallPieces[i]);
                }
                if (topPieces[i] != null)
                {
                    topPieces[i].SortY = topPieces[i].MinFootprintY - TopSortBias - bias;
                    pieces.Add(topPieces[i]);
                }
            }
            return pieces;
        }

        private void MergeTop(List<ShapeTriangle> top, ShapeMeshData[] topPieces)
        {
            if (top.Count == 0) return;
            ShapeMeshData piece = topPieces[0] = NewPiece();
            foreach (var triangle in top) piece.AddPolygon(new[] { triangle.A, triangle.B, triangle.C }, triangle.Color, triangle.Flat);
        }

        private void SliceTop(List<ShapeTriangle> top, ShapeMeshData[] topPieces, float minY, float slice)
        {
            int count = topPieces.Length;
            var triangle = new Vector2[3];
            foreach (var shapeTriangle in top)
            {
                triangle[0] = shapeTriangle.A;
                triangle[1] = shapeTriangle.B;
                triangle[2] = shapeTriangle.C;
                int first = Mathf.Clamp(Mathf.FloorToInt((shapeTriangle.MinY - minY) / slice), 0, count - 1);
                int last = Mathf.Clamp(Mathf.FloorToInt((shapeTriangle.MaxY - minY) / slice), 0, count - 1);
                for (int index = first; index <= last; index++)
                {
                    float bandMin = index == 0 ? float.MinValue : minY + index * slice;
                    float bandMax = index == count - 1 ? float.MaxValue : minY + (index + 1) * slice;
                    List<Vector2> clipped = ExtrudeFormula.ClipBand(triangle, bandMin, bandMax);
                    if (clipped.Count >= 3) (topPieces[index] ??= NewPiece()).AddPolygon(clipped, shapeTriangle.Color, shapeTriangle.Flat);
                }
            }
        }

        private float TopSortY(float footprintMinY)
        {
            if (!MergesWalls || !IsStacked) return footprintMinY - TopSortBias - StackSortBias;
            ExtrudedShape parent = ParentShape;
            YRange(parent.Footprint(), out float parentMinY, out _);
            return parent.TopSortY(parentMinY) - StackSortGap;
        }

        private ShapeMeshData NewPiece()
        {
            return new ShapeMeshData(BaseElevation, _height, _topTextureSize, _wallTextureSize, _wallColor);
        }

        private void ApplyPieces(List<ShapeMeshData> pieces)
        {
            Transform visual = EnsureChild(VisualName).transform;
            _block ??= new MaterialPropertyBlock();
            if (_materials == null || _materials.Length != ShapeMeshData.SubmeshCount) _materials = new Material[ShapeMeshData.SubmeshCount];
            for (int i = 0; i < _materials.Length; i++) _materials[i] = _material;

            for (int i = 0; i < pieces.Count; i++)
            {
                string pieceName = $"Piece {i:000}";
                Transform existing = visual.Find(pieceName);
                GameObject piece = existing != null ? existing.gameObject : CreatePiece(visual, pieceName);
                piece.transform.localPosition = new Vector3(0f, pieces[i].SortY, 0f);

                var filter = Ensure<MeshFilter>(piece);
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) mesh = new Mesh { name = pieceName, hideFlags = HideFlags.DontSave };
                pieces[i].ApplyTo(mesh);
                filter.sharedMesh = mesh;

                var renderer = Ensure<MeshRenderer>(piece);
                renderer.sharedMaterials = _materials;
                renderer.enabled = !VisualBaked;
                ApplyTextures(renderer);

                var group = piece.GetComponent<SortingGroup>();
                if (YSort)
                {
                    if (group == null) group = piece.AddComponent<SortingGroup>();
                    group.sortingOrder = SortingOrder;
                    renderer.sortingOrder = SortingOrder;
                }
                else
                {
                    DestroyGenerated(group);
                    renderer.sortingOrder = SortingOrder;
                }
            }

            for (int i = visual.childCount - 1; i >= pieces.Count; i--)
            {
                GameObject extra = visual.GetChild(i).gameObject;
                var filter = extra.GetComponent<MeshFilter>();
                if (filter != null) DestroyGenerated(filter.sharedMesh);
                DestroyGenerated(extra);
            }
        }

        private void ApplyGroundShadow(List<ShadowLoop> shadow)
        {
            Transform existing = transform.Find(ShadowName);
            if (shadow == null)
            {
                if (existing == null) return;
                if (existing.TryGetComponent(out MeshFilter oldFilter)) DestroyGenerated(oldFilter.sharedMesh);
                DestroyGenerated(existing.gameObject);
                return;
            }

            GameObject holder = EnsureChild(ShadowName);
            holder.layer = gameObject.layer;
            var data = new ShapeMeshData(BaseElevation, 0f, _topTextureSize, _wallTextureSize, _wallColor);
            foreach (var loop in shadow) data.AddShadow(loop, _shadowColor);

            var filter = Ensure<MeshFilter>(holder);
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) mesh = new Mesh { name = ShadowName, hideFlags = HideFlags.DontSave };
            data.ApplyTo(mesh);
            filter.sharedMesh = mesh;

            var renderer = Ensure<MeshRenderer>(holder);
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = ShadowOrder;
            renderer.enabled = !_shadowBaked;
        }

        private GameObject CreatePiece(Transform parent, string pieceName)
        {
            var piece = new GameObject(pieceName) { hideFlags = GeneratedFlags, layer = gameObject.layer };
            piece.transform.SetParent(parent, false);
            return piece;
        }

        private void ApplyTextures(MeshRenderer renderer)
        {
            SetTexture(renderer, ShapeMeshData.WallSubmesh, _wallTexture);
            SetTexture(renderer, ShapeMeshData.TopSubmesh, _topTexture);
        }

        private void SetTexture(MeshRenderer renderer, int submesh, Texture texture)
        {
            _block.Clear();
            _block.SetTexture(MainTexProperty, texture != null ? texture : Texture2D.whiteTexture);
            _block.SetColor(TintProperty, Color.white);
            renderer.SetPropertyBlock(_block, submesh);
        }

        private void BuildCollision(List<Vector2> footprint, List<Vector2> hole)
        {
            GameObject holder = EnsureChild(CollisionName);
            holder.layer = gameObject.layer;
            var colliders = new List<PolygonCollider2D>(holder.GetComponents<PolygonCollider2D>());
            int needed = _generateCollision && !IsStacked && !DrawsAsGround ? 1 : 0;
            while (colliders.Count < needed) colliders.Add(holder.AddComponent<PolygonCollider2D>());
            for (int i = colliders.Count - 1; i >= needed; i--)
            {
                DestroyGenerated(colliders[i]);
                colliders.RemoveAt(i);
            }
            if (needed == 0) return;

            PolygonCollider2D solid = colliders[0];
            solid.pathCount = hole != null ? 2 : 1;
            solid.SetPath(0, footprint);
            if (hole != null) solid.SetPath(1, hole);
        }

        private void RebuildTiers()
        {
            foreach (Transform child in transform)
            {
                var tier = child.GetComponent<ExtrudedShape>();
                if (tier != null && tier.isActiveAndEnabled) tier.Rebuild();
                var ramp = child.GetComponent<RampShape>();
                if (ramp != null && ramp.isActiveAndEnabled) ramp.Rebuild();
            }
        }

        private void RemoveLegacyChildren()
        {
            foreach (var legacyName in LegacyChildNames)
            {
                Transform legacy = transform.Find(legacyName);
                if (legacy == null) continue;
                var filter = legacy.GetComponent<MeshFilter>();
                if (filter != null) DestroyGenerated(filter.sharedMesh);
                DestroyGenerated(legacy.gameObject);
            }
        }

        private static void AddFill(List<ShapeTriangle> triangles, List<Vector2> polygon, Color color, bool flat)
        {
            List<int> indices = ExtrudeFormula.Triangulate(polygon);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                triangles.Add(new ShapeTriangle(polygon[indices[i]], polygon[indices[i + 1]], polygon[indices[i + 2]], color, flat));
            }
        }

        private static void AddBand(List<ShapeTriangle> triangles, List<Vector2> outer, List<Vector2> inner, Color color, bool flat)
        {
            int count = outer.Count;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(new ShapeTriangle(outer[i], outer[next], inner[next], color, flat));
                triangles.Add(new ShapeTriangle(outer[i], inner[next], inner[i], color, flat));
            }
        }

        private static void YRange(List<Vector2> points, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            foreach (var point in points)
            {
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
        }

        private static Vector2 Corner(Vector2 own, Vector2 neighbour)
        {
            return Vector2.Dot(own, neighbour) >= HardEdgeDot ? (own + neighbour).normalized : own;
        }
        #endregion
    }
}
