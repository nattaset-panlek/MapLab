using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Editor;
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

namespace MapLab.Samples.Elevation.Editor
{
    public static class ElevationDemoBuilder
    {
        #region [Variables/Fields]
        public const string SceneName = "MapLab_Elevation_Demo.unity";
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_elevation_walk.txt";

        private const string EditorAssemblyName = "MapLab.Samples.Elevation.Editor";
        private const string SheetPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_elevation_walk.png";
        private const string GroundArt = "Elevation_Ground";
        private const string HeroArt = "Elevation_Hero";
        private const string MarkerArt = "Elevation_Marker";
        private const int ArtPixelsPerUnit = 20;
        private const int FrameWidth = 960;
        private const int FrameHeight = 540;
        private const int SheetColumns = 2;
        private const float CameraSize = 6.4f;
        private const float StepSeconds = 0.05f;
        private const int MaxSteps = 4000;
        private const float PlateauHeight = 1.5f;
        private const float TierHeight = 1f;
        private const float TierScale = 0.5f;
        private static readonly Rect MapArea = new Rect(-12f, -8f, 24f, 17f);
        private static readonly Vector2 HeroStart = new Vector2(-5f, -5.5f);
        private static readonly Color BackgroundColor = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Vector2[] PlateauPoints =
        {
            new Vector2(-6f, -1.5f), new Vector2(-1f, -2.2f), new Vector2(4f, -1.8f), new Vector2(6.5f, -0.5f),
            new Vector2(6.5f, 3f), new Vector2(3f, 4.5f), new Vector2(-3f, 4.4f), new Vector2(-6.5f, 2.5f)
        };
        private static string _sampleFolder;
        #endregion

        #region [Properties]
        public static string SampleFolder => _sampleFolder ??= FindSampleFolder();
        public static string ScenePath => $"{SampleFolder}/Scenes/{SceneName}";
        private static string ArtFolder => $"{SampleFolder}/Art";
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Samples/Build Elevation Demo", false, 200)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Build());
        }

        [MenuItem("Tools/MapLab/Samples/Render Elevation Walk", false, 201)]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(RenderWalk());
            EditorUtility.RevealInFinder(ReportPath);
        }

        public static void BuildBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(Build()));
        }

        public static void BuildAndRenderBatch()
        {
            MapLabSceneBuilder.RunBatch(() =>
            {
                Debug.Log(Build());
                Debug.Log(RenderWalk());
            });
        }

        public static string Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MapLabOutputPaths.EnsureFolder($"{SampleFolder}/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Camera camera = CreateCamera();
            CreateLight();
            CreateGround();
            MapLabMap map = CreateMap(out ExtrudedShape plateau, out ExtrudedShape tier, out List<RampShape> ramps);
            EditorSceneManager.SaveScene(scene);
            string bake = MapLabWalkableBaker.Bake(map);
            Transform marker = CreateMarker();
            ElevationBody hero = CreateHero(map, camera, marker);
            EditorSceneManager.SaveScene(scene);
            return $"[MapLab] Built {ScenePath}: Plateau {plateau.Height:0.##} + {tier.name} {tier.Height:0.##} with {ramps.Count} ramps, hero '{hero.name}' at {hero.Ground}\n{bake}";
        }

        public static string RenderWalk()
        {
            if (!File.Exists(ScenePath)) throw new InvalidOperationException($"{ScenePath} does not exist. Run Tools > MapLab > Samples > Build Elevation Demo first.");
            var timer = Stopwatch.StartNew();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            ElevationBody hero = scene.GetRootGameObjects().Select(root => root.GetComponentInChildren<ElevationBody>()).First(body => body != null);
            var controller = hero.GetComponent<ElevationDemoController>();
            MapLabMap map = MapLabWalkableBaker.FindMaps(scene).First();
            ExtrudedShape top = map.GetComponentsInChildren<ExtrudedShape>().OrderByDescending(shape => shape.TopElevation).First();
            ElevationMap elevation = hero.Map;

            var report = new StringBuilder();
            var events = new List<string>();
            bool capture = false;
            hero.OnRampEntered += ramp => { events.Add($"entered ramp {Name(elevation, ramp)}"); capture = true; };
            hero.OnRampExited += ramp => events.Add($"left ramp {Name(elevation, ramp)}");
            hero.OnLevelChanged += (from, to) => { events.Add($"level {Name(elevation, from)} -> {Name(elevation, to)}"); capture = true; };

            hero.Teleport(HeroStart);
            Vector2 goal = Center(top);
            bool found = controller.WalkTo(goal);
            report.AppendLine($"MapLab elevation walk, {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"From {HeroStart} on {Name(elevation, hero.Level)} to {goal} on '{top.name}': path {(found ? $"found, {controller.Path.Count} points" : "NOT FOUND")}");
            report.AppendLine("Path: " + string.Join(" ", controller.Path.Select(point => $"({point.x:0.##},{point.y:0.##})")));

            var frames = new List<(Texture2D Image, string Label)> { (Capture(camera), $"start on {Name(elevation, hero.Level)}") };
            var sortChecks = new List<string> { SortCheck(map, hero, "start") };
            int steps = 0;
            while (controller.Walking && steps++ < MaxSteps)
            {
                controller.Step(StepSeconds);
                if (!capture) continue;
                capture = false;
                frames.Add((Capture(camera), events.Last()));
                sortChecks.Add(SortCheck(map, hero, events.Last()));
            }
            frames.Add((Capture(camera), $"end on {Name(elevation, hero.Level)}, height {hero.Height:0.##}"));
            sortChecks.Add(SortCheck(map, hero, "end"));
            report.AppendLine($"Walked {steps} steps of {StepSeconds}s; ended at ({hero.Ground.x:0.##},{hero.Ground.y:0.##}) on {Name(elevation, hero.Level)}, height {hero.Height:0.##}, sort bias {hero.SortBias:0.##}, {Vector2.Distance(hero.Ground, goal):0.###} from the goal");
            report.AppendLine("Events: " + string.Join("; ", events));
            report.AppendLine("Sort check per frame: " + string.Join(" | ", sortChecks));

            Vector2 feet = hero.Ground + Vector2.up * hero.Height;
            report.AppendLine(Pick(elevation, "click on the hero's feet", feet));
            report.AppendLine(Pick(elevation, "click on open ground", HeroStart));
            report.AppendLine(Pick(elevation, "click on the plateau top", Center(Base(map)) + Vector2.up * Base(map).TopElevation + Vector2.left * 5f));
            report.AppendLine(Pick(elevation, "click on a cliff wall", new Vector2(-1f, -2.2f + PlateauHeight * 0.5f)));

            string sheet = SaveSheet(frames);
            foreach (var frame in frames) Object.DestroyImmediate(frame.Image);
            report.AppendLine($"Frames: {sheet} ({frames.Count}: {string.Join(" | ", frames.Select(frame => frame.Label))})");
            report.AppendLine($"Done in {timer.ElapsedMilliseconds} ms");
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            File.WriteAllText(ReportPath, report.ToString());
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return $"[MapLab] Elevation walk written to {ReportPath}\n{report}";
        }
        #endregion

        #region [Private Methods]
        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, -0.2f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.GetUniversalAdditionalCameraData();
            return camera;
        }

        private static void CreateLight()
        {
            var light = new GameObject("Global Light").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.shadowsEnabled = false;
            light.targetSortingLayers = SortingLayer.layers.Select(layer => layer.id).ToArray();
        }

        private static void CreateGround()
        {
            var renderer = new GameObject("Ground").AddComponent<SpriteRenderer>();
            renderer.sprite = Art(GroundArt, 32, 32, new Vector2(0.5f, 0.5f), true, (x, y) =>
            {
                float noise = Mathf.PerlinNoise(x * 0.21f, y * 0.21f) * 0.12f;
                return new Color(0.34f + noise, 0.5f + noise, 0.3f + noise * 0.5f);
            });
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = new Vector2(MapArea.width + 8f, MapArea.height + 8f);
            renderer.sortingOrder = -100;
        }

        private static MapLabMap CreateMap(out ExtrudedShape plateau, out ExtrudedShape tier, out List<RampShape> ramps)
        {
            var root = new GameObject("[MapLab] Elevation Demo");
            var map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(map);
            serialized.FindProperty("_bounds").rectValue = MapArea;
            serialized.FindProperty("_cellSize").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            plateau = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, "Plateau", root.transform, Vector3.zero, PlateauPoints, PlateauHeight, 0.4f, ExtrudedShapeMenu.CharacterOrder);
            var shape = new SerializedObject(plateau);
            shape.FindProperty("_walkOnTop").boolValue = true;
            shape.ApplyModifiedPropertiesWithoutUndo();
            plateau.Rebuild();
            tier = ExtrudedShapeMenu.AddTier(plateau, TierScale, TierHeight);
            tier.name = "Tier";

            ramps = new List<RampShape>
            {
                Ramp(plateau, "South Ramp", new Vector2(1.5f, -3f), 2f, 2.6f, 0f, RampStyle.Ramp),
                Ramp(plateau, "West Stairs", new Vector2(-8f, 1f), 1.8f, 2.2f, 0f, RampStyle.Stairs),
                Ramp(tier, "Tier Ramp", new Vector2(-1.5f, -1f), 1.4f, 1.8f, 0f, RampStyle.Ramp)
            };
            return map;
        }

        private static RampShape Ramp(ExtrudedShape parent, string name, Vector2 near, float width, float length, float angle, RampStyle style)
        {
            RampShape ramp = ExtrudedShapeMenu.AddRamp(parent, RampFormula.Project(parent.Footprint(), parent.transform.InverseTransformPoint(near)));
            ramp.name = name;
            var serialized = new SerializedObject(ramp);
            serialized.FindProperty("_style").enumValueIndex = (int)style;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ramp.Configure(ramp.Position, width, length, angle);
            return ramp;
        }

        private static Transform CreateMarker()
        {
            var renderer = new GameObject("Click Marker").AddComponent<SpriteRenderer>();
            renderer.sprite = Art(MarkerArt, 16, 16, new Vector2(0.5f, 0.5f), false, (x, y) =>
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f));
                return distance <= 7.5f && distance >= 5f ? new Color(1f, 0.85f, 0.3f) : Color.clear;
            });
            renderer.sortingOrder = ExtrudedShapeMenu.CharacterOrder + 1;
            renderer.gameObject.SetActive(false);
            return renderer.transform;
        }

        private static ElevationBody CreateHero(MapLabMap map, Camera camera, Transform marker)
        {
            var hero = new GameObject("Hero");
            hero.transform.position = HeroStart;
            var anchor = new GameObject(ElevationBody.SortAnchorName).transform;
            anchor.SetParent(hero.transform, false);
            anchor.gameObject.AddComponent<SortingGroup>().sortingOrder = ExtrudedShapeMenu.CharacterOrder;
            var visual = new GameObject(ElevationBody.VisualName).transform;
            visual.SetParent(anchor, false);
            var sprite = visual.gameObject.AddComponent<SpriteRenderer>();
            sprite.sprite = Art(HeroArt, 14, 26, new Vector2(0.5f, 0f), false, HeroPixel);

            var body = hero.AddComponent<ElevationBody>();
            body.SetMap(map.ElevationData);
            body.SetParts(anchor, visual);
            var serialized = new SerializedObject(body);
            serialized.FindProperty("_elevationData").objectReferenceValue = map.ElevationData;
            serialized.FindProperty("_sortAnchor").objectReferenceValue = anchor;
            serialized.FindProperty("_visual").objectReferenceValue = visual;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var controller = hero.AddComponent<ElevationDemoController>();
            var settings = new SerializedObject(controller);
            settings.FindProperty("_camera").objectReferenceValue = camera;
            settings.FindProperty("_marker").objectReferenceValue = marker;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return body;
        }

        private static Color HeroPixel(int x, int y)
        {
            bool corner = (x < 2 || x > 11) && (y < 2 || y > 23);
            if (corner) return Color.clear;
            bool edge = x == 0 || x == 13 || y == 0 || y == 25;
            if (edge) return new Color(0.15f, 0.12f, 0.2f);
            if (y > 17) return new Color(0.98f, 0.82f, 0.68f);
            return y > 8 ? new Color(0.85f, 0.25f, 0.3f) : new Color(0.25f, 0.3f, 0.55f);
        }

        private static Sprite Art(string name, int width, int height, Vector2 pivot, bool tiled, Func<int, int, Color> paint)
        {
            string path = $"{ArtFolder}/{name}.png";
            if (!File.Exists(path))
            {
                MapLabOutputPaths.EnsureFolder(ArtFolder);
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++) texture.SetPixel(x, y, paint(x, y));
                }
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            Configure(path, pivot, tiled);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void Configure(string path, Vector2 pivot, bool tiled)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ArtPixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static ExtrudedShape Base(MapLabMap map)
        {
            return map.GetComponentsInChildren<ExtrudedShape>().First(shape => !shape.IsStacked);
        }

        private static Vector2 Center(ExtrudedShape shape)
        {
            List<Vector2> footprint = shape.Footprint();
            Vector2 min = footprint[0], max = footprint[0];
            foreach (var point in footprint)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return shape.transform.TransformPoint((min + max) * 0.5f);
        }

        private static string Pick(ElevationMap map, string label, Vector2 visual)
        {
            bool hit = map.ScreenToGround(visual, out Vector2 ground, out int region);
            return hit ? $"ScreenToGround {label} at ({visual.x:0.##},{visual.y:0.##}): ground ({ground.x:0.##},{ground.y:0.##}) on {Name(map, region)}" : $"ScreenToGround {label} at ({visual.x:0.##},{visual.y:0.##}): nothing to stand on there";
        }

        private static string Name(ElevationMap map, int region)
        {
            if (map.TryGetLevel(region, out ElevationLevel level)) return level.name;
            return map.TryGetRamp(region, out ElevationRamp ramp) ? ramp.name : region.ToString();
        }

        private static string SortCheck(MapLabMap map, ElevationBody hero, string label)
        {
            SortingGroup.UpdateAllSortingGroups();
            var check = new MapLabElevationSortCheck();
            foreach (var shape in map.GetComponentsInChildren<ExtrudedShape>()) check.AddShape(shape);
            foreach (var ramp in map.GetComponentsInChildren<RampShape>()) check.AddRamp(ramp);
            var renderer = hero.GetComponentInChildren<SpriteRenderer>();
            var anchor = hero.GetComponentInChildren<SortingGroup>();
            check.AddStandee(hero.name, hero.Ground.y, anchor.transform.position.y, renderer);
            List<ElevationSortMiss> misses = check.Run();
            if (misses.Count == 0) return $"{label}: ok";
            ElevationSortMiss worst = misses.OrderByDescending(miss => miss.DepthGap).First();
            string covering = string.Join("; ", check.Describe(worst.Point));
            return $"{label}: {misses.Count} misses, worst {worst.Kind} by {worst.Other} gap {worst.DepthGap:0.###} at ({worst.Point.x:0.##},{worst.Point.y:0.##}) (hero sort {anchor.transform.position.y:0.###}) [{covering}]";
        }

        private static Texture2D Capture(Camera camera)
        {
            RenderTexture previous = RenderTexture.active;
            var target = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                SortingGroup.UpdateAllSortingGroups();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
                image.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
            return image;
        }

        private static string SaveSheet(List<(Texture2D Image, string Label)> frames)
        {
            int rows = Mathf.CeilToInt(frames.Count / (float)SheetColumns);
            var sheet = new Texture2D(FrameWidth * SheetColumns, FrameHeight * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 24, 255);
            sheet.SetPixels32(fill);
            for (int i = 0; i < frames.Count; i++)
            {
                int column = i % SheetColumns;
                int row = rows - 1 - i / SheetColumns;
                sheet.SetPixels(column * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight, frames[i].Image.GetPixels());
            }
            sheet.Apply();
            Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
            File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            return SheetPath;
        }

        private static string FindSampleFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets($"{EditorAssemblyName} t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != EditorAssemblyName) continue;
                return Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
            }
            return $"{MapLabSceneBuilder.RootFolder}/Samples/Elevation";
        }
        #endregion
    }
}
