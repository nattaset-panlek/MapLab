using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [Serializable]
    public class MapLabMegaChunk
    {
        #region [Variables/Fields]
        [SerializeField] private Vector2Int _index;
        [SerializeField] private byte _baseTile;
        [SerializeField] private byte[] _cells = new byte[0];
        [SerializeField] private int _version;
        [SerializeField] private int _bakedVersion = -1;
        #endregion

        #region [Properties]
        public Vector2Int Index => _index;
        public byte BaseTile => _baseTile;
        public int Version => _version;
        public int BakedVersion => _bakedVersion;
        public bool IsPainted => _cells != null && _cells.Length > 0;
        public bool BakedCurrent => _bakedVersion == _version;
        #endregion

        #region [Public Methods]
        public MapLabMegaChunk(Vector2Int index, byte baseTile)
        {
            _index = index;
            _baseTile = baseTile;
        }

        public byte Cell(int x, int y, int size)
        {
            if (_cells == null || _cells.Length != size * size || x < 0 || y < 0 || x >= size || y >= size) return 0;
            return _cells[y * size + x];
        }

        public byte TileAt(int x, int y, int size)
        {
            byte cell = Cell(x, y, size);
            return cell != 0 ? cell : _baseTile;
        }

        public bool SetCell(int x, int y, int size, byte value)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return false;
            if (_cells == null || _cells.Length != size * size)
            {
                if (value == 0) return false;
                _cells = new byte[size * size];
            }
            if (_cells[y * size + x] == value) return false;
            _cells[y * size + x] = value;
            return true;
        }

        public bool SetBase(byte tile)
        {
            if (_baseTile == tile) return false;
            _baseTile = tile;
            return true;
        }

        public bool ClearCells()
        {
            if (!IsPainted) return false;
            _cells = new byte[0];
            return true;
        }

        public int PaintedCount()
        {
            if (!IsPainted) return 0;
            int count = 0;
            foreach (var cell in _cells)
            {
                if (cell != 0) count++;
            }
            return count;
        }

        public void Touch()
        {
            _version++;
        }

        public void MarkBaked()
        {
            _bakedVersion = _version;
        }
        #endregion
    }

    public class MapLabWorldData : ScriptableObject
    {
        #region [Variables/Fields]
        [SerializeField, Range(8, 256)] private int _tilesPerSide = MegaChunkFormula.DefaultTilesPerSide;
        [SerializeField] private MapLabFloorPalette _palette;
        [SerializeField] private List<MapLabMegaChunk> _megas = new List<MapLabMegaChunk>();
        [SerializeField] private int _version;
        #endregion

        #region [Properties]
        public int TilesPerSide
        {
            get => Mathf.Clamp(_tilesPerSide, 8, 256);
            set => _tilesPerSide = Mathf.Clamp(value, 8, 256);
        }

        public MapLabFloorPalette Palette
        {
            get => _palette;
            set => _palette = value;
        }

        public IReadOnlyList<MapLabMegaChunk> Megas => _megas;
        public int Count => _megas.Count;
        public int Version => _version;
        #endregion

        #region [Public Methods]
        public MapLabMegaChunk Find(Vector2Int index)
        {
            foreach (var mega in _megas)
            {
                if (mega.Index == index) return mega;
            }
            return null;
        }

        public bool Contains(Vector2Int index)
        {
            return Find(index) != null;
        }

        public MapLabMegaChunk Add(Vector2Int index, byte baseTile)
        {
            MapLabMegaChunk existing = Find(index);
            if (existing != null) return existing;
            var mega = new MapLabMegaChunk(index, baseTile);
            _megas.Add(mega);
            Touch();
            return mega;
        }

        public bool Remove(Vector2Int index)
        {
            int removed = _megas.RemoveAll(mega => mega.Index == index);
            if (removed > 0) Touch();
            return removed > 0;
        }

        public bool IsEdge(Vector2Int index)
        {
            if (Contains(index)) return false;
            return Contains(index + Vector2Int.right) || Contains(index + Vector2Int.left) || Contains(index + Vector2Int.up) || Contains(index + Vector2Int.down);
        }

        public RectInt Extent()
        {
            var extent = new RectInt(0, 0, 0, 0);
            foreach (var mega in _megas) extent = MegaChunkFormula.Union(extent, new RectInt(mega.Index, Vector2Int.one));
            return extent;
        }

        public byte TileAt(Vector2Int megaIndex, int x, int y)
        {
            MapLabMegaChunk mega = Find(megaIndex);
            return mega != null ? mega.TileAt(x, y, TilesPerSide) : (byte)0;
        }

        public void Touch()
        {
            _version++;
        }
        #endregion
    }
}
