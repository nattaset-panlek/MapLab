using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Editor
{
    public static class MapLabLayerClaims
    {
        #region [Variables/Fields]
        public static readonly MapLabBakeKinds[] Kinds =
        {
            MapLabBakeKinds.Floor, MapLabBakeKinds.Sprites, MapLabBakeKinds.Paths, MapLabBakeKinds.FlatShapes, MapLabBakeKinds.SortedShapes, MapLabBakeKinds.ShapeShadows, MapLabBakeKinds.PropShadows
        };

        public static readonly MapLabBakeKinds[] DecorateKinds = { MapLabBakeKinds.PulledShapes, MapLabBakeKinds.PulledProps };
        #endregion

        #region [Public Methods]
        public static string KindLabel(MapLabBakeKinds kind)
        {
            switch (kind)
            {
                case MapLabBakeKinds.Sprites: return "Ground sprites";
                case MapLabBakeKinds.Paths: return "Roads";
                case MapLabBakeKinds.FlatShapes: return "Flat shapes";
                case MapLabBakeKinds.ShapeShadows: return "Shape shadows";
                case MapLabBakeKinds.PropShadows: return "Prop shadows";
                case MapLabBakeKinds.SortedShapes: return "Tall shapes";
                case MapLabBakeKinds.Floor: return "Floor tiles";
                case MapLabBakeKinds.PulledShapes: return "Decorate shapes";
                case MapLabBakeKinds.PulledProps: return "Decorate props";
                default: return kind.ToString();
            }
        }

        public static string KindHint(MapLabBakeKinds kind)
        {
            switch (kind)
            {
                case MapLabBakeKinds.Sprites: return "flat sprites under the actors: grass, decals";
                case MapLabBakeKinds.Paths: return "path shapes";
                case MapLabBakeKinds.FlatShapes: return "plateaus, rings and boxes with no height";
                case MapLabBakeKinds.ShapeShadows: return "the shadow each tall shape throws";
                case MapLabBakeKinds.PropShadows: return "shadows under trees, rocks, crates";
                case MapLabBakeKinds.SortedShapes: return "walls, plateaus, pillars that hide who walks behind";
                case MapLabBakeKinds.Floor: return "the floor grid painted in Floor tiles";
                case MapLabBakeKinds.PulledShapes: return "plateaus drawn as background art";
                case MapLabBakeKinds.PulledProps: return "props drawn as background art";
                default: return "";
            }
        }

        public static MapLabBakeKinds KindOf(Renderer renderer, MapLabMap map)
        {
            if (renderer.GetComponentInParent<MapLabFloor>() != null) return MapLabBakeKinds.Floor;
            if (renderer.GetComponentInParent<MapLabGroundProp>() != null) return MapLabBakeKinds.PulledProps;
            PointShape owner = renderer.GetComponentInParent<PointShape>();
            bool generated = (renderer.gameObject.hideFlags & HideFlags.DontSave) != 0;
            if (owner != null && generated)
            {
                if (owner is PathShape) return MapLabBakeKinds.Paths;
                if (owner is ExtrudedShape extruded && extruded.DrawsAsGround) return MapLabBakeKinds.PulledShapes;
                if (IsShapeShadow(renderer, owner)) return MapLabBakeKinds.ShapeShadows;
                return MapLabBakeKinds.FlatShapes;
            }
            return IsPropShadow(renderer, map) ? MapLabBakeKinds.PropShadows : MapLabBakeKinds.Sprites;
        }

        public static bool IsShapeShadow(Renderer renderer, PointShape owner)
        {
            return owner is ExtrudedShape extruded && renderer.transform.parent == extruded.transform && renderer.name == ExtrudedShape.ShadowName;
        }

        public static MapLabBakeLayerSettings Claim(Renderer renderer, MapLabBakeKinds kind, IReadOnlyList<MapLabBakeLayerSettings> bottomToTop)
        {
            foreach (var layer in bottomToTop)
            {
                if (HasSets(layer) && Accepts(layer, renderer, kind)) return layer;
            }
            foreach (var layer in bottomToTop)
            {
                if (!HasSets(layer) && Accepts(layer, renderer, kind)) return layer;
            }
            return null;
        }

        public static Dictionary<string, List<Renderer>> ClaimAll(MapLabMap map, out List<Renderer> unclaimed)
        {
            return ClaimAll(map, out unclaimed, out _);
        }

        public static Dictionary<string, List<Renderer>> ClaimAll(MapLabMap map, out List<Renderer> unclaimed, out List<Renderer> unclaimedSorted)
        {
            List<MapLabBakeLayerSettings> layers = MapLabLayerStack.LayersBottomToTop(map);
            var claims = layers.ToDictionary(layer => layer.Name, layer => new List<Renderer>(), System.StringComparer.OrdinalIgnoreCase);
            List<MapLabBakeLayerSettings> decorate = MapLabLayerStack.DecorateLayers(map);
            List<MapLabBakeLayerSettings> ground = layers.Where(layer => !layer.Sorted && !decorate.Contains(layer)).ToList();
            List<MapLabBakeLayerSettings> sorted = layers.Where(layer => layer.Sorted).ToList();
            unclaimed = new List<Renderer>();
            unclaimedSorted = new List<Renderer>();
            foreach (var renderer in MapLabStaticBaker.CollectGround(map))
            {
                int rank = MapLabDrawLayers.OwnerOf(decorate, renderer.transform);
                MapLabBakeKinds kind = KindOf(renderer, map);
                MapLabBakeLayerSettings owner = rank >= 0 ? decorate[rank] : DecorateKinds.Contains(kind) ? null : Claim(renderer, kind, ground);
                if (owner != null) claims[owner.Name].Add(renderer);
                else unclaimed.Add(renderer);
            }
            foreach (var renderer in MapLabStaticBaker.CollectSorted(map))
            {
                MapLabBakeLayerSettings owner = Claim(renderer, MapLabBakeKinds.SortedShapes, sorted);
                if (owner != null) claims[owner.Name].Add(renderer);
                else unclaimedSorted.Add(renderer);
            }
            return claims;
        }

        public static List<Renderer> Claimed(MapLabMap map, string layer)
        {
            return ClaimAll(map, out _).TryGetValue(layer, out List<Renderer> claimed) ? claimed : new List<Renderer>();
        }

        public static Dictionary<MapLabBakeKinds, int> CountKinds(MapLabMap map, IEnumerable<Renderer> renderers)
        {
            var counts = Kinds.Concat(DecorateKinds).ToDictionary(kind => kind, kind => 0);
            var shapes = new HashSet<PointShape>();
            foreach (var renderer in renderers)
            {
                if (IsSortedPiece(renderer, map)) shapes.Add(renderer.GetComponentInParent<PointShape>());
                else counts[KindOf(renderer, map)]++;
            }
            counts[MapLabBakeKinds.SortedShapes] = shapes.Count;
            return counts;
        }

        public static bool IsSortedPiece(Renderer renderer, MapLabMap map)
        {
            if (!(renderer.GetComponentInParent<PointShape>() is ExtrudedShape shape)) return false;
            if ((renderer.gameObject.hideFlags & HideFlags.DontSave) == 0 || IsShapeShadow(renderer, shape)) return false;
            return !IsBelowActors(renderer, SortingLayer.GetLayerValueFromName(map.ActorSortingLayer), map.ActorSortingOrder);
        }

        public static int CountShapes(IEnumerable<Renderer> renderers)
        {
            return renderers.Select(renderer => renderer.GetComponentInParent<PointShape>()).Where(shape => shape != null).Distinct().Count();
        }

        public static int CountSorted(MapLabMap map)
        {
            int actorLayer = SortingLayer.GetLayerValueFromName(map.ActorSortingLayer);
            int sorted = 0;
            foreach (var shape in map.GetComponentsInChildren<ExtrudedShape>(true))
            {
                Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
                if (visual == null) continue;
                if (visual.GetComponentsInChildren<Renderer>(true).Any(renderer => !IsBelowActors(renderer, actorLayer, map.ActorSortingOrder))) sorted++;
            }
            return sorted;
        }

        public static int SortingLayerOf(Renderer renderer)
        {
            SortingGroup group = renderer.GetComponentInParent<SortingGroup>();
            return group != null ? group.sortingLayerID : renderer.sortingLayerID;
        }

        public static int SortingOrderOf(Renderer renderer)
        {
            SortingGroup group = renderer.GetComponentInParent<SortingGroup>();
            return group != null ? group.sortingOrder : renderer.sortingOrder;
        }

        public static Vector2Int SortKey(Renderer renderer)
        {
            return new Vector2Int(SortingLayer.GetLayerValueFromID(SortingLayerOf(renderer)), SortingOrderOf(renderer));
        }

        public static bool IsBelowActors(Renderer renderer, int actorLayerValue, int actorOrder)
        {
            if (renderer == null) return false;
            Vector2Int key = SortKey(renderer);
            return key.x < actorLayerValue || (key.x == actorLayerValue && key.y < actorOrder);
        }
        #endregion

        #region [Private Methods]
        private static bool HasSets(MapLabBakeLayerSettings layer)
        {
            return layer.Sets.Any(set => set != null);
        }

        private static bool Accepts(MapLabBakeLayerSettings layer, Renderer renderer, MapLabBakeKinds kind)
        {
            if ((layer.Kinds & kind) == 0) return false;
            return !HasSets(layer) || layer.Sets.Any(set => set != null && renderer.transform.IsChildOf(set.transform));
        }

        private static bool IsPropShadow(Renderer renderer, MapLabMap map)
        {
            Transform parent = renderer.transform.parent;
            if (parent == null || parent == map.transform || parent.GetComponent<MapLabSet>() != null) return false;
            int actorLayer = SortingLayer.GetLayerValueFromName(map.ActorSortingLayer);
            foreach (var other in parent.GetComponentsInChildren<Renderer>(true))
            {
                if (other != renderer && !IsBelowActors(other, actorLayer, map.ActorSortingOrder)) return true;
            }
            return false;
        }
        #endregion
    }
}
