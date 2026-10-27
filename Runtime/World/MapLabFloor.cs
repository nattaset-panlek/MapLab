using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class MapLabFloor : MonoBehaviour
    {
        #region [Variables/Fields]
        public const string ObjectName = "[MapLab] Floor";
        public const string MegaPrefix = "M ";
        public const string TilePrefix = "Tile ";

        private const HideFlags GeneratedFlags = HideFlags.DontSave | HideFlags.NotEditable;
        private const string MainTexProperty = "_MainTex";
        private const string TintProperty = "_White";
        private const int SurfaceChannel = 1;

        [SerializeField] private MapLabWorldData _world;
        [SerializeField] private Material _material;
        [SerializeField] private int _sortingOrder = -101;
        [SerializeField, HideInInspector] private bool _baked;

        private MaterialPropertyBlock _block;
        #endregion

        #region [Properties]
        public MapLabWorldData World => _world;
        public Material Material => _material;
        public int SortingOrder => _sortingOrder;
        public bool Baked => _baked;
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
        #endregion

        #region [Public Methods]
        public void Setup(MapLabWorldData world, Material material, int sortingOrder)
        {
            _world = world;
            _material = material;
            _sortingOrder = sortingOrder;
            Rebuild();
        }

        public void SetBaked(bool baked)
        {
            _baked = baked;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = !baked;
        }

        public void Rebuild()
        {
            MapLabMap map = GetComponentInParent<MapLabMap>();
            var keep = new HashSet<string>();
            if (_world != null && map != null)
            {
                foreach (var mega in _world.Megas)
                {
                    keep.Add(MegaName(mega.Index));
                    BuildMega(mega, map);
                }
            }
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (!keep.Contains(child.name)) DestroyGenerated(child.gameObject);
            }
        }

        public void RebuildMega(Vector2Int index)
        {
            MapLabMap map = GetComponentInParent<MapLabMap>();
            MapLabMegaChunk mega = _world != null ? _world.Find(index) : null;
            if (mega != null && map != null)
            {
                BuildMega(mega, map);
                return;
            }
            Transform existing = transform.Find(MegaName(index));
            if (existing != null) DestroyGenerated(existing.gameObject);
        }

        public static string MegaName(Vector2Int index)
        {
            return $"{MegaPrefix}{index.x},{index.y}";
        }
        #endregion

        #region [Private Methods]
        private void BuildMega(MapLabMegaChunk mega, MapLabMap map)
        {
            float megaSize = map.MegaChunkSize;
            int size = _world.TilesPerSide;
            float tileSize = megaSize / size;
            Vector2 origin = new Vector2(mega.Index.x, mega.Index.y) * megaSize;

            GameObject megaObject = EnsureChild(transform, MegaName(mega.Index));
            megaObject.transform.position = new Vector3(origin.x, origin.y, transform.position.z);
            megaObject.transform.rotation = Quaternion.identity;

            byte TileAt(int x, int y) => mega.TileAt(x, y, size);
            HashSet<byte> used = FloorFormula.TilesUsed(TileAt, size);
            var keep = new HashSet<string>();
            int sortingLayer = SortingLayer.NameToID(map.ActorSortingLayer);
            foreach (var id in used)
            {
                MapLabFloorTile tile = _world.Palette != null ? _world.Palette.Tile(id) : null;
                string name = $"{TilePrefix}{id}";
                keep.Add(name);
                GameObject tileObject = EnsureChild(megaObject.transform, name);
                var filter = Ensure<MeshFilter>(tileObject);
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) mesh = new Mesh { name = "MapLab Floor", hideFlags = HideFlags.DontSave };
                Fill(mesh, FloorFormula.Rectangles(TileAt, size, id), tileSize, origin, tile);
                filter.sharedMesh = mesh;

                var renderer = Ensure<MeshRenderer>(tileObject);
                renderer.sharedMaterial = _material;
                renderer.sortingLayerID = sortingLayer;
                renderer.sortingOrder = _sortingOrder;
                renderer.enabled = !_baked;
                _block ??= new MaterialPropertyBlock();
                _block.Clear();
                _block.SetTexture(MainTexProperty, tile != null && tile.Texture != null ? tile.Texture : Texture2D.whiteTexture);
                _block.SetColor(TintProperty, Color.white);
                renderer.SetPropertyBlock(_block);
            }
            for (int i = megaObject.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = megaObject.transform.GetChild(i);
                if (!keep.Contains(child.name)) DestroyGenerated(child.gameObject);
            }
        }

        private static void Fill(Mesh mesh, List<RectInt> rectangles, float tileSize, Vector2 origin, MapLabFloorTile tile)
        {
            Color color = tile != null ? tile.Color : Color.magenta;
            float repeat = tile != null ? tile.TextureSize : 4f;
            var flat = new Vector2(tile != null && tile.Texture != null ? 0f : 1f, 0f);
            var vertices = new List<Vector3>(rectangles.Count * 4);
            var uvs = new List<Vector2>(rectangles.Count * 4);
            var surfaces = new List<Vector2>(rectangles.Count * 4);
            var colors = new List<Color>(rectangles.Count * 4);
            var normals = new List<Vector3>(rectangles.Count * 4);
            var triangles = new List<int>(rectangles.Count * 6);
            foreach (var rect in rectangles)
            {
                int start = vertices.Count;
                var min = new Vector2(rect.xMin * tileSize, rect.yMin * tileSize);
                var max = new Vector2(rect.xMax * tileSize, rect.yMax * tileSize);
                foreach (var corner in new[] { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) })
                {
                    vertices.Add(corner);
                    uvs.Add((origin + corner) / repeat);
                    surfaces.Add(flat);
                    colors.Add(color);
                    normals.Add(Vector3.back);
                }
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(SurfaceChannel, surfaces);
            mesh.SetColors(colors);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static GameObject EnsureChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(childName) { hideFlags = GeneratedFlags, layer = parent.gameObject.layer };
            child.transform.SetParent(parent, false);
            return child;
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void DestroyGenerated(GameObject target)
        {
            if (target == null) return;
            foreach (var filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && (filter.sharedMesh.hideFlags & HideFlags.DontSave) != 0) Discard(filter.sharedMesh);
            }
            Discard(target);
        }

        private static void Discard(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void RebuildIfAlive()
        {
            if (this != null) Rebuild();
        }
        #endregion
    }
}
