using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabChunkLayer : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private string _layerName;
        [SerializeField] private string _paintName;
        [SerializeField, Min(0.5f)] private float _chunkSize = 16f;
        [SerializeField] private int _pixelsPerUnit = 64;
        [SerializeField] private List<Vector2Int> _indices = new List<Vector2Int>();
        [SerializeField] private List<SpriteRenderer> _chunks = new List<SpriteRenderer>();
        #endregion

        #region [Properties]
        public string LayerName => _layerName;
        public string PaintName => _paintName;
        public bool IsPaint => !string.IsNullOrEmpty(_paintName);
        public string StackName => IsPaint ? _paintName : _layerName;
        public float ChunkSize => _chunkSize;
        public int PixelsPerUnit => _pixelsPerUnit;
        public IReadOnlyList<Vector2Int> Indices => _indices;
        public IReadOnlyList<SpriteRenderer> Chunks => _chunks;
        #endregion

        #region [Public Methods]
        public void Setup(string layerName, string paintName, float chunkSize, int pixelsPerUnit, List<Vector2Int> indices, List<SpriteRenderer> chunks)
        {
            _layerName = layerName;
            _paintName = paintName;
            _chunkSize = chunkSize;
            _pixelsPerUnit = pixelsPerUnit;
            _indices = new List<Vector2Int>(indices);
            _chunks = new List<SpriteRenderer>(chunks);
        }

        public void Rename(string layerName, string paintName)
        {
            _layerName = layerName;
            _paintName = paintName;
        }

        public void SetSorting(int sortingLayerId, int sortingOrder)
        {
            foreach (var chunk in _chunks)
            {
                if (chunk == null) continue;
                chunk.sortingLayerID = sortingLayerId;
                chunk.sortingOrder = sortingOrder;
            }
        }

        public int Show(RectInt range)
        {
            int active = 0;
            for (int i = 0; i < _chunks.Count && i < _indices.Count; i++)
            {
                if (_chunks[i] == null) continue;
                bool inside = range.Contains(_indices[i]);
                if (_chunks[i].enabled != inside) _chunks[i].enabled = inside;
                if (inside) active++;
            }
            return active;
        }

        public int ShowAll()
        {
            int active = 0;
            foreach (var chunk in _chunks)
            {
                if (chunk == null) continue;
                chunk.enabled = true;
                active++;
            }
            return active;
        }

        public static RectInt ChunkRange(Rect view, float chunkSize)
        {
            int xMin = Mathf.FloorToInt(view.xMin / chunkSize);
            int yMin = Mathf.FloorToInt(view.yMin / chunkSize);
            int xMax = Mathf.CeilToInt(view.xMax / chunkSize);
            int yMax = Mathf.CeilToInt(view.yMax / chunkSize);
            return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
        }

        public static Rect ChunkRect(Vector2Int index, float chunkSize)
        {
            return new Rect(index.x * chunkSize, index.y * chunkSize, chunkSize, chunkSize);
        }
        #endregion
    }
}
