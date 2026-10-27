using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class MapLabGroundProp : MonoBehaviour
    {
        #region [Variables/Fields]
        public const int MaxSpread = 3;

        [Serializable]
        private struct RendererState
        {
            [SerializeField] private Renderer _renderer;
            [SerializeField] private int _order;
            [SerializeField] private bool _shadow;

            public Renderer Renderer => _renderer;
            public int Order => _order;
            public bool Shadow => _shadow;

            public RendererState(Renderer renderer, bool shadow)
            {
                _renderer = renderer;
                _order = renderer.sortingOrder;
                _shadow = shadow;
            }
        }

        [Serializable]
        private struct GroupState
        {
            [SerializeField] private SortingGroup _group;
            [SerializeField] private int _order;

            public SortingGroup Group => _group;
            public int Order => _order;

            public GroupState(SortingGroup group)
            {
                _group = group;
                _order = group.sortingOrder;
            }
        }

        [Serializable]
        private struct ColliderState
        {
            [SerializeField] private Collider2D _collider;
            [SerializeField] private bool _enabled;

            public Collider2D Collider => _collider;
            public bool Enabled => _enabled;

            public ColliderState(Collider2D collider)
            {
                _collider = collider;
                _enabled = collider.enabled;
            }
        }

        [SerializeField] private string _layer;
        [SerializeField] private int _order;
        [SerializeField] private bool _shadowsHidden;
        [SerializeField] private List<RendererState> _renderers = new List<RendererState>();
        [SerializeField] private List<GroupState> _groups = new List<GroupState>();
        [SerializeField] private List<ColliderState> _colliders = new List<ColliderState>();
        #endregion

        #region [Properties]
        public string Layer => _layer;
        public int Order => _order;
        public int RendererCount => _renderers.Count + _groups.Count;
        public bool ShadowsHidden => _shadowsHidden;
        public int ShadowCount => _renderers.FindAll(state => state.Shadow).Count;
        #endregion

        #region [Public Methods]
        public bool Apply(string layer, int order, bool castShadows = true, Func<Renderer, bool> isShadow = null)
        {
            bool captured = _renderers.Count == 0 && _groups.Count == 0 && _colliders.Count == 0;
            if (captured) Capture(isShadow);
            bool changed = captured || _layer != layer || _order != order || _shadowsHidden == castShadows;
            _layer = layer;
            _order = order;
            int lowest = int.MaxValue;
            foreach (var state in _groups) lowest = Mathf.Min(lowest, state.Order);
            foreach (var state in _renderers) lowest = Mathf.Min(lowest, state.Order);
            foreach (var state in _groups)
            {
                if (state.Group != null) state.Group.sortingOrder = order + Spread(state.Order, lowest);
            }
            foreach (var state in _renderers)
            {
                if (state.Renderer != null) state.Renderer.sortingOrder = order + Spread(state.Order, lowest);
            }
            foreach (var state in _colliders)
            {
                if (state.Collider != null) state.Collider.enabled = false;
            }
            if (!castShadows || _shadowsHidden) ShowShadows(castShadows);
            _shadowsHidden = !castShadows;
            return changed;
        }

        public void Restore()
        {
            if (_shadowsHidden) ShowShadows(true);
            _shadowsHidden = false;
            foreach (var state in _groups)
            {
                if (state.Group != null) state.Group.sortingOrder = state.Order;
            }
            foreach (var state in _renderers)
            {
                if (state.Renderer != null) state.Renderer.sortingOrder = state.Order;
            }
            foreach (var state in _colliders)
            {
                if (state.Collider != null) state.Collider.enabled = state.Enabled;
            }
            _renderers.Clear();
            _groups.Clear();
            _colliders.Clear();
        }
        #endregion

        #region [Private Methods]
        private void Capture(Func<Renderer, bool> isShadow)
        {
            foreach (var group in GetComponentsInChildren<SortingGroup>(true)) _groups.Add(new GroupState(group));
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<SortingGroup>(true) == null) _renderers.Add(new RendererState(renderer, isShadow != null && isShadow(renderer)));
            }
            foreach (var collider in GetComponentsInChildren<Collider2D>(true)) _colliders.Add(new ColliderState(collider));
        }

        private void ShowShadows(bool show)
        {
            foreach (var state in _renderers)
            {
                if (state.Shadow && state.Renderer != null) state.Renderer.enabled = show;
            }
        }

        private static int Spread(int original, int lowest)
        {
            return Mathf.Clamp(original - lowest, 0, MaxSpread);
        }
        #endregion
    }
}
