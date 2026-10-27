using System;
using System.IO;
using System.Linq;
using MapLab.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabSceneBuilder
    {
        #region [Variables/Fields]
        public const string DefaultRootFolder = "Assets/ThirdParty/MapLab";
        public const string EnvironmentRootName = "[MapLab] Environment";

        public const int GroundOrder = -100;
        public const int MossOrder = -96;
        public const int PathOrder = -92;
        public const int JunctionOrder = -91;
        public const int PlazaOrder = -90;
        public const int MistOrder = -50;
        public const int ShadowOrder = 1;
        public const int ContactShadowOrder = 2;
        public const int ObjectOrder = 6;
        public const int DustOrder = 95;
        public const int CanopyOrder = 100;

        private const string EditorAssemblyName = "MapLab.Editor";
        private const string SpriteLitMaterialGuid = "a97c105638bdf8b4a8650670310a4cd3";
        private const string SpriteUnlitShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        private const string ViewerLayerName = "Player";
        private const int DemoObstacleLayer = 0;
        private const float DemoCameraSize = 6f;
        private const float WalkableCellSize = 0.5f;
        private const float ShadowAlpha = 0.2f;
        private const float ContactShadowAlpha = 0.24f;

        private static readonly Vector2[] BoulderPositions =
        {
            new Vector2(-5.6f, -3.9f), new Vector2(-3.2f, -4.6f), new Vector2(13.6f, -2.9f), new Vector2(-12f, 0.6f),
            new Vector2(2f, 4.4f), new Vector2(12.6f, 4.6f), new Vector2(-15.5f, -4.2f)
        };

        private static readonly Vector2[] TreePositions =
        {
            new Vector2(-8.8f, -4.9f), new Vector2(10.9f, -4.6f)
        };

        private static string _rootFolder;
        #endregion

        #region [Properties]
        public static event Action<Transform, TextAsset> OnEnvironmentBuilt;

        public static string RootFolder => _rootFolder ??= FindRootFolder();
        public static string DemoFolder => RootFolder + "/Demo";
        public static string GeneratedFolder => DemoFolder + "/Generated";
        public static string ScenePath => DemoFolder + "/MapLab_Demo.unity";
        private static string VolumeProfilePath => DemoFolder + "/MapLab_Volume.asset";
        private static string FogMaterialPath => GeneratedFolder + "/MapLab_Fog.mat";
        private static string DustMaterialPath => GeneratedFolder + "/MapLab_Dust.mat";
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Build Demo Scene", false, 100)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Build());
        }

        public static void BuildBatch()
        {
            RunBatch(() => Debug.Log(Build()));
        }

        public static void BuildAndRenderBatch()
        {
            RunBatch(() =>
            {
                Debug.Log(Build());
                Debug.Log(MapLabPreviewRenderer.RenderAll());
            });
        }

        public static string Build()
        {
            EnsureFolder(RootFolder, "Demo");
            EnsureFolder(DemoFolder, "Generated");

            bool created = !File.Exists(ScenePath);
            Scene scene = created ? CreateDemoScene() : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var existing in scene.GetRootGameObjects().Where(go => go.name == EnvironmentRootName).ToArray())
            {
                Object.DestroyImmediate(existing);
            }

            Material litMaterial = LoadLitMaterial();

            var root = new GameObject(EnvironmentRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            string ground = MapLabGroundBuilder.Build(root.transform, litMaterial);
            string shapes = MapLabGroundBuilder.BuildShapes(root.transform);
            BuildProps(root.transform, litMaterial, DemoObstacleLayer);
            BuildLights(root.transform);
            BuildAtmosphere(root.transform);
            MapLabMap map = AddMap(root);
            string bake = MapLabWalkableBaker.Bake(map);
            OnEnvironmentBuilt?.Invoke(root.transform, map.WalkableData);
            string camera = EnablePostProcessing(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return $"[MapLab] {(created ? "Created" : "Rebuilt")} {ScenePath}; {ground}; {shapes}; props, lights, volume, mist; {camera}\n{bake}";
        }

        public static Transform Set(Transform parent, string name, int colour)
        {
            Transform existing = parent.Find(name);
            if (existing != null && existing.GetComponent<MapLabSet>() != null) return existing;
            Transform set = Child(parent, name);
            set.gameObject.AddComponent<MapLabSet>().SetColor(MapLabSets.Palette[colour % MapLabSets.Palette.Length]);
            return set;
        }

        public static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Camera FindCamera(Scene scene)
        {
            Camera fallback = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag("MainCamera")) return camera;
                    if (fallback == null) fallback = camera;
                }
            }
            return fallback;
        }

        public static void RunBatch(Action action)
        {
            int exitCode = 0;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }
        #endregion

        #region [Private Methods]
        private static string FindRootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets($"{EditorAssemblyName} t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != EditorAssemblyName) continue;
                return Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
            }
            return DefaultRootFolder;
        }

        private static Scene CreateDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = DemoCameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camera.GetUniversalAdditionalCameraData();
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        private static Sprite Art(string name)
        {
            return MapLabArtPainter.Get(GeneratedFolder, name);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }

        private static Material LoadLitMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SpriteLitMaterialGuid));
            if (material == null) throw new InvalidOperationException("Sprite-Lit-Default material not found.");
            return material;
        }

        private static void BuildProps(Transform parent, Material litMaterial, int obstacleLayer)
        {
            Transform props = Set(parent, "Props", MapLabSets.Palette.Length - 1);
            Sprite shadow = Art(MapLabArtPainter.SoftShadow);

            foreach (var position in BoulderPositions)
            {
                GameObject boulder = CreateProp(props, "Boulder", position, Art(MapLabArtPainter.Boulder), shadow, new Vector2(1.6f, 0.6f), litMaterial, obstacleLayer);
                var circle = boulder.AddComponent<CircleCollider2D>();
                circle.radius = 0.42f;
                circle.offset = new Vector2(0f, 0.24f);
            }

            foreach (var position in TreePositions)
            {
                GameObject tree = CreateProp(props, "Tree", position, Art(MapLabArtPainter.TreeTrunk), shadow, new Vector2(1.2f, 0.45f), litMaterial, obstacleLayer);
                var circle = tree.AddComponent<CircleCollider2D>();
                circle.radius = 0.22f;
                circle.offset = new Vector2(0f, 0.08f);
                AddSprite(tree.transform, "Canopy Shadow", shadow, litMaterial, ShadowOrder, new Color(0f, 0f, 0f, 0.24f), new Vector3(0.35f, 0.15f, 0f), new Vector3(3.3f, 1.5f, 1f));
                AddCanopy(tree.transform, litMaterial);
            }
        }

        private static GameObject CreateProp(Transform parent, string name, Vector2 position, Sprite sprite, Sprite shadow, Vector2 shadowSize, Material litMaterial, int layer)
        {
            var root = new GameObject(name) { layer = layer };
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            SpriteRenderer body = AddSprite(root.transform, "Sprite", sprite, litMaterial, ObjectOrder, Color.white, Vector3.zero, Vector3.one);
            body.spriteSortPoint = SpriteSortPoint.Pivot;
            AddSprite(root.transform, "Shadow", shadow, litMaterial, ShadowOrder, new Color(0f, 0f, 0f, ShadowAlpha), new Vector3(0.12f, -0.03f, 0f), new Vector3(shadowSize.x, shadowSize.y, 1f));
            AddSprite(root.transform, "Contact Shadow", shadow, litMaterial, ContactShadowOrder, new Color(0f, 0f, 0f, ContactShadowAlpha), new Vector3(0.02f, 0.01f, 0f), new Vector3(shadowSize.x * 0.55f, shadowSize.y * 0.5f, 1f));
            return root;
        }

        private static void AddCanopy(Transform tree, Material litMaterial)
        {
            SpriteRenderer canopy = AddSprite(tree, "Canopy", Art(MapLabArtPainter.TreeCanopy), litMaterial, CanopyOrder, Color.white, new Vector3(0f, 2.05f, 0f), Vector3.one);
            canopy.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            var trigger = canopy.gameObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(2.6f, 3.05f);
            trigger.offset = new Vector2(0f, -0.575f);

            var fade = canopy.gameObject.AddComponent<OccluderFadeController>();
            var serialized = new SerializedObject(fade);
            SerializedProperty renderers = serialized.FindProperty("_renderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = canopy;
            int viewerLayer = LayerMask.NameToLayer(ViewerLayerName);
            serialized.FindProperty("_viewerLayers").intValue = viewerLayer >= 0 ? 1 << viewerLayer : ~0;
            serialized.FindProperty("_fadedAlpha").floatValue = 0.35f;
            serialized.FindProperty("_fadeDuration").floatValue = 0.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Material material, int order, Color color, Vector3 localPosition, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.color = color;
            return renderer;
        }

        private static void BuildLights(Transform parent)
        {
            Transform lights = Child(parent, "Lights");
            var warm = new Color(1f, 0.74f, 0.46f);
            CreateLight(lights, "Global Light", Vector2.zero, Light2D.LightType.Global, Color.white, 1f, 0f, 0f);
            CreateLight(lights, "Torch Plaza North", new Vector2(5.2f, 2.7f), Light2D.LightType.Point, warm, 0.35f, 4.5f, 0.8f);
            CreateLight(lights, "Torch Plaza South", new Vector2(5.2f, -3.1f), Light2D.LightType.Point, warm, 0.35f, 4.5f, 0.8f);
            CreateLight(lights, "Cool Glow", new Vector2(-3.2f, 1.7f), Light2D.LightType.Point, new Color(0.5f, 0.66f, 1f), 0.3f, 3.6f, 0.5f);
            CreateLight(lights, "Cliff Lantern", new Vector2(-9.6f, 1.5f), Light2D.LightType.Point, warm, 0.3f, 4f, 0.5f);
        }

        private static void CreateLight(Transform parent, string name, Vector2 position, Light2D.LightType type, Color color, float intensity, float outerRadius, float innerRadius)
        {
            Transform child = Child(parent, name);
            child.position = position;
            var light = child.gameObject.AddComponent<Light2D>();
            light.lightType = type;
            light.color = color;
            light.intensity = intensity;
            light.shadowsEnabled = false;
            light.targetSortingLayers = SortingLayer.layers.Select(layer => layer.id).ToArray();
            if (type != Light2D.LightType.Point) return;
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerRadius = innerRadius;
            light.falloffIntensity = 0.6f;
        }

        private static void BuildAtmosphere(Transform parent)
        {
            Transform atmosphere = Child(parent, "Atmosphere");

            var volume = Child(atmosphere, "Global Volume").gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = LoadOrCreateVolumeProfile();

            Material fog = LoadOrCreateParticleMaterial(FogMaterialPath, Art(MapLabArtPainter.FogPuff).texture);
            Material dust = LoadOrCreateParticleMaterial(DustMaterialPath, Art(MapLabArtPainter.Dust).texture);

            ParticleSystem mist = CreateParticles(atmosphere, "Ground Mist", fog, MistOrder, new Vector2(52f, 36f));
            ConfigureFog(mist, new Vector2(5f, 9f), new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.08f), 2f, 80);

            ParticleSystem motes = CreateParticles(atmosphere, "Dust Motes", dust, DustOrder, new Vector2(34f, 22f));
            ParticleSystem.MainModule main = motes.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.93f, 0.75f, 0.5f), new Color(1f, 0.9f, 0.7f, 0.85f));
            main.maxParticles = 160;
            ParticleSystem.EmissionModule emission = motes.emission;
            emission.rateOverTime = 7f;
            ParticleSystem.NoiseModule noise = motes.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            SetFadeInOut(motes);
        }

        private static ParticleSystem CreateParticles(Transform parent, string name, Material material, int order, Vector2 area)
        {
            var go = Child(parent, name).gameObject;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(area.x, area.y, 0f);
            return system;
        }

        private static void ConfigureFog(ParticleSystem system, Vector2 sizeRange, Color colorA, Color colorB, float rate, int maxParticles)
        {
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(22f, 30f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.04f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            SetFadeInOut(system);
        }

        private static void SetFadeInOut(ParticleSystem system)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Material LoadOrCreateParticleMaterial(string path, Texture texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(SpriteUnlitShaderName);
                if (shader == null) throw new InvalidOperationException($"Shader '{SpriteUnlitShaderName}' not found.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static VolumeProfile LoadOrCreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            var bloom = GetOrAddOverride<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.15f);
            bloom.scatter.Override(0.5f);

            var color = GetOrAddOverride<ColorAdjustments>(profile);
            color.contrast.Override(0f);
            color.saturation.Override(6f);
            color.colorFilter.Override(Color.white);

            var vignette = GetOrAddOverride<Vignette>(profile);
            vignette.intensity.Override(0f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T GetOrAddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static MapLabMap AddMap(GameObject root)
        {
            var map = root.AddComponent<MapLabMap>();
            var serialized = new SerializedObject(map);
            Rect area = MapLabGroundBuilder.GroundArea;
            serialized.FindProperty("_bounds").rectValue = new Rect(area.xMin + 1f, area.yMin + 1f, area.width - 2f, area.height - 2f);
            serialized.FindProperty("_cellSize").floatValue = WalkableCellSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Ground", MapLabBakeKinds.Sprites | MapLabBakeKinds.Paths | MapLabBakeKinds.FlatShapes));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Shadows", MapLabBakeKinds.ShapeShadows | MapLabBakeKinds.PropShadows));
            map.BakeLayers.Add(new MapLabBakeLayerSettings("Walls", MapLabBakeKinds.SortedShapes) { Sorted = true });
            return map;
        }

        private static string EnablePostProcessing(Scene scene)
        {
            Camera camera = FindCamera(scene);
            if (camera == null) return "no camera found, post-processing not enabled";
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
            return $"post-processing on for '{camera.name}'";
        }
        #endregion
    }
}
