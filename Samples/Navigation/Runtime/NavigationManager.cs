using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MapLab.Samples.Navigation
{
    [DisallowMultipleComponent]
    public class NavigationManager : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private NavigationMode _mode = NavigationMode.Auto;
        [SerializeField] private TextAsset _walkableData;
        [SerializeField] private bool _buildOnStart = true;
        [SerializeField, Min(0.05f)] private float _agentRadius = 0.3f;
        [SerializeField, Min(0.1f)] private float _sampleDistance = 1.5f;

        private NavMeshDataInstance _navMeshInstance;
        private NavMeshPath _path;
        private bool _warnedFallback;
        #endregion

        #region [Properties]
        public static NavigationManager Instance { get; private set; }
        public static Func<Vector2, Vector2, List<Vector2>> GridPathfinder { get; set; }
        public static event Action<NavigationManager> OnNavMeshBuilt;

        public static bool IsAvailable => (Instance != null && Instance.UsesNavMesh) || GridPathfinder != null;

        public NavigationMode Mode => _mode;
        public TextAsset WalkableData => _walkableData;
        public Rect WalkableArea { get; private set; }
        public bool IsNavMeshReady => _navMeshInstance.valid;
        public bool UsesNavMesh => _mode != NavigationMode.Grid && IsNavMeshReady;
        #endregion

        #region [Unity Methods]
        private void Awake()
        {
            if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            if (_buildOnStart && _mode != NavigationMode.Grid && _walkableData != null) Rebuild();
        }

        private void OnDestroy()
        {
            RemoveNavMesh();
            if (Instance == this) Instance = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(WalkableArea.center, WalkableArea.size);
            if (!IsNavMeshReady) return;

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            for (int i = 0; i + 2 < triangulation.indices.Length; i += 3)
            {
                Vector3 a = triangulation.vertices[triangulation.indices[i]];
                Vector3 b = triangulation.vertices[triangulation.indices[i + 1]];
                Vector3 c = triangulation.vertices[triangulation.indices[i + 2]];
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, a);
            }
        }
        #endregion

        #region [Public Methods]
        public static List<Vector2> FindPath(Vector2 from, Vector2 to)
        {
            if (Instance != null)
            {
                if (Instance.UsesNavMesh) return Instance.FindNavMeshPath(from, to);
                Instance.WarnFallbackOnce();
            }

            return GridPathfinder?.Invoke(from, to);
        }

        public void Rebuild()
        {
            RemoveNavMesh();
            if (_walkableData == null)
            {
                Debug.LogWarning($"[MapLab] NavigationManager in {gameObject.scene.name} has no Walkable Data; bake the map and assign the JSON file.");
                return;
            }

            WalkableAreaData area = WalkableAreaData.Parse(_walkableData.text);
            WalkableArea = area.Bounds;
            NavMeshData data = NavMesh2DFactory.Build(area.Bounds, area.HoleOutlines(), _agentRadius);
            if (data == null)
            {
                Debug.LogWarning($"[MapLab] NavMesh build failed in {gameObject.scene.name}; using the grid pathfinder.");
                return;
            }

            _navMeshInstance = NavMesh.AddNavMeshData(data);
            _warnedFallback = false;
            OnNavMeshBuilt?.Invoke(this);
        }

        public List<Vector2> FindNavMeshPath(Vector2 from, Vector2 to)
        {
            if (!IsNavMeshReady) return null;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, _sampleDistance, NavMesh.AllAreas)) return null;
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, _sampleDistance, NavMesh.AllAreas)) return null;

            _path ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path)) return null;
            if (_path.status == NavMeshPathStatus.PathInvalid) return null;

            Vector3[] corners = _path.corners;
            var result = new List<Vector2>(corners.Length);
            for (int i = 1; i < corners.Length; i++)
            {
                result.Add(corners[i]);
            }
            return result;
        }
        #endregion

        #region [Private Methods]
        private void RemoveNavMesh()
        {
            if (_navMeshInstance.valid) NavMesh.RemoveNavMeshData(_navMeshInstance);
            _navMeshInstance = default;
        }

        private void WarnFallbackOnce()
        {
            if (_mode != NavigationMode.NavMesh || _warnedFallback) return;
            _warnedFallback = true;
            Debug.LogWarning($"[MapLab] NavMesh mode is on but no NavMesh is built in {gameObject.scene.name}; using the grid pathfinder.");
        }
        #endregion
    }
}
