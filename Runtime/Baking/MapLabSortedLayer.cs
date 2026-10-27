using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabSortedLayer : MonoBehaviour
    {
        #region [Variables/Fields]
        [Serializable]
        public struct Piece
        {
            public GameObject Root;
            public Rect World;

            public Piece(GameObject root, Rect world)
            {
                Root = root;
                World = world;
            }
        }

        [SerializeField] private string _layerName;
        [SerializeField] private int _pixelsPerUnit = 64;
        [SerializeField] private List<Piece> _pieces = new List<Piece>();

        private SpriteRenderer[][] _renderers;
        #endregion

        #region [Properties]
        public string LayerName => _layerName;
        public int PixelsPerUnit => _pixelsPerUnit;
        public IReadOnlyList<Piece> Pieces => _pieces;
        #endregion

        #region [Public Methods]
        public void Setup(string layerName, int pixelsPerUnit, List<Piece> pieces)
        {
            _layerName = layerName;
            _pixelsPerUnit = pixelsPerUnit;
            _pieces = new List<Piece>(pieces);
        }

        public void Rename(string layerName)
        {
            _layerName = layerName;
        }

        public void Refresh()
        {
            _renderers = null;
        }

        public int Show(Rect view)
        {
            Cache();
            int active = 0;
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i].Root == null) continue;
                bool inside = _pieces[i].World.Overlaps(view);
                Toggle(_renderers[i], inside);
                if (inside) active++;
            }
            return active;
        }

        public int ShowAll()
        {
            Cache();
            int active = 0;
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i].Root == null) continue;
                Toggle(_renderers[i], true);
                active++;
            }
            return active;
        }
        #endregion

        #region [Private Methods]
        private void Cache()
        {
            if (_renderers != null && _renderers.Length == _pieces.Count) return;
            _renderers = new SpriteRenderer[_pieces.Count][];
            for (int i = 0; i < _pieces.Count; i++)
            {
                _renderers[i] = _pieces[i].Root != null ? _pieces[i].Root.GetComponentsInChildren<SpriteRenderer>(true) : Array.Empty<SpriteRenderer>();
            }
        }

        private static void Toggle(SpriteRenderer[] renderers, bool visible)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.enabled != visible) renderer.enabled = visible;
            }
        }
        #endregion
    }
}
