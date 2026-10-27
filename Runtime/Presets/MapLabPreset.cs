using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab
{
    public enum MapLabPresetKind
    {
        Plateau,
        Ring,
        Box,
        Flat,
        Void,
        Path,
        Environment
    }

    [Serializable]
    public class MapLabPresetLayer
    {
        #region [Variables/Fields]
        [SerializeField] private string _name = "Ground";
        [SerializeField] private MapLabBakeKinds _kinds = MapLabBakeKinds.All;
        [SerializeField] private bool _sorted;
        [SerializeField] private bool _decorate;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private List<string> _sets = new List<string>();
        #endregion

        #region [Properties]
        public string Name => _name;
        public MapLabBakeKinds Kinds => _kinds;
        public bool Sorted => _sorted;
        public bool Decorate => _decorate;
        public bool CastShadows => _castShadows;
        public IReadOnlyList<string> Sets => _sets;
        #endregion

        #region [Public Methods]
        public MapLabPresetLayer()
        {
        }

        public MapLabPresetLayer(string name, MapLabBakeKinds kinds, bool sorted, bool decorate = false, bool castShadows = true, IEnumerable<string> sets = null)
        {
            _name = name;
            _kinds = kinds;
            _sorted = sorted;
            _decorate = decorate;
            _castShadows = castShadows;
            if (sets != null) _sets.AddRange(sets);
        }
        #endregion
    }

    public class MapLabPreset : ScriptableObject
    {
        #region [Variables/Fields]
        [SerializeField] private MapLabPresetKind _kind = MapLabPresetKind.Plateau;
        [SerializeField] private Object _values;
        [SerializeField] private bool _syncUsers = true;
        [SerializeField] private List<MapLabPresetLayer> _layers = new List<MapLabPresetLayer>();
        [SerializeField] private MapLabFloorPalette _palette;
        [SerializeField] private int _tilesPerSide = MegaChunkFormula.DefaultTilesPerSide;
        #endregion

        #region [Properties]
        public MapLabPresetKind Kind
        {
            get => _kind;
            set => _kind = value;
        }

        public Object Values
        {
            get => _values;
            set => _values = value;
        }

        public bool SyncUsers
        {
            get => _syncUsers;
            set => _syncUsers = value;
        }

        public List<MapLabPresetLayer> Layers => _layers;

        public MapLabFloorPalette Palette
        {
            get => _palette;
            set => _palette = value;
        }

        public int TilesPerSide
        {
            get => _tilesPerSide;
            set => _tilesPerSide = value;
        }

        public bool IsEnvironment => _kind == MapLabPresetKind.Environment;
        #endregion
    }
}
