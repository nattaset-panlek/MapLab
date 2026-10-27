using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Shapes
{
    public abstract class PointShape : MonoBehaviour
    {
        #region [Variables/Fields]
        protected const HideFlags GeneratedFlags = HideFlags.DontSave | HideFlags.NotEditable;

        [SerializeField] private List<Vector2> _points = new List<Vector2>
        {
            new Vector2(-3f, -1.5f), new Vector2(0f, -2.2f), new Vector2(3f, -1.5f),
            new Vector2(3.2f, 1.5f), new Vector2(0f, 2.2f), new Vector2(-3.2f, 1.5f)
        };
        [SerializeField] private List<bool> _sharp = new List<bool>();
        [SerializeField, Range(1, 24)] private int _smoothness = 10;
        [SerializeField, HideInInspector] private bool _visualBaked;
        [SerializeField, HideInInspector] private MapLabPreset _preset;
        #endregion

        #region [Properties]
        public IReadOnlyList<Vector2> Points => _points;
        public IReadOnlyList<bool> Sharp => _sharp;
        public int Smoothness => _smoothness;
        public bool VisualBaked => _visualBaked;

        public MapLabPreset Preset
        {
            get => _preset;
            set => _preset = value;
        }
        public abstract bool Closed { get; }
        public int MinPointCount => Closed ? 3 : 2;
        #endregion

        #region [Unity Methods]
        protected virtual void OnEnable()
        {
            Rebuild();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildIfAlive;
            UnityEditor.EditorApplication.delayCall += RebuildIfAlive;
        }
#endif
        #endregion

        #region [Public Methods]
        public abstract void Rebuild();

        public bool IsSharp(int index)
        {
            return ExtrudeFormula.IsSharp(_sharp, index);
        }

        public void SetVisualBaked(bool baked)
        {
            _visualBaked = baked;
            Rebuild();
        }

        public void SetPoints(IReadOnlyList<Vector2> points, IReadOnlyList<bool> sharp = null)
        {
            _points.Clear();
            _sharp.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                _sharp.Add(sharp != null && i < sharp.Count && sharp[i]);
            }
            Rebuild();
        }

        public void SetPoint(int index, Vector2 point)
        {
            if (index < 0 || index >= _points.Count) return;
            _points[index] = point;
            Rebuild();
        }

        public void OffsetPoints(Vector2 offset)
        {
            for (int i = 0; i < _points.Count; i++) _points[i] += offset;
            Rebuild();
        }

        public Vector2 PointsCenter()
        {
            if (_points.Count == 0) return Vector2.zero;
            Vector2 min = _points[0], max = _points[0];
            foreach (var point in _points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return (min + max) * 0.5f;
        }

        public void SetSharp(int index, bool sharp)
        {
            if (index < 0 || index >= _points.Count) return;
            while (_sharp.Count < _points.Count) _sharp.Add(false);
            _sharp[index] = sharp;
            Rebuild();
        }

        public void InsertPoint(int index, Vector2 point)
        {
            index = Mathf.Clamp(index, 0, _points.Count);
            while (_sharp.Count < _points.Count) _sharp.Add(false);
            _points.Insert(index, point);
            _sharp.Insert(index, false);
            Rebuild();
        }

        public void RemovePoint(int index)
        {
            if (_points.Count <= MinPointCount || index < 0 || index >= _points.Count) return;
            _points.RemoveAt(index);
            if (index < _sharp.Count) _sharp.RemoveAt(index);
            Rebuild();
        }

        public List<Vector2> SampleOutline()
        {
            return ExtrudeFormula.Sample(_points, _sharp, _smoothness, Closed);
        }

        public Vector2 SpanMidpoint(int span)
        {
            return ExtrudeFormula.Evaluate(_points, _sharp, Closed, span, 0.5f);
        }
        #endregion

        #region [Private Methods]
        protected GameObject EnsureChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(childName) { hideFlags = GeneratedFlags };
            child.transform.SetParent(transform, false);
            return child;
        }

        protected static T Ensure<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        protected static void DestroyGenerated(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void RebuildIfAlive()
        {
            if (this != null && isActiveAndEnabled) Rebuild();
        }
        #endregion
    }
}
