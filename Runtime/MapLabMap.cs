using System.Collections.Generic;
using UnityEngine;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabMap : MonoBehaviour
    {
        #region [Variables/Fields]
        [Header("Walkable Area")]
        [SerializeField] private Rect _bounds = new Rect(-10f, -10f, 20f, 20f);
        [SerializeField, Min(0.05f)] private float _cellSize = 0.5f;
        [SerializeField] private LayerMask _obstacleLayers = ~0;
        [SerializeField] private TextAsset _walkableData;
        [SerializeField] private Texture2D _walkableMask;
        [SerializeField, HideInInspector] private string _walkableStamp;

        [Header("Heights")]
        [SerializeField, Min(0f)] private float _stepHeight = 0.25f;
        [SerializeField] private TextAsset _elevationData;
        [SerializeField] private Texture2D _elevationMask;

        [Header("Bake Layers")]
        [SerializeField] private string _actorSortingLayer = "Default";
        [SerializeField] private int _actorSortingOrder = 6;
        [SerializeField, Min(1f)] private float _bakeChunkSize = 16f;
        [SerializeField, Range(8, 256)] private int _bakePixelsPerUnit = 64;
        [SerializeField, Range(256, 16384)] private int _megaChunkPixels = MegaChunkFormula.DefaultMegaPixels;
        [SerializeField, HideInInspector] private List<MapLabBakeLayerSettings> _bakeLayers = new List<MapLabBakeLayerSettings>();
        [SerializeField, HideInInspector] private List<string> _layerStack = new List<string>();

        [Header("World")]
        [SerializeField] private MapLabWorldData _world;
        [SerializeField, HideInInspector] private MapLabPreset _preset;
        #endregion

        #region [Properties]
        public Rect Bounds => _bounds;
        public float CellSize => _cellSize;
        public LayerMask ObstacleLayers => _obstacleLayers;
        public TextAsset WalkableData => _walkableData;
        public Texture2D WalkableMask => _walkableMask;
        public float StepHeight => _stepHeight;
        public TextAsset ElevationData => _elevationData;
        public Texture2D ElevationMask => _elevationMask;
        public string ActorSortingLayer => _actorSortingLayer;
        public int ActorSortingOrder => _actorSortingOrder;
        public float BakeChunkSize => _bakeChunkSize;
        public int BakePixelsPerUnit => _bakePixelsPerUnit;
        public List<MapLabBakeLayerSettings> BakeLayers => _bakeLayers;
        public List<string> LayerStack => _layerStack;
        public int MegaChunkPixels => _megaChunkPixels;
        public float ChunkSize => MegaChunkFormula.SnappedChunkSize(_bakeChunkSize, _bakePixelsPerUnit);
        public int ChunkPixels => MegaChunkFormula.ChunkPixels(ChunkSize, _bakePixelsPerUnit);
        public int ChunksPerMega => MegaChunkFormula.ChunksPerMega(ChunkPixels, _megaChunkPixels);
        public float MegaChunkSize => ChunkSize * ChunksPerMega;

        public MapLabWorldData World
        {
            get => _world;
            set => _world = value;
        }

        public MapLabPreset Preset
        {
            get => _preset;
            set => _preset = value;
        }
        #endregion

        #region [Unity Methods]
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(_bounds.center, _bounds.size);
        }
        #endregion
    }
}
