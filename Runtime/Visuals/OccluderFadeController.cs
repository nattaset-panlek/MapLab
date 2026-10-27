using System.Collections;
using UnityEngine;

namespace MapLab.Visuals
{
    [RequireComponent(typeof(Collider2D))]
    public class OccluderFadeController : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private SpriteRenderer[] _renderers = new SpriteRenderer[0];
        [SerializeField] private LayerMask _viewerLayers = ~0;
        [SerializeField, Range(0f, 1f)] private float _fadedAlpha = 0.35f;
        [SerializeField, Min(0.01f)] private float _fadeDuration = 0.25f;

        private float[] _baseAlphas = new float[0];
        private float _currentFactor = 1f;
        private int _insideCount;
        private Coroutine _fadeRoutine;
        #endregion

        #region [Properties]
        public bool IsFaded => _insideCount > 0;
        #endregion

        #region [Unity Methods]
        private void Reset()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            GetComponent<Collider2D>().isTrigger = true;
        }

        private void Awake()
        {
            _baseAlphas = new float[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _baseAlphas[i] = _renderers[i] != null ? _renderers[i].color.a : 1f;
            }
        }

        private void OnDisable()
        {
            _insideCount = 0;
            StopFade();
            ApplyFactor(1f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsViewer(other)) return;
            _insideCount++;
            if (_insideCount == 1) FadeTo(_fadedAlpha);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (_insideCount == 0 || !IsViewer(other)) return;
            _insideCount--;
            if (_insideCount == 0) FadeTo(1f);
        }
        #endregion

        #region [Private Methods]
        private bool IsViewer(Collider2D other)
        {
            return (_viewerLayers.value & (1 << other.gameObject.layer)) != 0;
        }

        private void FadeTo(float target)
        {
            StopFade();
            if (!isActiveAndEnabled)
            {
                ApplyFactor(target);
                return;
            }
            _fadeRoutine = StartCoroutine(FadeRoutine(target));
        }

        private void StopFade()
        {
            if (_fadeRoutine == null) return;
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        private IEnumerator FadeRoutine(float target)
        {
            float start = _currentFactor;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeDuration);
                ApplyFactor(Mathf.Lerp(start, target, t * t * (3f - 2f * t)));
                yield return null;
            }
            ApplyFactor(target);
            _fadeRoutine = null;
        }

        private void ApplyFactor(float factor)
        {
            _currentFactor = factor;
            for (int i = 0; i < _renderers.Length; i++)
            {
                SpriteRenderer target = _renderers[i];
                if (target == null) continue;
                Color color = target.color;
                color.a = (i < _baseAlphas.Length ? _baseAlphas[i] : 1f) * factor;
                target.color = color;
            }
        }
        #endregion
    }
}
