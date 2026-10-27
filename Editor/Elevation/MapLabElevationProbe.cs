using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Elevation;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabElevationProbe
    {
        #region [Variables/Fields]
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_elevation.txt";
        public const string AnchorName = "Sort Anchor";
        public const string StandeeVisualName = "Visual";

        private const string ShotPrefix = MapLabPreviewRenderer.OutputFolder + "/MapLab_elevation_";
        private const int ShotWidth = 1600;
        private const int ShotHeight = 900;
        private const int MarkRadius = 1;
        private const int StandeePixelsPerUnit = 20;
        private const int ActorPixelsWide = 12;
        private const int ActorPixelsHigh = 24;
        private const int TreePixelsWide = 28;
        private const int TreePixelsHigh = 56;
        private const float PlateauHeight = 1.5f;
        private const float PlateauLip = 0.4f;
        private const float TierHeight = 1f;
        private const float TierScale = 0.45f;
        private const float FineSortSlice = 0.25f;
        private const float ScanStep = 0.01f;
        private const float SnapGap = 0.0005f;
        private const float CameraMargin = 2.6f;
        private const float CameraLift = 1.4f;
        private const float TintAmount = 0.4f;
        private static readonly Color GroundColor = new Color(0.33f, 0.45f, 0.3f);
        private static readonly Color BackgroundColor = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color TrunkColor = new Color(0.45f, 0.3f, 0.18f);
        private static readonly Color CanopyColor = new Color(0.2f, 0.6f, 0.32f);
        private static readonly Color32 HiddenMark = new Color32(255, 0, 255, 255);
        private static readonly Color32 ShowsMark = new Color32(255, 0, 0, 255);
        private static readonly Color32 BlockedTint = new Color32(230, 40, 40, 255);
        private static readonly Color32 CliffColor = new Color32(255, 220, 40, 255);
        private static readonly Color32 RampTint = new Color32(230, 60, 220, 255);
        private static readonly Vector2 SouthRampAnchor = new Vector2(3.3f, -1.5f);
        private static readonly Vector2 WestStairsAnchor = new Vector2(-6.5f, 1.4f);
        private static readonly Color32[] LevelTints = { new Color32(60, 120, 255, 255), new Color32(40, 220, 90, 255), new Color32(255, 150, 30, 255) };

        private static readonly Vector2[] PlateauPoints =
        {
            new Vector2(-5f, 0f), new Vector2(-1f, -0.5f), new Vector2(3f, -0.2f), new Vector2(5.2f, 0.6f),
            new Vector2(5.4f, 3.2f), new Vector2(2.5f, 4.6f), new Vector2(-2.5f, 4.5f), new Vector2(-5.3f, 2.8f)
        };

        private sealed class ProbeStandee
        {
            public string Name;
            public string Colour;
            public ExtrudedShape Surface;
            public RampShape Ramp;
            public Vector2 Ground;
            public SpriteRenderer Renderer;
            public bool Tree;
        }
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Elevation Sort Report", false, 107)]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup().Where(scene => !string.IsNullOrEmpty(scene.path)).ToArray();
            try
            {
                Debug.Log(Run());
            }
            finally
            {
                if (setup.Length == 0) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                else
                {
                    if (!setup.Any(scene => scene.isActive)) setup[0].isActive = true;
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
            EditorUtility.RevealInFinder(ReportPath);
        }

        public static void ReportBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(Run()));
        }

        public static string Run()
        {
            var timer = Stopwatch.StartNew();
            var owned = new List<Object>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Camera camera = CreateCamera();
                CreateLight();
                CreateGround(owned);
                Transform root = new GameObject("[MapLab] Elevation Probe").transform;
                ExtrudedShape plateau = CreatePlateau(root);
                ExtrudedShape tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
                tier.name = "Tier";
                List<RampShape> ramps = CreateRamps(plateau);
                List<ProbeStandee> standees = PlaceStandees(root, plateau, tier, ramps, owned);
                Rect view = View(plateau);

                var report = new StringBuilder();
                report.AppendLine($"MapLab elevation sort probe (P0), {DateTime.Now:yyyy-MM-dd HH:mm}");
                report.AppendLine($"Scene: Plateau height {plateau.Height:0.##}, walk on top, drawn as {PointShapeEditor.PieceLayout(plateau)} ({PieceCount(plateau)} pieces); Tier height {tier.Height:0.##} on it (top at {tier.TopElevation:0.##}), drawn as {PointShapeEditor.PieceLayout(tier)} ({PieceCount(tier)} pieces)");
                report.AppendLine($"Surface sort bias (LikeATier moves an actor's sort point down by this): Plateau {plateau.SurfaceSortBias:0.###}, Tier {tier.SurfaceSortBias:0.###}, " + string.Join(", ", ramps.Select(ramp => $"{ramp.name} {ramp.SurfaceSortBias:0.###}")));
                report.AppendLine("Ramps: " + string.Join("; ", ramps.Select(ramp => $"{ramp.name} ({ramp.Style}, {ramp.Width:0.##} wide, {ramp.Length:0.##} long, {ramp.SlopeDegrees:0} degrees, {ramp.LowElevation:0.##} to {ramp.HighElevation:0.##})")));
                report.AppendLine($"Check: every {MapLabElevationSortCheck.SampleStep} units of each actor's and tree's visible sprite, Unity's draw order (lower sort point y draws later) against the real depth (ground y; a top surface point minus its height; a wall at its footprint edge). Gaps under {MapLabElevationSortCheck.DepthTolerance} are ignored.");
                report.AppendLine("Marks in the _misses images: magenta = drawn behind something it should be in front of, red = drawn over something that should cover it.");
                report.AppendLine("Standees: " + string.Join("; ", standees.Select(standee => $"{standee.Name} ({standee.Colour}, ground {standee.Ground.x:0.##},{standee.Ground.y:0.##})")));

                foreach (ElevationSortVariant variant in Enum.GetValues(typeof(ElevationSortVariant)))
                {
                    RunVariant(report, camera, view, plateau, tier, ramps, standees, variant, variant.ToString());
                }

                SetSortSlice(plateau, FineSortSlice);
                SetSortSlice(tier, FineSortSlice);
                foreach (var ramp in ramps) SetSortSlice(ramp, FineSortSlice);
                report.AppendLine();
                report.AppendLine($"Sort slice {FineSortSlice} on both shapes: Plateau {PieceCount(plateau)} pieces, Tier {PieceCount(tier)} pieces, surface sort bias Plateau {plateau.SurfaceSortBias:0.###}, Tier {tier.SurfaceSortBias:0.###}");
                RunVariant(report, camera, view, plateau, tier, ramps, standees, ElevationSortVariant.LikeATier, $"{ElevationSortVariant.LikeATier}_FineSlice");

                report.AppendLine();
                AppendDemo(report);

                report.AppendLine();
                report.AppendLine($"Done in {timer.ElapsedMilliseconds} ms");
                Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
                File.WriteAllText(ReportPath, report.ToString());
                return $"[MapLab] Elevation sort probe written to {ReportPath}\n{report}";
            }
            finally
            {
                foreach (var item in owned) Object.DestroyImmediate(item);
            }
        }

        public static Sprite ActorSprite(Color color)
        {
            var texture = NewTexture(ActorPixelsWide, ActorPixelsHigh);
            Color outline = color * 0.45f;
            outline.a = 1f;
            Color head = Color.Lerp(color, Color.white, 0.35f);
            for (int y = 0; y < ActorPixelsHigh; y++)
            {
                for (int x = 0; x < ActorPixelsWide; x++)
                {
                    bool corner = (x < 2 || x >= ActorPixelsWide - 2) && (y < 2 || y >= ActorPixelsHigh - 2);
                    bool edge = x == 0 || y == 0 || x == ActorPixelsWide - 1 || y == ActorPixelsHigh - 1;
                    Color pixel = corner ? Color.clear : edge ? outline : y >= ActorPixelsHigh - 8 ? head : color;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, ActorPixelsWide, ActorPixelsHigh), new Vector2(0.5f, 0f), StandeePixelsPerUnit);
        }

        public static SpriteRenderer CreateActor(string name, Transform parent, Vector2 ground, float elevation, float sortOffset, Sprite sprite, int sortingOrder)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = ground;

            var anchor = new GameObject(AnchorName).transform;
            anchor.SetParent(root, false);
            anchor.gameObject.AddComponent<SortingGroup>().sortingOrder = sortingOrder;

            var visual = new GameObject(StandeeVisualName).transform;
            visual.SetParent(anchor, false);
            var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            SetSortOffset(renderer, elevation, sortOffset);
            return renderer;
        }

        public static void SetSortOffset(SpriteRenderer actor, float elevation, float sortOffset)
        {
            actor.transform.parent.localPosition = new Vector3(0f, sortOffset, 0f);
            actor.transform.localPosition = new Vector3(0f, elevation - sortOffset, 0f);
        }

        public static float SortOffset(ElevationSortVariant variant, RampShape ramp, Vector2 ground)
        {
            switch (variant)
            {
                case ElevationSortVariant.LikeATier:
                    return -ramp.SurfaceSortBias;
                case ElevationSortVariant.SnapToSlice:
                    var visual = new Vector2(ground.x, ground.y + ramp.HeightAt(ground) + ScanStep);
                    return MapLabElevationSortCheck.TopPieceSortY(ramp, visual, out float sortY) ? Mathf.Min(0f, sortY - SnapGap - ground.y) : 0f;
                default:
                    return 0f;
            }
        }

        public static float SortOffset(ElevationSortVariant variant, ExtrudedShape surface, Vector2 ground)
        {
            if (surface == null) return 0f;
            switch (variant)
            {
                case ElevationSortVariant.LikeATier:
                    return -surface.SurfaceSortBias;
                case ElevationSortVariant.SnapToSlice:
                    var visual = new Vector2(ground.x, ground.y + surface.TopElevation + ScanStep);
                    return MapLabElevationSortCheck.TopPieceSortY(surface, visual, out float sortY) ? Mathf.Min(0f, sortY - SnapGap - ground.y) : 0f;
                default:
                    return 0f;
            }
        }
        #endregion

        #region [Private Methods]
        private static void RunVariant(StringBuilder report, Camera camera, Rect view, ExtrudedShape plateau, ExtrudedShape tier, List<RampShape> ramps, List<ProbeStandee> standees, ElevationSortVariant variant, string label)
        {
            foreach (var standee in standees.Where(standee => !standee.Tree))
            {
                float offset = standee.Ramp != null ? SortOffset(variant, standee.Ramp, standee.Ground) : SortOffset(variant, standee.Surface, standee.Ground);
                SetSortOffset(standee.Renderer, StandeeElevation(standee), offset);
            }
            SortingGroup.UpdateAllSortingGroups();

            var check = new MapLabElevationSortCheck();
            check.AddShape(plateau);
            check.AddShape(tier);
            foreach (var ramp in ramps) check.AddRamp(ramp);
            foreach (var standee in standees) check.AddStandee(standee.Name, standee.Ground.y, SortPointY(standee), standee.Renderer);
            List<ElevationSortMiss> misses = check.Run();

            Texture2D shot = Capture(camera, view);
            string clean = Save(shot, $"{label}.png");
            Mark(shot, view, misses);
            string marked = Save(shot, $"{label}_misses.png");
            Object.DestroyImmediate(shot);
            AppendVariant(report, label, standees, misses, check.Samples, clean, marked);
        }

        private static void AppendDemo(StringBuilder report)
        {
            if (!File.Exists(MapLabSceneBuilder.ScenePath))
            {
                report.AppendLine("Demo: not built, skipped");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            MapLabMap map = MapLabWalkableBaker.FindMaps(scene).FirstOrDefault(candidate => candidate.WalkableData != null);
            if (map == null)
            {
                report.AppendLine("Demo: no baked map, skipped");
                return;
            }

            string recomputed = JsonUtility.ToJson(MapLabWalkableBaker.Compute(map), true);
            report.AppendLine($"Demo ({MapLabSceneBuilder.ScenePath}, opened in memory, not saved): Walkable.json recomputed is {(recomputed == map.WalkableData.text ? "identical to the file on disk" : "DIFFERENT from the file on disk")}");

            var raised = map.GetComponentsInChildren<ExtrudedShape>().Where(shape => shape.Type == ExtrudedShapeType.Plateau && shape.Height > 0f && !IsBox(shape)).ToList();
            foreach (var shape in raised)
            {
                var serialized = new SerializedObject(shape);
                serialized.FindProperty("_walkOnTop").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var shape in raised) shape.Rebuild();
            bool unchanged = JsonUtility.ToJson(MapLabWalkableBaker.Compute(map), true) == recomputed;
            report.AppendLine($"Walk On Top turned on for {string.Join(", ", raised.Select(shape => shape.name))}; Walkable.json still identical: {unchanged}");
            foreach (var shape in raised)
            {
                List<Vector2> outline = shape.Footprint();
                Rect area = Bounds(outline);
                ExtrudedShapeMenu.AddRamp(shape, RampFormula.Project(outline, new Vector2(area.center.x, area.yMin - 1f)));
            }
            report.AppendLine($"Added a ramp on the south side of each: {string.Join(", ", map.GetComponentsInChildren<RampShape>().Select(ramp => $"{ramp.transform.parent.name}/{ramp.name} {ramp.Length:0.##} long, {ramp.SlopeDegrees:0} degrees"))}");

            var timer = Stopwatch.StartNew();
            WalkableAreaJson walkable = MapLabWalkableBaker.Compute(map);
            WalkableElevationJson data = MapLabElevationBaker.Compute(map, walkable);
            long bakeMs = timer.ElapsedMilliseconds;
            MapLabElevationBaker.Report heights = MapLabElevationBaker.Scan(map);
            byte[] cells = MapLabElevationBaker.DecodeLevels(data.cellLevel, data.CellCount);
            foreach (var level in data.levels)
            {
                int count = cells.Count(cell => cell == level.id);
                report.AppendLine($"  level {level.id} {level.name}: height {level.height:0.##}, sort bias {level.sortBias:0.###}, stands on {level.below}, {count} cells");
            }
            foreach (var ramp in data.ramps)
            {
                report.AppendLine($"  ramp {ramp.id} {ramp.name}: {ramp.lower} -> {ramp.upper}, {ramp.lowHeight:0.##} to {ramp.highHeight:0.##}, {cells.Count(cell => cell == ramp.id)} cells");
            }
            report.AppendLine($"  blocked {cells.Count(cell => cell == WalkableElevationJson.Blocked)} cells, {data.cliffs.Length} cliff outlines, {data.cliffs.Sum(cliff => cliff.openings.Length)} openings, islands: {(heights.Islands.Count == 0 ? "none" : string.Join(", ", heights.Islands))}, overlaps: {(heights.Overlaps.Count == 0 ? "none" : string.Join("; ", heights.Overlaps))}, elevation bake {bakeMs} ms");

            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            if (camera == null) return;
            Rect view = DemoView(walkable);
            Texture2D shot = Capture(camera, view);
            Texture2D raisedShot = Capture(camera, view);
            PaintLevels(shot, view, data, cells, false);
            PaintLevels(raisedShot, view, data, cells, true);
            report.AppendLine($"  images: {Save(shot, "demo.png")} (cells on the ground plane), {Save(raisedShot, "demo_raised.png")} (each cell lifted by its level height, where it is drawn); blue/green/orange = levels 1/2/3, magenta = ramps, red = blocked, yellow = cliffs");
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(raisedShot);
        }

        private static bool IsBox(ExtrudedShape shape)
        {
            if (shape.Points.Count != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                if (!shape.IsSharp(i)) return false;
            }
            return true;
        }

        private static Rect DemoView(WalkableAreaJson walkable)
        {
            var area = new Rect(walkable.bounds.x, walkable.bounds.y, walkable.bounds.width, walkable.bounds.height);
            float halfHeight = Mathf.Max(area.height * 0.5f, area.width * 0.5f * ShotHeight / ShotWidth);
            float halfWidth = halfHeight * ShotWidth / ShotHeight;
            return new Rect(area.center.x - halfWidth, area.center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        }

        private static void PaintLevels(Texture2D image, Rect view, WalkableElevationJson data, byte[] cells, bool raised)
        {
            var heights = data.levels.ToDictionary(level => level.id, level => level.height);
            var ramps = (data.ramps ?? new ElevationRamp[0]).ToDictionary(ramp => ramp.id);
            Color32[] original = image.GetPixels32();
            Color32[] pixels = image.GetPixels32();
            var painted = new float[pixels.Length];
            for (int i = 0; i < painted.Length; i++) painted[i] = float.MinValue;
            float scale = ShotWidth / view.width;
            for (int row = 0; row < data.rows; row++)
            {
                for (int column = 0; column < data.columns; column++)
                {
                    byte level = cells[row * data.columns + column];
                    if (level == 0) continue;
                    bool onRamp = ramps.TryGetValue(level, out ElevationRamp ramp);
                    Color32 tint = level == WalkableElevationJson.Blocked ? BlockedTint : onRamp ? RampTint : LevelTints[(level - 1) % LevelTints.Length];
                    float x = data.bounds.x + column * data.cellSize;
                    float lift = !raised ? 0f : onRamp ? MapLabElevationBaker.RampHeight(ramp, ElevationMap.CellCenter(data.bounds, data.cellSize, column, row)) : heights.TryGetValue(level, out float height) ? height : 0f;
                    float y = data.bounds.y + row * data.cellSize + lift;
                    int xMin = Mathf.Clamp(Mathf.FloorToInt((x - view.x) * scale), 0, ShotWidth);
                    int xMax = Mathf.Clamp(Mathf.CeilToInt((x + data.cellSize - view.x) * scale), 0, ShotWidth);
                    int yMin = Mathf.Clamp(Mathf.FloorToInt((y - view.y) * scale), 0, ShotHeight);
                    int yMax = Mathf.Clamp(Mathf.CeilToInt((y + data.cellSize - view.y) * scale), 0, ShotHeight);
                    for (int py = yMin; py < yMax; py++)
                    {
                        for (int px = xMin; px < xMax; px++)
                        {
                            int index = py * ShotWidth + px;
                            if (lift < painted[index]) continue;
                            painted[index] = lift;
                            pixels[index] = Color32.Lerp(original[index], tint, TintAmount);
                        }
                    }
                }
            }
            foreach (var cliff in data.cliffs)
            {
                for (int i = 0; i < cliff.points.Length; i++)
                {
                    Vector2 lift = raised ? Vector2.up * heights[cliff.level] : Vector2.zero;
                    Vector2 from = (cliff.points[i] + lift - view.position) * scale;
                    Vector2 to = (cliff.points[(i + 1) % cliff.points.Length] + lift - view.position) * scale;
                    int steps = Mathf.CeilToInt(Vector2.Distance(from, to));
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector2 point = steps == 0 ? from : Vector2.Lerp(from, to, step / (float)steps);
                        int px = Mathf.FloorToInt(point.x);
                        int py = Mathf.FloorToInt(point.y);
                        if (px >= 0 && py >= 0 && px < ShotWidth && py < ShotHeight) pixels[py * ShotWidth + px] = CliffColor;
                    }
                }
            }
            image.SetPixels32(pixels);
            image.Apply();
        }

        private static List<RampShape> CreateRamps(ExtrudedShape plateau)
        {
            List<Vector2> outline = plateau.Footprint();
            RampShape south = ExtrudedShapeMenu.AddRamp(plateau, RampFormula.Project(outline, SouthRampAnchor));
            south.name = "South Ramp";
            south.Configure(south.Position, 1.6f, 2.4f, 0f);
            RampShape west = ExtrudedShapeMenu.AddRamp(plateau, RampFormula.Project(outline, WestStairsAnchor));
            west.name = "West Stairs";
            var serialized = new SerializedObject(west);
            serialized.FindProperty("_style").enumValueIndex = (int)RampStyle.Stairs;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            west.Configure(west.Position, 1.6f, 2f, 0f);
            return new List<RampShape> { south, west };
        }

        private static Vector2 OnRamp(RampShape ramp, float across, float down)
        {
            RampCorners corners = ramp.WorldCorners();
            return corners.TopLeft + (corners.TopRight - corners.TopLeft) * across + (corners.BottomLeft - corners.TopLeft) * down;
        }

        private static ProbeStandee RampActor(Transform root, string name, string colour, Color color, RampShape ramp, Vector2 ground, List<Object> owned)
        {
            Sprite sprite = ActorSprite(color);
            owned.Add(sprite);
            owned.Add(sprite.texture);
            return new ProbeStandee
            {
                Name = name,
                Colour = colour,
                Ramp = ramp,
                Ground = ground,
                Renderer = CreateActor(name, root, ground, ramp.HeightAt(ground), 0f, sprite, ExtrudedShapeMenu.CharacterOrder)
            };
        }

        private static void SetSortSlice(RampShape ramp, float slice)
        {
            var serialized = new SerializedObject(ramp);
            serialized.FindProperty("_sortSlice").floatValue = slice;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ramp.Rebuild();
        }

        private static void SetSortSlice(ExtrudedShape shape, float slice)
        {
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_sortSlice").floatValue = slice;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.GetUniversalAdditionalCameraData();
            return camera;
        }

        private static void CreateLight()
        {
            var light = new GameObject("Global Light").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadowsEnabled = false;
            light.targetSortingLayers = SortingLayer.layers.Select(layer => layer.id).ToArray();
        }

        private static void CreateGround(List<Object> owned)
        {
            Texture2D texture = NewTexture(4, 4);
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();
            owned.Add(texture);
            var renderer = new GameObject("Ground").AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            owned.Add(renderer.sprite);
            renderer.color = GroundColor;
            renderer.sortingOrder = -100;
            renderer.transform.localScale = new Vector3(40f, 30f, 1f);
        }

        private static ExtrudedShape CreatePlateau(Transform root)
        {
            ExtrudedShape plateau = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, "Plateau", root, Vector3.zero, PlateauPoints, PlateauHeight, PlateauLip, ExtrudedShapeMenu.CharacterOrder);
            var serialized = new SerializedObject(plateau);
            serialized.FindProperty("_walkOnTop").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            plateau.Rebuild();
            return plateau;
        }

        private static List<ProbeStandee> PlaceStandees(Transform root, ExtrudedShape plateau, ExtrudedShape tier, List<RampShape> ramps, List<Object> owned)
        {
            List<Vector2> top = WorldFootprint(plateau);
            List<Vector2> upper = WorldFootprint(tier);
            Rect area = Bounds(top);
            Rect tierArea = Bounds(upper);
            float midY = (area.yMin + area.yMax) * 0.5f;
            float frontX = area.xMin + area.width * 0.3f;
            float westX = area.xMin + area.width * 0.15f;
            float behindX = tierArea.center.x + 0.4f;
            float onTierX = tierArea.center.x - 0.5f;
            float behindGroundX = area.center.x - 1.5f;
            float frontGroundX = area.center.x + 1.2f;

            var front = new Vector2(frontX, South(top, frontX) + 0.2f);
            var middle = new Vector2(westX, (South(top, westX) + North(top, westX)) * 0.5f);
            var beforeTier = new Vector2(tierArea.center.x, South(upper, tierArea.center.x) - 0.25f);
            var behindTier = new Vector2(behindX, Mathf.Min(North(upper, behindX) + 0.25f, North(top, behindX) - 0.15f));
            var onTier = new Vector2(onTierX, (South(upper, onTierX) + North(upper, onTierX)) * 0.5f);
            var eastEdge = new Vector2(East(top, midY) - 0.3f, midY);
            float treeFrontX = front.x + 0.35f;
            float treeEastY = eastEdge.y - 0.2f;

            var standees = new List<ProbeStandee>
            {
                Actor(root, "A1 on top, front edge", "blue", new Color(0.25f, 0.45f, 1f), plateau, front, owned),
                Actor(root, "A2 on top, middle", "cyan", new Color(0.2f, 0.85f, 0.95f), plateau, middle, owned),
                Actor(root, "A3 on top, in front of the tier", "orange", new Color(1f, 0.55f, 0.1f), plateau, beforeTier, owned),
                Actor(root, "A4 on top, behind the tier", "yellow", new Color(1f, 0.9f, 0.2f), plateau, behindTier, owned),
                Actor(root, "A5 on the tier", "purple", new Color(0.6f, 0.35f, 0.95f), tier, onTier, owned),
                Actor(root, "A6 on top, east edge", "white", new Color(0.95f, 0.95f, 0.95f), plateau, eastEdge, owned),
                Actor(root, "G1 ground, behind", "brown", new Color(0.6f, 0.4f, 0.25f), null, new Vector2(behindGroundX, North(top, behindGroundX) + 0.35f), owned),
                Actor(root, "G2 ground, in front", "pink", new Color(1f, 0.6f, 0.75f), null, new Vector2(frontGroundX, South(top, frontGroundX) - 0.4f), owned),
                Actor(root, "G3 ground, west side", "teal", new Color(0.1f, 0.6f, 0.55f), null, new Vector2(West(top, midY) - 0.4f, midY + 0.3f), owned),
                Tree(root, "T1 tree in front of the edge", new Vector2(treeFrontX, South(top, treeFrontX) - 0.3f), owned),
                Tree(root, "T2 tree beside the east edge", new Vector2(East(top, treeEastY) + 0.55f, treeEastY), owned),
                RampActor(root, "R1 on the ramp, bottom", "lime", new Color(0.6f, 1f, 0.3f), ramps[0], OnRamp(ramps[0], 0.5f, 0.85f), owned),
                RampActor(root, "R2 on the ramp, middle", "coral", new Color(1f, 0.45f, 0.35f), ramps[0], OnRamp(ramps[0], 0.5f, 0.5f), owned),
                RampActor(root, "R3 on the ramp, top", "navy", new Color(0.3f, 0.35f, 0.75f), ramps[0], OnRamp(ramps[0], 0.5f, 0.15f), owned),
                RampActor(root, "S1 on the stairs, middle", "gold", new Color(0.95f, 0.75f, 0.2f), ramps[1], OnRamp(ramps[1], 0.5f, 0.5f), owned),
                Actor(root, "G4 ground, beside the ramp", "silver", new Color(0.72f, 0.72f, 0.78f), null, OnRamp(ramps[0], 1.35f, 0.5f), owned)
            };
            return standees;
        }

        private static ProbeStandee Actor(Transform root, string name, string colour, Color color, ExtrudedShape surface, Vector2 ground, List<Object> owned)
        {
            Sprite sprite = ActorSprite(color);
            owned.Add(sprite);
            owned.Add(sprite.texture);
            return new ProbeStandee
            {
                Name = name,
                Colour = colour,
                Surface = surface,
                Ground = ground,
                Renderer = CreateActor(name, root, ground, Elevation(surface), 0f, sprite, ExtrudedShapeMenu.CharacterOrder)
            };
        }

        private static ProbeStandee Tree(Transform root, string name, Vector2 ground, List<Object> owned)
        {
            Texture2D texture = NewTexture(TreePixelsWide, TreePixelsHigh);
            var center = new Vector2(TreePixelsWide * 0.5f, 37f);
            for (int y = 0; y < TreePixelsHigh; y++)
            {
                for (int x = 0; x < TreePixelsWide; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    bool trunk = y < 26 && x >= 11 && x < 17;
                    Color pixel = distance <= 13.5f ? (distance > 12.5f ? CanopyColor * 0.6f : CanopyColor) : trunk ? TrunkColor : Color.clear;
                    if (pixel.a > 0f) pixel.a = 1f;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply();
            owned.Add(texture);

            var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(root, false);
            renderer.transform.position = ground;
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, TreePixelsWide, TreePixelsHigh), new Vector2(0.5f, 0f), StandeePixelsPerUnit);
            owned.Add(renderer.sprite);
            renderer.sortingOrder = ExtrudedShapeMenu.CharacterOrder;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            return new ProbeStandee { Name = name, Colour = "green", Ground = ground, Renderer = renderer, Tree = true };
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static float StandeeElevation(ProbeStandee standee)
        {
            return standee.Ramp != null ? standee.Ramp.HeightAt(standee.Ground) : Elevation(standee.Surface);
        }

        private static float Elevation(ExtrudedShape surface)
        {
            return surface != null ? surface.TopElevation : 0f;
        }

        private static float SortPointY(ProbeStandee standee)
        {
            return standee.Tree ? standee.Renderer.transform.position.y : standee.Renderer.transform.parent.position.y;
        }

        private static int PieceCount(ExtrudedShape shape)
        {
            Transform visual = shape.transform.Find(ExtrudedShape.VisualName);
            return visual != null ? visual.childCount : 0;
        }

        private static Rect View(ExtrudedShape plateau)
        {
            Rect area = Bounds(WorldFootprint(plateau));
            foreach (var ramp in plateau.GetComponentsInChildren<RampShape>())
            {
                foreach (var point in ramp.WorldCorners().Footprint()) area = Rect.MinMaxRect(Mathf.Min(area.xMin, point.x), Mathf.Min(area.yMin, point.y), Mathf.Max(area.xMax, point.x), Mathf.Max(area.yMax, point.y));
            }
            float size = area.height * 0.5f + CameraMargin;
            float halfWidth = size * ShotWidth / ShotHeight;
            var center = new Vector2(area.center.x, area.center.y + CameraLift);
            return new Rect(center.x - halfWidth, center.y - size, halfWidth * 2f, size * 2f);
        }

        private static Texture2D Capture(Camera camera, Rect view)
        {
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(view.center.x, view.center.y, -10f);
                camera.orthographicSize = view.height * 0.5f;
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                image.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
            return image;
        }

        private static void Mark(Texture2D image, Rect view, List<ElevationSortMiss> misses)
        {
            foreach (var miss in misses)
            {
                int px = Mathf.RoundToInt((miss.Point.x - view.xMin) / view.width * ShotWidth);
                int py = Mathf.RoundToInt((miss.Point.y - view.yMin) / view.height * ShotHeight);
                Color32 color = miss.Kind == ElevationSortMissKind.Hidden ? HiddenMark : ShowsMark;
                for (int dy = -MarkRadius; dy <= MarkRadius; dy++)
                {
                    for (int dx = -MarkRadius; dx <= MarkRadius; dx++)
                    {
                        int x = px + dx;
                        int y = py + dy;
                        if (x >= 0 && y >= 0 && x < ShotWidth && y < ShotHeight) image.SetPixel(x, y, color);
                    }
                }
            }
            image.Apply();
        }

        private static string Save(Texture2D image, string suffix)
        {
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            string path = ShotPrefix + suffix;
            File.WriteAllBytes(path, image.EncodeToPNG());
            return path;
        }

        private static void AppendVariant(StringBuilder report, string label, List<ProbeStandee> standees, List<ElevationSortMiss> misses, int samples, string clean, string marked)
        {
            int hidden = misses.Count(miss => miss.Kind == ElevationSortMissKind.Hidden);
            float worst = misses.Count > 0 ? misses.Max(miss => miss.DepthGap) : 0f;
            report.AppendLine();
            report.AppendLine($"[{label}] {misses.Count} misses in {samples} samples (hidden {hidden}, shows through {misses.Count - hidden}), worst depth gap {worst:0.###}");
            foreach (var standee in standees)
            {
                List<ElevationSortMiss> own = misses.Where(miss => miss.Standee == standee.Name).ToList();
                string surface = standee.Tree ? "ground tree" : standee.Ramp != null ? $"on {standee.Ramp.name} at height {StandeeElevation(standee):0.##}, sort offset {SortPointY(standee) - standee.Ground.y:0.###}" : standee.Surface != null ? $"on {standee.Surface.name}, sort offset {SortPointY(standee) - standee.Ground.y:0.###}" : "ground";
                if (own.Count == 0)
                {
                    report.AppendLine($"  ok    {standee.Name} ({surface})");
                    continue;
                }
                ElevationSortMiss first = own.OrderByDescending(miss => miss.DepthGap).First();
                string others = string.Join(", ", own.GroupBy(miss => miss.Other).OrderByDescending(group => group.Count()).Take(3).Select(group => $"{group.Key} x{group.Count()}"));
                int ownHidden = own.Count(miss => miss.Kind == ElevationSortMissKind.Hidden);
                report.AppendLine($"  MISS  {standee.Name} ({surface}): hidden {ownHidden}, shows through {own.Count - ownHidden}, worst gap {first.DepthGap:0.###}; by {others}");
            }
            report.AppendLine($"  images: {clean}, {marked}");
        }

        private static List<Vector2> WorldFootprint(ExtrudedShape shape)
        {
            Vector2 offset = shape.transform.position;
            return shape.Footprint().Select(point => point + offset).ToList();
        }

        private static Rect Bounds(List<Vector2> polygon)
        {
            Vector2 min = polygon[0];
            Vector2 max = polygon[0];
            foreach (var point in polygon)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool Inside(List<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static float South(List<Vector2> polygon, float x)
        {
            Rect area = Bounds(polygon);
            for (float y = area.yMin - ScanStep; y <= area.yMax; y += ScanStep)
            {
                if (Inside(polygon, new Vector2(x, y))) return y;
            }
            return area.yMin;
        }

        private static float North(List<Vector2> polygon, float x)
        {
            Rect area = Bounds(polygon);
            for (float y = area.yMax + ScanStep; y >= area.yMin; y -= ScanStep)
            {
                if (Inside(polygon, new Vector2(x, y))) return y;
            }
            return area.yMax;
        }

        private static float East(List<Vector2> polygon, float y)
        {
            Rect area = Bounds(polygon);
            for (float x = area.xMax + ScanStep; x >= area.xMin; x -= ScanStep)
            {
                if (Inside(polygon, new Vector2(x, y))) return x;
            }
            return area.xMax;
        }

        private static float West(List<Vector2> polygon, float y)
        {
            Rect area = Bounds(polygon);
            for (float x = area.xMin - ScanStep; x <= area.xMax; x += ScanStep)
            {
                if (Inside(polygon, new Vector2(x, y))) return x;
            }
            return area.xMin;
        }
        #endregion
    }
}
