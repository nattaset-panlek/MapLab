using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [Flags]
    public enum MapLabBakeKinds
    {
        None = 0,
        Sprites = 1,
        Paths = 2,
        FlatShapes = 4,
        ShapeShadows = 8,
        PropShadows = 16,
        SortedShapes = 32,
        Floor = 64,
        PulledShapes = 128,
        PulledProps = 256,
        All = Sprites | Paths | FlatShapes | ShapeShadows | PropShadows | Floor
    }

    [Serializable]
    public class MapLabBakeLayerSettings
    {
        #region [Variables/Fields]
        [SerializeField] private string _name = "Ground";
        [SerializeField] private MapLabBakeKinds _kinds = MapLabBakeKinds.All;
        [SerializeField] private List<MapLabSet> _sets = new List<MapLabSet>();
        [SerializeField] private bool _sorted;
        [SerializeField] private bool _locked;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private List<Vector2Int> _changedUnderPaint = new List<Vector2Int>();
        #endregion

        #region [Properties]
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public MapLabBakeKinds Kinds
        {
            get => _kinds;
            set => _kinds = value;
        }

        public bool Sorted
        {
            get => _sorted;
            set => _sorted = value;
        }

        public bool Locked
        {
            get => _locked;
            set => _locked = value;
        }

        public bool CastShadows
        {
            get => _castShadows;
            set => _castShadows = value;
        }

        public List<MapLabSet> Sets => _sets;
        public IReadOnlyList<Vector2Int> ChangedUnderPaint => _changedUnderPaint;
        #endregion

        #region [Public Methods]
        public MapLabBakeLayerSettings()
        {
        }

        public MapLabBakeLayerSettings(string name, MapLabBakeKinds kinds)
        {
            _name = name;
            _kinds = kinds;
        }

        public void SetChangedUnderPaint(IEnumerable<Vector2Int> chunks)
        {
            _changedUnderPaint = new List<Vector2Int>(chunks);
        }
        #endregion
    }
}
