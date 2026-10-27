using System.Collections.Generic;
using MapLab.Shapes;
using UnityEngine;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabBakedLayer : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private List<Renderer> _hiddenRenderers = new List<Renderer>();
        [SerializeField] private List<PointShape> _bakedVisuals = new List<PointShape>();
        [SerializeField] private List<ExtrudedShape> _bakedShadows = new List<ExtrudedShape>();
        [SerializeField] private List<MapLabFloor> _bakedFloors = new List<MapLabFloor>();
        [SerializeField] private int _sourceSortingLayer;
        [SerializeField] private int _sourceSortingOrder;
        [SerializeField, HideInInspector] private string _sourceStamp;

        private MapLabChunkLayer _chunks;
        private MapLabSortedLayer _sorted;
        #endregion

        #region [Properties]
        public MapLabChunkLayer ChunkLayer => _chunks != null ? _chunks : _chunks = GetComponent<MapLabChunkLayer>();
        public MapLabSortedLayer SortedLayer => _sorted != null ? _sorted : _sorted = GetComponent<MapLabSortedLayer>();
        public bool IsSorted => SortedLayer != null;
        public string LayerName => ChunkLayer != null ? ChunkLayer.LayerName : SortedLayer != null ? SortedLayer.LayerName : null;
        public int SourceSortingLayer => _sourceSortingLayer;
        public int SourceSortingOrder => _sourceSortingOrder;
        public IReadOnlyList<Renderer> HiddenRenderers => _hiddenRenderers;
        public IReadOnlyList<PointShape> BakedVisuals => _bakedVisuals;
        public IReadOnlyList<ExtrudedShape> BakedShadows => _bakedShadows;
        public IReadOnlyList<MapLabFloor> BakedFloors => _bakedFloors;
        #endregion

        #region [Public Methods]
        public void RecordHidden(List<Renderer> renderers, List<PointShape> visuals, List<ExtrudedShape> shadows)
        {
            RecordHidden(renderers, visuals, shadows, new List<MapLabFloor>());
        }

        public void RecordHidden(List<Renderer> renderers, List<PointShape> visuals, List<ExtrudedShape> shadows, List<MapLabFloor> floors)
        {
            _hiddenRenderers = new List<Renderer>(renderers);
            _bakedVisuals = new List<PointShape>(visuals);
            _bakedShadows = new List<ExtrudedShape>(shadows);
            _bakedFloors = new List<MapLabFloor>(floors);
        }

        public void SetSourceSorting(int sortingLayerId, int sortingOrder)
        {
            _sourceSortingLayer = sortingLayerId;
            _sourceSortingOrder = sortingOrder;
        }
        #endregion
    }
}
