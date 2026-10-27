using System;
using UnityEngine;

namespace MapLab.Elevation
{
    [DisallowMultipleComponent]
    public class ElevationBody : MonoBehaviour
    {
        #region [Variables/Fields]
        public const string SortAnchorName = "Sort Anchor";
        public const string VisualName = "Visual";

        [SerializeField] private TextAsset _elevationData;
        [SerializeField] private Transform _sortAnchor;
        [SerializeField] private Transform _visual;
        [SerializeField] private ElevationCollision _collision = ElevationCollision.Grid;
        [SerializeField] private bool _syncWhenMoved;

        private ElevationMap _map;
        private int _region = ElevationMap.Outside;
        private int _level;
        private float _height;
        private float _sortBias;

        public event Action<int, int> OnLevelChanged;
        public event Action<int> OnRampEntered;
        public event Action<int> OnRampExited;
        public event Action<float> OnHeightChanged;
        #endregion

        #region [Properties]
        public ElevationMap Map => _map ??= ElevationMap.Load(_elevationData);
        public Vector2 Ground => transform.position;
        public int Region => _region;
        public int Level => _level;
        public float Height => _height;
        public float SortBias => _sortBias;
        public bool IsOnRamp => Map != null && Map.IsRamp(_region);
        public ElevationCollision Collision
        {
            get => _collision;
            set => _collision = value;
        }
        #endregion

        #region [Unity Methods]
        private void Reset()
        {
            _sortAnchor = transform.Find(SortAnchorName);
            _visual = _sortAnchor != null ? _sortAnchor.Find(VisualName) : transform.Find(VisualName);
        }

        private void OnEnable()
        {
            Sync();
        }

        private void LateUpdate()
        {
            if (!_syncWhenMoved || !transform.hasChanged) return;
            transform.hasChanged = false;
            Sync();
        }
        #endregion

        #region [Public Methods]
        public void SetMap(ElevationMap map)
        {
            _map = map;
            Sync();
        }

        public void SetMap(TextAsset file)
        {
            _elevationData = file;
            _map = null;
            Sync();
        }

        public void SetParts(Transform sortAnchor, Transform visual)
        {
            _sortAnchor = sortAnchor;
            _visual = visual;
            Sync();
        }

        public Vector2 Move(Vector2 delta)
        {
            Vector2 from = Ground;
            Vector2 to = from + delta;
            if (_collision == ElevationCollision.Grid && Map != null) to = Map.Constrain(from, to);
            Place(to);
            return to - from;
        }

        public void Teleport(Vector2 ground)
        {
            Place(ground);
        }

        public bool CanStepTo(Vector2 ground)
        {
            return Map == null || Map.CanStep(Ground, ground);
        }

        public void Sync()
        {
            ElevationMap map = Map;
            if (map == null)
            {
                ApplyVisual();
                return;
            }

            int region = map.RegionAt(Ground);
            bool walkable = map.IsWalkable(region);
            if (walkable && region != _region)
            {
                int previous = _region;
                _region = region;
                if (map.IsRamp(previous)) OnRampExited?.Invoke(previous);
                if (map.IsRamp(region)) OnRampEntered?.Invoke(region);
                else if (region != _level || previous == ElevationMap.Outside)
                {
                    int old = _level;
                    _level = region;
                    if (old != region) OnLevelChanged?.Invoke(old, region);
                }
            }

            float height = walkable ? map.HeightAt(region, Ground) : region == ElevationMap.Outside ? 0f : _height;
            if (!Mathf.Approximately(height, _height))
            {
                _height = height;
                OnHeightChanged?.Invoke(height);
            }
            _sortBias = map.SortBias(map.IsWalkable(_region) ? _region : _level);
            ApplyVisual();
        }
        #endregion

        #region [Private Methods]
        private void Place(Vector2 ground)
        {
            transform.position = new Vector3(ground.x, ground.y, transform.position.z);
            transform.hasChanged = false;
            Sync();
        }

        private void ApplyVisual()
        {
            if (_sortAnchor != null)
            {
                Vector3 anchor = _sortAnchor.localPosition;
                _sortAnchor.localPosition = new Vector3(anchor.x, -_sortBias, anchor.z);
            }
            if (_visual == null) return;
            bool underAnchor = _sortAnchor != null && _visual.IsChildOf(_sortAnchor);
            Vector3 visual = _visual.localPosition;
            _visual.localPosition = new Vector3(visual.x, _height + (underAnchor ? _sortBias : 0f), visual.z);
        }
        #endregion
    }
}
