using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabChunkStreamer : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private Camera _viewer;
        [SerializeField, Min(0f)] private float _margin = 2f;

        private const float SortedStep = 4f;
        private static readonly RectInt Nothing = new RectInt(int.MinValue, int.MinValue, 0, 0);
        private static readonly Vector2[] Corners = { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };

        private RectInt _shown = Nothing;
        private MapLabChunkLayer[] _layers;
        private MapLabSortedLayer[] _sorted;
        private bool _listening;
        #endregion

        #region [Properties]
        public Camera Viewer => _viewer;
        public RectInt Shown => _shown;
        public int ActiveChunks { get; private set; }
        public int ActivePieces { get; private set; }
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            Refresh();
            if (!Application.isPlaying) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Camera.onPreCull += OnPreCull;
            _listening = true;
        }

        private void OnDisable()
        {
            if (_listening)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                Camera.onPreCull -= OnPreCull;
                _listening = false;
            }
            ShowAll();
        }
        #endregion

        #region [Public Methods]
        public void SetViewer(Camera viewer)
        {
            _viewer = viewer;
            _shown = Nothing;
        }

        public void Refresh()
        {
            _layers = GetComponentsInChildren<MapLabChunkLayer>(true);
            _sorted = GetComponentsInChildren<MapLabSortedLayer>(true);
            foreach (var layer in _sorted) layer.Refresh();
            _shown = Nothing;
        }

        public void ShowArea(Rect view)
        {
            if (_layers == null || _sorted == null) Refresh();
            if (_layers.Length == 0 && _sorted.Length == 0) return;
            view.min -= Vector2.one * _margin;
            view.max += Vector2.one * _margin;
            float step = _layers.Length > 0 ? _layers[0].ChunkSize : SortedStep;
            RectInt range = MapLabChunkLayer.ChunkRange(view, step);
            if (range.Equals(_shown)) return;
            _shown = range;
            var covered = new Rect(range.x * step, range.y * step, range.width * step, range.height * step);
            ActiveChunks = 0;
            ActivePieces = 0;
            foreach (var layer in _layers)
            {
                if (layer != null) ActiveChunks += layer.Show(range);
            }
            foreach (var layer in _sorted)
            {
                if (layer != null) ActivePieces += layer.Show(covered);
            }
        }

        public void ShowAll()
        {
            if (_layers == null || _sorted == null) Refresh();
            _shown = Nothing;
            ActiveChunks = 0;
            ActivePieces = 0;
            foreach (var layer in _layers)
            {
                if (layer != null) ActiveChunks += layer.ShowAll();
            }
            foreach (var layer in _sorted)
            {
                if (layer != null) ActivePieces += layer.ShowAll();
            }
        }

        public static Rect ViewRect(Camera camera)
        {
            float depth = Mathf.Abs(camera.transform.position.z);
            Vector3 first = camera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
            float xMin = first.x, xMax = first.x, yMin = first.y, yMax = first.y;
            foreach (var corner in Corners)
            {
                Vector3 point = camera.ViewportToWorldPoint(new Vector3(corner.x, corner.y, depth));
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
        #endregion

        #region [Private Methods]
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Follow(camera);
        }

        private void OnPreCull(Camera camera)
        {
            Follow(camera);
        }

        private void Follow(Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            if (_viewer == null)
            {
                if (!camera.orthographic) return;
                _viewer = camera;
            }
            if (camera == _viewer) ShowArea(ViewRect(camera));
        }
        #endregion
    }
}
