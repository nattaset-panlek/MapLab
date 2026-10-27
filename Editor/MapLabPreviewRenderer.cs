using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MapLab.Shapes;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabPreviewRenderer
    {
        #region [Variables/Fields]
        public const string OutputFolder = "Logs/MapLab";
        private const int Width = 1920;
        private const int Height = 1080;
        private const float OverviewSize = 17f;
        private const float BlockedTintAmount = 0.45f;
        private static readonly Color32 BlockedTint = new Color32(230, 40, 40, 255);
        private static readonly Color32 HoleLineColor = new Color32(255, 220, 40, 255);
        private static readonly Color32 OutlineLineColor = new Color32(60, 200, 255, 255);
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Render Demo Preview", false, 101)]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(RenderAll());
            EditorUtility.RevealInFinder(OutputFolder);
        }

        public static void RenderBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(RenderAll()));
        }

        public static string RenderAll()
        {
            if (!File.Exists(MapLabSceneBuilder.ScenePath))
            {
                throw new InvalidOperationException($"{MapLabSceneBuilder.ScenePath} does not exist. Run Tools > MapLab > Build Demo Scene first.");
            }

            Scene scene = EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            try
            {
                Camera camera = MapLabSceneBuilder.FindCamera(scene);
                if (camera == null) throw new InvalidOperationException("The MapLab demo scene has no camera.");

                PrepareScene(scene);
                Directory.CreateDirectory(OutputFolder);
                var written = new List<string>
                {
                    Shoot(camera, Vector2.zero, OverviewSize, "MapLab_overview.png"),
                    Shoot(camera, Vector2.zero, 6f, "MapLab_gameplay.png")
                };

                MapLabMap map = MapLabWalkableBaker.FindMaps(scene).Find(candidate => candidate.WalkableData != null);
                if (map != null)
                {
                    var data = JsonUtility.FromJson<WalkableAreaJson>(map.WalkableData.text);
                    written.Add(Shoot(camera, Vector2.zero, OverviewSize, "MapLab_walkable.png", (image, view) => PaintWalkable(image, view, data)));
                }
                return $"[MapLab] Rendered {string.Join(", ", written)}";
            }
            finally
            {
                EditorSceneManager.OpenScene(MapLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            }
        }
        #endregion

        #region [Private Methods]
        private static void PrepareScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var shape in root.GetComponentsInChildren<PointShape>(true))
                {
                    shape.Rebuild();
                }

                foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Simulate(20f, true, true);
                }
            }
        }

        private static string Shoot(Camera camera, Vector2 center, float orthographicSize, string fileName, Action<Texture2D, Rect> paint = null)
        {
            Transform cameraTransform = camera.transform;
            Vector3 previousPosition = cameraTransform.position;
            float previousSize = camera.orthographicSize;
            RenderTexture previousTarget = camera.targetTexture;

            cameraTransform.position = new Vector3(center.x, center.y, previousPosition.z);
            camera.orthographicSize = orthographicSize;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.Render();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            float halfWidth = orthographicSize * Width / Height;
            paint?.Invoke(image, new Rect(center.x - halfWidth, center.y - orthographicSize, halfWidth * 2f, orthographicSize * 2f));
            image.Apply();
            RenderTexture.active = previousActive;

            camera.targetTexture = previousTarget;
            cameraTransform.position = previousPosition;
            camera.orthographicSize = previousSize;

            string path = Path.Combine(OutputFolder, fileName).Replace('\\', '/');
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            return path;
        }

        private static void PaintWalkable(Texture2D image, Rect view, WalkableAreaJson data)
        {
            Color32[] pixels = image.GetPixels32();
            bool[] walkable = MapLabWalkableBaker.DecodeCells(data.cells, data.CellCount);
            float scale = Width / view.width;

            for (int row = 0; row < data.rows; row++)
            {
                for (int column = 0; column < data.columns; column++)
                {
                    if (walkable[row * data.columns + column]) continue;
                    float x = data.bounds.x + column * data.cellSize;
                    float y = data.bounds.y + row * data.cellSize;
                    int xMin = Mathf.Clamp(Mathf.FloorToInt((x - view.x) * scale), 0, Width);
                    int xMax = Mathf.Clamp(Mathf.CeilToInt((x + data.cellSize - view.x) * scale), 0, Width);
                    int yMin = Mathf.Clamp(Mathf.FloorToInt((y - view.y) * scale), 0, Height);
                    int yMax = Mathf.Clamp(Mathf.CeilToInt((y + data.cellSize - view.y) * scale), 0, Height);
                    for (int py = yMin; py < yMax; py++)
                    {
                        for (int px = xMin; px < xMax; px++)
                        {
                            int index = py * Width + px;
                            pixels[index] = Color32.Lerp(pixels[index], BlockedTint, BlockedTintAmount);
                        }
                    }
                }
            }

            foreach (var hole in data.holes)
            {
                DrawPolygon(pixels, view, scale, hole.points, HoleLineColor);
            }
            DrawPolygon(pixels, view, scale, data.outline, OutlineLineColor);
            image.SetPixels32(pixels);
        }

        private static void DrawPolygon(Color32[] pixels, Rect view, float scale, Vector2[] points, Color32 color)
        {
            if (points == null) return;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 from = (points[i] - view.position) * scale;
                Vector2 to = (points[(i + 1) % points.Length] - view.position) * scale;
                int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)));
                for (int step = 0; step <= steps; step++)
                {
                    Vector2 point = steps == 0 ? from : Vector2.Lerp(from, to, step / (float)steps);
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int px = Mathf.FloorToInt(point.x) + dx;
                            int py = Mathf.FloorToInt(point.y) + dy;
                            if (px >= 0 && px < Width && py >= 0 && py < Height) pixels[py * Width + px] = color;
                        }
                    }
                }
            }
        }
        #endregion
    }
}
