using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [Serializable]
    public class MapLabFloorTile
    {
        #region [Variables/Fields]
        [SerializeField] private string _name = "Tile";
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private Texture2D _texture;
        [SerializeField, Min(0.1f)] private float _textureSize = 4f;
        #endregion

        #region [Properties]
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public Color Color
        {
            get => _color;
            set => _color = value;
        }

        public Texture2D Texture
        {
            get => _texture;
            set => _texture = value;
        }

        public float TextureSize
        {
            get => _textureSize;
            set => _textureSize = Mathf.Max(0.1f, value);
        }
        #endregion

        #region [Public Methods]
        public MapLabFloorTile()
        {
        }

        public MapLabFloorTile(string name, Color color)
        {
            _name = name;
            _color = color;
        }
        #endregion
    }

    [CreateAssetMenu(menuName = "MapLab/Floor Palette", fileName = "MapLab Floor Palette")]
    public class MapLabFloorPalette : ScriptableObject
    {
        #region [Variables/Fields]
        public const int MaxTiles = 254;

        [SerializeField] private List<MapLabFloorTile> _tiles = new List<MapLabFloorTile>();
        #endregion

        #region [Properties]
        public List<MapLabFloorTile> Tiles => _tiles;
        public int Count => _tiles.Count;
        #endregion

        #region [Public Methods]
        public MapLabFloorTile Tile(byte id)
        {
            return id >= 1 && id <= _tiles.Count ? _tiles[id - 1] : null;
        }

        public string NameOf(byte id)
        {
            MapLabFloorTile tile = Tile(id);
            return tile != null ? tile.Name : "None";
        }

        public byte IdOf(string name)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (string.Equals(_tiles[i].Name, name, StringComparison.OrdinalIgnoreCase)) return (byte)(i + 1);
            }
            return 0;
        }
        #endregion
    }
}
