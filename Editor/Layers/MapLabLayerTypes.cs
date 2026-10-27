using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace MapLab.Editor
{
    public static class MapLabLayerTypes
    {
        #region [Variables/Fields]
        public const string KeepLive = "Keep live";
        #endregion

        #region [Public Methods]
        public static List<MapLabBakeLayerSettings> Targets(MapLabMap map, MapLabBakeKinds kind)
        {
            List<MapLabBakeLayerSettings> decorate = MapLabLayerStack.DecorateLayers(map);
            bool sorted = kind == MapLabBakeKinds.SortedShapes;
            return MapLabLayerStack.LayersBottomToTop(map).Where(layer => layer.Sorted == sorted && !decorate.Contains(layer)).ToList();
        }

        public static MapLabBakeLayerSettings Owner(MapLabMap map, MapLabBakeKinds kind)
        {
            List<MapLabBakeLayerSettings> targets = Targets(map, kind);
            return targets.FirstOrDefault(layer => (layer.Kinds & kind) != 0 && layer.Sets.Any(set => set != null))
                ?? targets.FirstOrDefault(layer => (layer.Kinds & kind) != 0);
        }

        public static List<MapLabBakeKinds> Held(MapLabMap map, MapLabBakeLayerSettings layer)
        {
            return MapLabLayerClaims.Kinds.Where(kind => Owner(map, kind) == layer).ToList();
        }

        public static void Route(MapLabMap map, MapLabBakeKinds kind, string layer)
        {
            MapLabLayerStack.EnsureLayers(map);
            MapLabBakeLayerSettings target = layer != null ? Targets(map, kind).FirstOrDefault(settings => MapLabLayerStack.Same(settings.Name, layer)) : null;
            Undo.RecordObject(map, "Change MapLab Layer Types");
            foreach (var settings in map.BakeLayers) settings.Kinds &= ~kind;
            if (target != null) target.Kinds |= kind;
            MapLabLayerStack.MarkDirty(map);
            MapLabDrawLayers.Sync(map);
            MapLabLayerBaker.SyncScene(map);
        }

        public static void Release(MapLabMap map, MapLabBakeLayerSettings layer)
        {
            if (layer == null || MapLabLayerStack.IsDecorate(map, layer.Name)) return;
            MapLabBakeKinds taken = MapLabBakeKinds.None;
            foreach (var kind in MapLabLayerClaims.Kinds)
            {
                if ((layer.Kinds & kind) == 0) continue;
                if (Targets(map, kind).Any(other => other != layer && (other.Kinds & kind) != 0)) taken |= kind;
            }
            if (taken == MapLabBakeKinds.None) return;
            Undo.RecordObject(map, "Change MapLab Layer Types");
            layer.Kinds &= ~taken;
            MapLabLayerStack.MarkDirty(map);
        }
        #endregion
    }
}
