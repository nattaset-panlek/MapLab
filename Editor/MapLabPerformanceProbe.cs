using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabPerformanceProbe
    {
        #region [Variables/Fields]
        public const string ReportPath = MapLabPreviewRenderer.OutputFolder + "/MapLab_performance.txt";

        private const int RebuildRuns = 20;
        private const int BakeRuns = 5;
        private const int NavMeshRuns = 3;
        private const int RenderWarmup = 5;
        private const int RenderRuns = 40;
        private const int RenderWidth = 1920;
        private const int RenderHeight = 1080;
        private const int BytesPerVertex = 48;
        private const int BytesPerIndex = 2;
        private static readonly float[] SliceSweep = { 0.25f, 0.5f, 1f, 2f, 4f };
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Performance Report", false, 102)]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Run());
            EditorUtility.RevealInFinder(ReportPath);
        }

        public static void ReportBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(Run()));
        }

        public static string Run()
        {
            if (!File.Exists(MapLabSceneBuilder.ScenePath)) throw new InvalidOperationException("Build the MapLab demo scene first.");
            Scene scene = EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            try
            {
                var report = new StringBuilder();
                report.AppendLine($"MapLab performance report  {DateTime.Now:yyyy-MM-dd HH:mm}  Unity {Application.unityVersion}  {SystemInfo.graphicsDeviceName}");
                report.AppendLine($"Scene: {MapLabSceneBuilder.ScenePath}");
                report.AppendLine();

                List<ExtrudedShape> shapes = Find<ExtrudedShape>(scene);
                List<PathShape> paths = Find<PathShape>(scene);
                ReportShapes(report, shapes, paths);
                ReportSliceSweep(report, shapes.FirstOrDefault(shape => !shape.IsStacked && shape.Type == ExtrudedShapeType.Plateau && shape.Height > 0f && shape.YSort));
                ReportBake(report, scene);
                ReportNavMesh(report, scene);
                ReportRendering(report, scene, shapes);

                Directory.CreateDirectory(MapLabPreviewRenderer.OutputFolder);
                File.WriteAllText(ReportPath, report.ToString());
                return $"[MapLab] Performance report written to {ReportPath}\n{report}";
            }
            finally
            {
                EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            }
        }

        public static double RenderMs(Camera camera, Vector2 center, float size)
        {
            Transform cameraTransform = camera.transform;
            Vector3 previousPosition = cameraTransform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;
            var target = new RenderTexture(RenderWidth, RenderHeight, 24, RenderTextureFormat.ARGB32);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            cameraTransform.position = new Vector3(center.x, center.y, previousPosition.z);
            camera.orthographicSize = size;
            camera.targetTexture = target;

            void Frame()
            {
                camera.Render();
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = target;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = previousActive;
            }

            for (int i = 0; i < RenderWarmup; i++) Frame();
            double ms = Median(RenderRuns, Frame);

            camera.targetTexture = previousTarget;
            cameraTransform.position = previousPosition;
            camera.orthographicSize = previousSize;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(probe);
            return ms;
        }
        #endregion

        #region [Private Methods]
        private static void ReportShapes(StringBuilder report, List<ExtrudedShape> shapes, List<PathShape> paths)
        {
            report.AppendLine("## Shapes (rebuild = median of " + RebuildRuns + " runs, includes tiers on top)");
            report.AppendLine("| Shape | Type | Tier | Drawn as | Pieces | Draw calls | Vertices | Triangles | Rebuild ms |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int pieces = 0, draws = 0, vertices = 0, indices = 0;
            foreach (var shape in shapes)
            {
                Measure(shape.transform.Find(ExtrudedShape.VisualName), out int p, out int d, out int v, out int i);
                Measure(shape.transform.Find(ExtrudedShape.ShadowName), out int sp, out int sd, out int sv, out int si);
                p += sp; d += sd; v += sv; i += si;
                pieces += p; draws += d; vertices += v; indices += i;
                double ms = Median(RebuildRuns, shape.Rebuild);
                report.AppendLine($"| {shape.name} | {shape.Type}{(shape.Height <= 0f ? " (flat)" : "")} | {shape.TierIndex + 1} | {PointShapeEditor.PieceLayout(shape)} | {p} | {d} | {v} | {i / 3} | {ms:0.00} |");
            }
            foreach (var path in paths)
            {
                Measure(path.transform, out int p, out int d, out int v, out int i);
                pieces += p; draws += d; vertices += v; indices += i;
                double ms = Median(RebuildRuns, path.Rebuild);
                report.AppendLine($"| {path.name} | Path | - | one piece | {p} | {d} | {v} | {i / 3} | {ms:0.00} |");
            }

            double all = Median(10, () =>
            {
                foreach (var shape in shapes.Where(shape => !shape.IsStacked)) shape.Rebuild();
                foreach (var path in paths) path.Rebuild();
            });
            int groups = shapes.Sum(shape => shape.transform.Find(ExtrudedShape.VisualName).GetComponentsInChildren<SortingGroup>().Length);
            int shadows = shapes.Count(shape => shape.transform.Find(ExtrudedShape.ShadowName) != null);
            report.AppendLine();
            report.AppendLine($"Totals: {shapes.Count} shapes, {paths.Count} paths, {pieces} mesh renderers ({shadows} ground shadows; pieces include them), {groups} sorting groups, about {draws} draw calls (non-empty submeshes), {vertices} vertices, {indices / 3} triangles");
            report.AppendLine($"Mesh memory: about {(vertices * BytesPerVertex + indices * BytesPerIndex) / 1024f:0.0} KB");
            report.AppendLine($"Rebuild everything (what scene load / Play costs once): {all:0.00} ms");
            report.AppendLine();
        }

        private static void ReportSliceSweep(StringBuilder report, ExtrudedShape shape)
        {
            if (shape == null) return;
            var serialized = new SerializedObject(shape);
            SerializedProperty slice = serialized.FindProperty("_sortSlice");
            float original = slice.floatValue;

            report.AppendLine($"## Sort Slice sweep on '{shape.name}' (with its tiers)");
            report.AppendLine("| Sort Slice | Pieces | Draw calls | Rebuild ms |");
            report.AppendLine("|---|---|---|---|");
            foreach (float value in SliceSweep)
            {
                SetSlice(shape, value);
                int pieces = 0, draws = 0;
                foreach (var each in shape.GetComponentsInChildren<ExtrudedShape>())
                {
                    Measure(each.transform.Find(ExtrudedShape.VisualName), out int p, out int d, out _, out _);
                    pieces += p;
                    draws += d;
                }
                report.AppendLine($"| {value:0.##} | {pieces} | {draws} | {Median(RebuildRuns, shape.Rebuild):0.00} |");
            }
            SetSlice(shape, original);
            report.AppendLine();
        }

        private static void ReportBake(StringBuilder report, Scene scene)
        {
            MapLabMap map = MapLabWalkableBaker.FindMaps(scene).FirstOrDefault();
            if (map == null) return;
            WalkableAreaJson data = null;
            double ms = Median(BakeRuns, () => data = MapLabWalkableBaker.Compute(map));
            int bytes = Encoding.UTF8.GetByteCount(JsonUtility.ToJson(data, true));
            report.AppendLine("## Walkable bake (Editor only)");
            report.AppendLine($"Compute: {ms:0.0} ms for {data.columns} x {data.rows} cells ({data.CellCount} OverlapBox queries), {data.holes.Length} outlines, JSON {bytes / 1024f:0.0} KB");
            report.AppendLine();
        }

        private static void ReportNavMesh(StringBuilder report, Scene scene)
        {
            MonoBehaviour navigation = Find<MonoBehaviour>(scene).FirstOrDefault(component => component.GetType().Name == "NavigationManager");
            if (navigation == null) return;
            double ms = Median(NavMeshRuns, () => navigation.SendMessage("Rebuild", SendMessageOptions.DontRequireReceiver));
            report.AppendLine("## NavMesh from the baked file (sample, runs once at Start)");
            report.AppendLine($"Rebuild: {ms:0.0} ms");
            report.AppendLine();
        }

        private static void ReportRendering(StringBuilder report, Scene scene, List<ExtrudedShape> shapes)
        {
            Camera camera = MapLabSceneBuilder.FindCamera(scene);
            if (camera == null) return;
            report.AppendLine($"## Rendering ({RenderWidth} x {RenderHeight}, median of {RenderRuns} frames, CPU + GPU, waits for the GPU each frame)");
            report.AppendLine("| View | Everything | Shapes hidden | Shapes cost |");
            report.AppendLine("|---|---|---|---|");
            List<ExtrudedShape> roots = shapes.Where(shape => !shape.IsStacked).ToList();
            foreach (var (label, center, size) in new[] { ("Gameplay (ortho 6)", Vector2.zero, 6f), ("Whole map (ortho 17)", Vector2.zero, 17f) })
            {
                double full = RenderMs(camera, center, size);
                foreach (var root in roots) root.gameObject.SetActive(false);
                double hidden = RenderMs(camera, center, size);
                foreach (var root in roots) root.gameObject.SetActive(true);
                report.AppendLine($"| {label} | {full:0.00} ms | {hidden:0.00} ms | {full - hidden:0.00} ms |");
            }
            report.AppendLine();
            report.AppendLine("Per frame on the CPU MapLab runs no Update: shapes, paths and the map only rebuild when edited or enabled. The occluder fade runs a coroutine only while fading.");
        }

        private static void SetSlice(ExtrudedShape shape, float value)
        {
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_sortSlice").floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private static void Measure(Transform root, out int pieces, out int draws, out int vertices, out int indices)
        {
            pieces = draws = vertices = indices = 0;
            if (root == null) return;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                pieces++;
                vertices += mesh.vertexCount;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    int count = (int)mesh.GetIndexCount(i);
                    indices += count;
                    if (count > 0) draws++;
                }
            }
        }

        private static List<T> Find<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static double Median(int runs, Action action)
        {
            var times = new List<double>(runs);
            var watch = new Stopwatch();
            for (int i = 0; i < runs; i++)
            {
                watch.Restart();
                action();
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }
        #endregion
    }
}
