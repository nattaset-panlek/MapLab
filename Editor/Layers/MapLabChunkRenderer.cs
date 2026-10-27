using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabChunkRenderer
    {
        #region [Variables/Fields]
        private const string CameraName = "[MapLab] Bake Camera";
        private const float CameraDepth = -10f;
        #endregion

        #region [Public Methods]
        public static float SnappedChunkSize(MapLabMap map)
        {
            return map.ChunkSize;
        }

        public static int ChunkPixels(float chunkSize, int pixelsPerUnit)
        {
            return Mathf.RoundToInt(chunkSize * pixelsPerUnit);
        }

        public static Dictionary<Vector2Int, string> Render(Scene scene, IEnumerable<Renderer> sources, IEnumerable<Vector2Int> chunks, float chunkSize, int pixelsPerUnit, string folder, int perMega)
        {
            var written = new Dictionary<Vector2Int, string>();
            Render(scene, sources, chunks, chunkSize, pixelsPerUnit, (index, image) => written[index] = MapLabLayerFiles.Write(folder, index, image, perMega));
            return written;
        }

        public static void Render(Scene scene, IEnumerable<Renderer> sources, IEnumerable<Vector2Int> chunks, float chunkSize, int pixelsPerUnit, Action<Vector2Int, Texture2D> rendered)
        {
            var keep = new HashSet<Renderer>(sources);
            var renderers = new List<Renderer>();
            var lights = new List<Light2D>();
            foreach (var root in scene.GetRootGameObjects())
            {
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
                lights.AddRange(root.GetComponentsInChildren<Light2D>(true));
            }
            bool[] rendererStates = renderers.Select(renderer => renderer.enabled).ToArray();
            var lightStates = lights.Select(light => (light.enabled, light.color, light.intensity)).ToArray();

            int pixels = ChunkPixels(chunkSize, pixelsPerUnit);
            var cameraObject = new GameObject(CameraName) { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(pixels, pixels, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(pixels, pixels, TextureFormat.RGBA32, false);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                foreach (var renderer in renderers) renderer.enabled = keep.Contains(renderer);
                foreach (var light in lights)
                {
                    bool global = light.lightType == Light2D.LightType.Global;
                    light.enabled = global;
                    if (!global) continue;
                    light.color = Color.white;
                    light.intensity = 1f;
                }

                Camera camera = CreateCamera(cameraObject, chunkSize, target);
                foreach (var index in chunks.OrderBy(index => index.y).ThenBy(index => index.x))
                {
                    Vector2 center = MapLabChunkLayer.ChunkRect(index, chunkSize).center;
                    camera.transform.position = new Vector3(center.x, center.y, CameraDepth);
                    camera.Render();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0);
                    image.Apply();
                    if (!IsEmpty(image)) rendered(index, image);
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = rendererStates[i];
                }
                for (int i = 0; i < lights.Count; i++)
                {
                    lights[i].enabled = lightStates[i].enabled;
                    lights[i].color = lightStates[i].color;
                    lights[i].intensity = lightStates[i].intensity;
                }
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
        #endregion

        #region [Private Methods]
        private static Camera CreateCamera(GameObject holder, float chunkSize, RenderTexture target)
        {
            var camera = holder.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = chunkSize * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.targetTexture = target;
            camera.aspect = 1f;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            return camera;
        }

        private static bool IsEmpty(Texture2D image)
        {
            foreach (var pixel in image.GetPixels32())
            {
                if (pixel.a > 0) return false;
            }
            return true;
        }
        #endregion
    }
}
