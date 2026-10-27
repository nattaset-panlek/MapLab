using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLab.Editor
{
    public static class MapLabStaticBaker
    {
        #region [Public Methods]
        [MenuItem("Tools/MapLab/Bake All Layers (Open Scenes)", false, 21)]
        public static void BakeOpenScenes()
        {
            foreach (var map in OpenMaps()) Debug.Log(Bake(map));
        }

        [MenuItem("Tools/MapLab/Unbake All Layers (Open Scenes)", false, 22)]
        public static void UnbakeOpenScenes()
        {
            foreach (var map in OpenMaps()) Debug.Log(Unbake(map));
        }

        public static void BakeDemoBatch()
        {
            MapLabSceneBuilder.RunBatch(() =>
            {
                Scene scene = EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (var map in MapLabWalkableBaker.FindMaps(scene)) Debug.Log(Bake(map));
                EditorSceneManager.SaveScene(scene);
            });
        }

        public static string Bake(MapLabMap map)
        {
            MapLabLayerStack.EnsureLayers(map);
            var messages = new List<string>();
            foreach (var layer in MapLabLayerStack.LayersBottomToTop(map)) messages.Add(MapLabLayerBaker.Bake(map, layer.Name));
            return string.Join("\n", messages);
        }

        public static string RebakeOutOfDate(MapLabMap map)
        {
            var messages = new List<string>();
            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out _);
            foreach (var layer in MapLabLayerStack.LayersBottomToTop(map))
            {
                if (layer.Locked || MapLabLayerBaker.BakedLayer(map, layer.Name) == null) continue;
                if (MapLabBakeStatus.Layer(map, layer, claims[layer.Name]).State == MapLabBakeStatus.State.OutOfDate) messages.Add(MapLabLayerBaker.Bake(map, layer.Name));
            }
            return messages.Count > 0 ? string.Join("\n", messages) : $"[MapLab] Every baked layer of '{map.name}' is up to date";
        }

        public static string Unbake(MapLabMap map)
        {
            (int layers, int restored) = MapLabLayerBaker.UnbakeAll(map, false);
            if (layers == 0 && restored == 0) return $"[MapLab] '{map.name}' has no baked layer to unbake";
            return $"[MapLab] Unbaked {layers} layers of '{map.name}': {restored} live sources shown again (images and paint stay on disk; locked layers stay baked)";
        }

        public static List<Renderer> CollectGround(MapLabMap map)
        {
            int actorLayer = SortingLayer.GetLayerValueFromName(map.ActorSortingLayer);
            return map.GetComponentsInChildren<Renderer>(true).Where(renderer => IsStaticGround(renderer, actorLayer, map.ActorSortingOrder)).ToList();
        }

        public static List<Renderer> CollectSorted(MapLabMap map)
        {
            var sorted = new List<Renderer>();
            foreach (var shape in map.GetComponentsInChildren<ExtrudedShape>(true))
            {
                Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
                if (visual == null || shape.GetComponentInParent<Animator>() != null || shape.GetComponentInParent<MapLabChunkStreamer>() != null) continue;
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy && MapLabLayerClaims.IsSortedPiece(renderer, map)) sorted.Add(renderer);
                }
            }
            return sorted;
        }

        public static bool IsStaticGround(Renderer renderer, int actorLayerValue, int actorOrder)
        {
            if (!(renderer is SpriteRenderer) && !(renderer is MeshRenderer)) return false;
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
            if (renderer.GetComponentInParent<Animator>() != null) return false;
            if (renderer.GetComponentInParent<MapLabChunkStreamer>() != null) return false;
            return MapLabLayerClaims.IsBelowActors(renderer, actorLayerValue, actorOrder);
        }

        public static void HideSources(MapLabBakedLayer layer, List<Renderer> ground)
        {
            var hidden = new List<Renderer>();
            var visuals = new List<PointShape>();
            var shadows = new List<ExtrudedShape>();
            var floors = new List<MapLabFloor>();
            foreach (var renderer in ground)
            {
                MapLabFloor floor = renderer.GetComponentInParent<MapLabFloor>();
                if (floor != null && (renderer.gameObject.hideFlags & HideFlags.DontSave) != 0)
                {
                    if (!floors.Contains(floor)) floors.Add(floor);
                    continue;
                }
                PointShape owner = renderer.GetComponentInParent<PointShape>();
                bool generated = (renderer.gameObject.hideFlags & HideFlags.DontSave) != 0;
                if (owner == null || !generated)
                {
                    renderer.enabled = false;
                    hidden.Add(renderer);
                }
                else if (MapLabLayerClaims.IsShapeShadow(renderer, owner))
                {
                    var extruded = (ExtrudedShape)owner;
                    if (!shadows.Contains(extruded)) shadows.Add(extruded);
                }
                else if (!visuals.Contains(owner))
                {
                    visuals.Add(owner);
                }
            }
            foreach (var shape in visuals) shape.SetVisualBaked(true);
            foreach (var shape in shadows) shape.SetShadowBaked(true);
            foreach (var floor in floors) floor.SetBaked(true);
            layer.RecordHidden(hidden, visuals, shadows, floors);
        }
        #endregion

        #region [Private Methods]
        private static IEnumerable<MapLabMap> OpenMaps()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                foreach (var map in MapLabWalkableBaker.FindMaps(SceneManager.GetSceneAt(i))) yield return map;
            }
        }
        #endregion
    }
}
