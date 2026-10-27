using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabDrawLayerProbe
    {
        #region [Variables/Fields]
        public const string BeforePath = MapLabPreviewRenderer.OutputFolder + "/MapLab_drawlayers_before.png";
        public const string GroundPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_drawlayers_ground.png";
        public const string BakedPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_drawlayers_baked.png";

        private const string DecorLayer = "Ground Decor";
        private const string CliffSet = "Cliffs";
        private const string PropSet = "Props";
        private const string DecorSet = "Decor";
        private const float ShotSize = 6f;
        #endregion

        #region [Public Methods]
        public static int Run(StringBuilder report, MapLabMap map, Scene scene)
        {
            report.AppendLine();
            report.AppendLine("8. Decorate line: the Cliffs set and a prop become background art under the line");
            var (unbaked, shown) = MapLabLayerBaker.UnbakeAll(map, true);
            report.AppendLine($"Unbaked {unbaked} layers first so the first two shots show the live shapes ({shown} sources live)");
            List<MapLabSet> sets = MapLabSets.SetsOf(map);
            MapLabSet cliffs = sets.First(set => set.name == CliffSet);
            MapLabSet props = sets.First(set => set.name == PropSet);
            ExtrudedShape cliff = cliffs.GetComponentsInChildren<ExtrudedShape>(true).First(shape => !shape.IsStacked);
            GameObject tree = props.transform.Cast<Transform>().FirstOrDefault(child => child.Find("Canopy") != null)?.gameObject ?? props.transform.GetChild(0).gameObject;
            MapLabSet decor = MapLabSets.CreateSet(map, DecorSet);
            tree.transform.SetParent(decor.transform, true);

            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            Vector2 center = Center(cliff);
            GameObject actor = StandIn(map, center + Vector2.up * 0.6f);
            int obstaclesBefore = MapLabWalkableBaker.CollectObstacles(map).Count;
            SortingGroup.UpdateAllSortingGroups();
            MapLabWorldProbe.Shoot(camera, center, ShotSize, BeforePath);

            MapLabBakeLayerSettings layer = MapLabLayerEdits.AddDecorate(map);
            MapLabLayerEdits.Rename(map, layer.Name, DecorLayer);
            layer = MapLabLayerStack.Find(map, DecorLayer);
            MapLabLayerEdits.SetSet(map, layer, cliffs, true);
            MapLabLayerEdits.SetSet(map, layer, decor, true);
            report.AppendLine($"Stack (bottom to top, | = Decorate line): {Names(map)}");
            int failures = Check(report, MapLabLayerStack.IsDecorate(map, DecorLayer), "the new layer sits under the Decorate line, also after a rename");

            List<ExtrudedShape> cliffShapes = cliffs.GetComponentsInChildren<ExtrudedShape>(true).Where(shape => !shape.IsStacked).ToList();
            int decorated = cliffShapes.Count(shape => shape.DrawsAsGround);
            bool noCollider = cliffShapes.All(shape => shape.transform.Find(ExtrudedShape.CollisionName).GetComponent<PolygonCollider2D>() == null);
            MapLabGroundProp groundTree = tree.GetComponent<MapLabGroundProp>();
            int obstaclesAfter = MapLabWalkableBaker.CollectObstacles(map).Count;
            report.AppendLine($"'{DecorLayer}' takes {decorated} of {cliffShapes.Count} cliffs (order {MapLabDrawLayers.OrderOf(map, DecorLayer)}) and '{tree.name}'; obstacles {obstaclesBefore} -> {obstaclesAfter}");
            failures += Check(report, decorated == cliffShapes.Count && noCollider, "every plateau of the Cliffs set is background art with no collider");
            failures += Check(report, groundTree != null && tree.GetComponentsInChildren<Collider2D>(true).All(collider => !collider.enabled), "the prop in the Decor set is background art and stops blocking");
            failures += Check(report, obstaclesAfter < obstaclesBefore, "the walkable bake sees fewer obstacles");
            int shadowFloor = MapLabDrawLayers.ShadowFloor(map);
            List<string> order = MapLabLayerStack.DrawOrder(map, MapLabLayerStack.Resolve(map, false)).Select(entry => entry.Name).ToList();
            string shadowLayer = order.FirstOrDefault(name => MapLabLayerStack.HoldsShadows(MapLabLayerStack.Find(map, name)));
            report.AppendLine($"Cliffs draw at {string.Join(", ", cliffShapes.Select(shape => shape.SortingOrder).Distinct())}, the lowest shadow at {shadowFloor}; bake order {string.Join(" ", order)}");
            failures += Check(report, cliffShapes.All(shape => shape.SortingOrder < shadowFloor) && shadowLayer != null && order.IndexOf(DecorLayer) < order.IndexOf(shadowLayer),
                "other plateaus' shadows draw over the decor, live and in the bake order");

            MapLabLayerEdits.SetCastShadows(map, layer, false);
            bool shadowsOff = cliffShapes.All(shape => !shape.CastsShadow) && (groundTree == null || groundTree.ShadowCount == 0 || groundTree.ShadowsHidden);
            MapLabLayerEdits.SetCastShadows(map, layer, true);
            bool shadowsOn = cliffShapes.All(shape => shape.CastsShadow || shape.Height <= 0f) && (groundTree == null || !groundTree.ShadowsHidden);
            failures += Check(report, shadowsOff && shadowsOn, "Cast shadows turns the decor's own shadows off and on");

            MapLabBakeLayerSettings owner = MapLabLayerTypes.Owner(map, MapLabBakeKinds.Paths);
            MapLabLayerTypes.Route(map, MapLabBakeKinds.Paths, null);
            bool live = MapLabLayerTypes.Owner(map, MapLabBakeKinds.Paths) == null;
            if (owner != null) MapLabLayerTypes.Route(map, MapLabBakeKinds.Paths, owner.Name);
            int holders = MapLabLayerStack.Layers(map).Count(settings => (settings.Kinds & MapLabBakeKinds.Paths) != 0);
            failures += Check(report, live && holders == (owner != null ? 1 : 0), "a type goes to Keep live and back, and only one layer ever holds it");
            SortingGroup.UpdateAllSortingGroups();
            MapLabWorldProbe.Shoot(camera, center, ShotSize, GroundPath);

            report.AppendLine(MapLabStaticBaker.Bake(map));
            MapLabBakedLayer decorBaked = MapLabLayerBaker.BakedLayer(map, DecorLayer);
            int actorOrder = map.ActorSortingOrder;
            int decorOrder = decorBaked != null && decorBaked.ChunkLayer.Chunks.Count > 0 ? decorBaked.ChunkLayer.Chunks[0].sortingOrder : int.MaxValue;
            report.AppendLine($"Baked: '{DecorLayer}' {decorBaked?.ChunkLayer.Chunks.Count ?? 0} chunks at order {decorOrder}, actors at {actorOrder}");
            failures += Check(report, decorBaked != null && decorOrder < actorOrder, "the decor bakes into chunks under the actors");
            MapLabBakedLayer shadowsBaked = MapLabLayerStack.Layers(map).Where(MapLabLayerStack.HoldsShadows).Select(settings => MapLabLayerBaker.BakedLayer(map, settings.Name)).FirstOrDefault(baked => baked != null && baked.ChunkLayer != null && baked.ChunkLayer.Chunks.Count > 0);
            int shadowOrder = shadowsBaked != null ? shadowsBaked.ChunkLayer.Chunks[0].sortingOrder : int.MinValue;
            int groundOrder = Lowest(map, order.Take(Math.Max(0, order.IndexOf(DecorLayer))));
            report.AppendLine($"Baked shadows at order {shadowOrder}, the ground under the decor at {groundOrder}");
            failures += Check(report, shadowsBaked != null && shadowOrder > decorOrder, "the baked shadows draw over the baked decor");
            failures += Check(report, groundOrder < decorOrder, "the baked decor draws over the baked floor");
            failures += Check(report, cliffShapes.All(shape => shape.VisualBaked), "the live cliff pieces are hidden after the bake");
            SortingGroup.UpdateAllSortingGroups();
            MapLabWorldProbe.Shoot(camera, center, ShotSize, BakedPath);
            report.AppendLine($"Shots: {BeforePath} (cliff sorts with the stand-in), {GroundPath} (cliff as background art, live), {BakedPath} (baked)");

            MapLabLayerEdits.SetSide(map, MapLabLayerStack.Find(map, DecorLayer), false);
            failures += Check(report, cliffShapes.All(shape => !shape.DrawsAsGround) && tree.GetComponent<MapLabGroundProp>() == null && tree.GetComponentsInChildren<Collider2D>(true).All(collider => collider.enabled),
                "moving the layer above the line puts everything back as it was");
            Object.DestroyImmediate(actor);
            return failures;
        }
        #endregion

        #region [Private Methods]
        private static string Names(MapLabMap map)
        {
            return string.Join(" ", MapLabLayerStack.NamesWithLine(map).Select(name => name == MapLabLayerStack.DecorateMarker ? "|" : name));
        }

        private static int Lowest(MapLabMap map, IEnumerable<string> layers)
        {
            int lowest = int.MaxValue;
            foreach (var name in layers)
            {
                MapLabBakedLayer baked = MapLabLayerBaker.BakedLayer(map, name);
                if (baked != null && baked.ChunkLayer != null && baked.ChunkLayer.Chunks.Count > 0) lowest = Mathf.Min(lowest, baked.ChunkLayer.Chunks[0].sortingOrder);
            }
            return lowest == int.MaxValue ? int.MinValue : lowest;
        }

        private static Vector2 Center(ExtrudedShape shape)
        {
            List<Vector2> footprint = shape.Footprint();
            Vector2 sum = Vector2.zero;
            foreach (var point in footprint) sum += point;
            return shape.transform.TransformPoint(sum / footprint.Count);
        }

        private static GameObject StandIn(MapLabMap map, Vector2 feet)
        {
            var texture = new Texture2D(16, 32) { filterMode = FilterMode.Point };
            var pixels = new Color32[16 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = i / 16 > 22 ? new Color32(233, 200, 160, 255) : new Color32(62, 92, 138, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var go = new GameObject("[Probe] Actor");
            SceneManager.MoveGameObjectToScene(go, map.gameObject.scene);
            go.transform.position = new Vector3(feet.x, feet.y, 0f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 16f);
            renderer.sortingLayerName = map.ActorSortingLayer;
            renderer.sortingOrder = map.ActorSortingOrder;
            go.AddComponent<SortingGroup>().sortingOrder = map.ActorSortingOrder;
            return go;
        }

        private static int Check(StringBuilder report, bool passed, string what)
        {
            report.AppendLine($"{(passed ? "PASS" : "FAIL")}  {what}");
            return passed ? 0 : 1;
        }
        #endregion
    }
}
