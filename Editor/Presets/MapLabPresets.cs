using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.Presets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabPresets
    {
        #region [Variables/Fields]
        public const string FolderPreference = "MapLab.PresetFolder";
        public const string DefaultFolder = "Assets/MapLab Presets";
        public const string ValuesName = "Values";

        private const float FlatHeight = 0.01f;
        private const string TemplateName = "[MapLab] Preset Template";

        private static readonly string[] ShapeExcluded = { "m_Enabled", "_points", "_sharp", "_visualBaked", "_shadowBaked", "_preset" };
        private static readonly string[] MapExcluded =
        {
            "m_Enabled", "_bounds", "_walkableData", "_walkableMask", "_walkableStamp", "_elevationData", "_elevationMask", "_bakeLayers", "_layerStack", "_world", "_preset"
        };
        #endregion

        #region [Properties]
        public static string Folder
        {
            get => EditorPrefs.GetString(FolderPreference, DefaultFolder);
            set => EditorPrefs.SetString(FolderPreference, value);
        }
        #endregion

        #region [Public Methods]
        public static List<MapLabPreset> All()
        {
            return AssetDatabase.FindAssets($"t:{nameof(MapLabPreset)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<MapLabPreset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(preset => preset != null)
                .OrderBy(preset => preset.Kind)
                .ThenBy(preset => preset.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool CanCapture(Object source)
        {
            return source is MapLabMap || source is ExtrudedShape || source is PathShape;
        }

        public static Component Source(GameObject go)
        {
            if (go == null) return null;
            Component shape = go.GetComponent<PointShape>();
            return shape != null ? shape : go.GetComponent<MapLabMap>();
        }

        public static MapLabPresetKind KindOf(Component source)
        {
            switch (source)
            {
                case MapLabMap _: return MapLabPresetKind.Environment;
                case PathShape _: return MapLabPresetKind.Path;
                case ExtrudedShape shape:
                    if (shape.Type == ExtrudedShapeType.Void) return MapLabPresetKind.Void;
                    if (shape.Type == ExtrudedShapeType.Ring) return MapLabPresetKind.Ring;
                    if (shape.Height < FlatHeight) return MapLabPresetKind.Flat;
                    if (shape.Points.Count == 4 && Enumerable.Range(0, 4).All(shape.IsSharp)) return MapLabPresetKind.Box;
                    return MapLabPresetKind.Plateau;
                default:
                    throw new ArgumentException($"'{source}' cannot be a MapLab preset");
            }
        }

        public static Type TargetType(MapLabPresetKind kind)
        {
            switch (kind)
            {
                case MapLabPresetKind.Environment: return typeof(MapLabMap);
                case MapLabPresetKind.Path: return typeof(PathShape);
                default: return typeof(ExtrudedShape);
            }
        }

        public static bool Fits(MapLabPreset preset, Component target)
        {
            return preset != null && target != null && TargetType(preset.Kind).IsInstanceOfType(target);
        }

        public static Preset Values(MapLabPreset preset)
        {
            return preset != null ? preset.Values as Preset : null;
        }

        public static MapLabPreset Create(Component source, string name, string folder = null)
        {
            if (!CanCapture(source)) throw new InvalidOperationException("Pick a MapLab map, plateau, ring, box, flat shape or path to make a preset from.");
            folder = string.IsNullOrEmpty(folder) ? Folder : folder;
            MapLabOutputPaths.EnsureFolder(folder);
            var preset = ScriptableObject.CreateInstance<MapLabPreset>();
            preset.Kind = KindOf(source);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Clean(name)}.asset");
            AssetDatabase.CreateAsset(preset, path);
            var values = new Preset(source) { name = ValuesName, excludedProperties = Excluded(source) };
            AssetDatabase.AddObjectToAsset(values, preset);
            preset.Values = values;
            if (source is MapLabMap map) CaptureEnvironment(preset, map);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            Link(source, preset);
            return preset;
        }

        public static void Capture(MapLabPreset preset, Component source)
        {
            Preset values = Values(preset);
            if (values == null) throw new InvalidOperationException($"Preset '{preset.name}' has lost its values; make it again from an object.");
            Undo.RecordObjects(new Object[] { preset, values }, "Update MapLab Preset");
            values.UpdateProperties(source);
            values.excludedProperties = Excluded(source);
            if (source is MapLabMap map) CaptureEnvironment(preset, map);
            EditorUtility.SetDirty(values);
            EditorUtility.SetDirty(preset);
        }

        public static bool Apply(MapLabPreset preset, Component target)
        {
            Preset values = Values(preset);
            if (values == null || !Fits(preset, target)) return false;
            Undo.RecordObject(target, $"Apply Preset {preset.name}");
            values.ApplyTo(target);
            Link(target, preset);
            if (target is MapLabMap map) ApplyEnvironment(preset, map);
            if (target is PointShape shape) shape.Rebuild();
            EditorUtility.SetDirty(target);
            if (target.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            return true;
        }

        public static void Link(Component target, MapLabPreset preset)
        {
            if (target is PointShape shape) shape.Preset = preset;
            else if (target is MapLabMap map) map.Preset = preset;
            EditorUtility.SetDirty(target);
        }

        public static void Unlink(Component target)
        {
            Undo.RecordObject(target, "Unlink MapLab Preset");
            Link(target, null);
        }

        public static List<Component> Users(MapLabPreset preset)
        {
            var users = new List<Component>();
            if (preset == null) return users;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    users.AddRange(root.GetComponentsInChildren<PointShape>(true).Where(shape => shape.Preset == preset));
                    users.AddRange(root.GetComponentsInChildren<MapLabMap>(true).Where(map => map.Preset == preset));
                }
            }
            return users;
        }

        public static int Sync(MapLabPreset preset)
        {
            if (preset == null || !preset.SyncUsers) return 0;
            return Users(preset).Count(user => Apply(preset, user));
        }

        public static Component Template(MapLabPreset preset)
        {
            Preset values = Values(preset);
            if (values == null) return null;
            var go = new GameObject(TemplateName) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            Component template = go.AddComponent(TargetType(preset.Kind));
            values.ApplyTo(template);
            return template;
        }

        public static void Release(Component template)
        {
            if (template != null) Object.DestroyImmediate(template.gameObject);
        }

        public static void Store(MapLabPreset preset, Component template)
        {
            Preset values = Values(preset);
            if (values == null || template == null) return;
            Undo.RecordObject(values, "Edit MapLab Preset");
            values.UpdateProperties(template);
            values.excludedProperties = Excluded(template);
            EditorUtility.SetDirty(values);
            EditorUtility.SetDirty(preset);
        }

        public static Component CreateInScene(MapLabPreset preset, MapLabMap map, Vector3 position)
        {
            Component created;
            Transform parent = map != null ? map.transform : null;
            switch (preset.Kind)
            {
                case MapLabPresetKind.Environment:
                    created = ExtrudedShapeMenu.CreateMap(null);
                    break;
                case MapLabPresetKind.Path:
                    created = ExtrudedShapeMenu.CreatePath(preset.name, parent, position, new[] { new Vector2(-4f, 0f), new Vector2(0f, 1f), new Vector2(4f, 0f) }, 1.5f, -92);
                    break;
                default:
                    created = CreateShape(preset, parent, position);
                    break;
            }
            Undo.RegisterCreatedObjectUndo(created.gameObject, $"Create {preset.name}");
            Apply(preset, created);
            return created;
        }

        public static MapLabPreset Rename(MapLabPreset preset, string name)
        {
            string path = AssetDatabase.GetAssetPath(preset);
            string error = AssetDatabase.RenameAsset(path, Clean(name));
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            return preset;
        }

        public static string[] Excluded(Component source)
        {
            return source is MapLabMap ? MapExcluded : ShapeExcluded;
        }
        #endregion

        #region [Private Methods]
        private static ExtrudedShape CreateShape(MapLabPreset preset, Transform parent, Vector3 position)
        {
            ExtrudedShapeType type = preset.Kind == MapLabPresetKind.Ring ? ExtrudedShapeType.Ring : preset.Kind == MapLabPresetKind.Void ? ExtrudedShapeType.Void : ExtrudedShapeType.Plateau;
            if (preset.Kind == MapLabPresetKind.Box)
            {
                var corners = new[] { new Vector2(-1f, -0.6f), new Vector2(1f, -0.6f), new Vector2(1f, 0.6f), new Vector2(-1f, 0.6f) };
                return ExtrudedShapeMenu.Create(type, preset.name, parent, position, corners, 2f, 0.4f, ExtrudedShapeMenu.CharacterOrder, new[] { true, true, true, true });
            }
            float radius = type == ExtrudedShapeType.Ring ? 4f : 3f;
            var points = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8;
                points.Add(new Vector2(Mathf.Cos(angle) * radius * 1.3f, Mathf.Sin(angle) * radius * 0.8f));
            }
            return ExtrudedShapeMenu.Create(type, preset.name, parent, position, points, 1.5f, 0.4f, ExtrudedShapeMenu.CharacterOrder);
        }

        private static void CaptureEnvironment(MapLabPreset preset, MapLabMap map)
        {
            preset.Layers.Clear();
            List<MapLabBakeLayerSettings> decorate = MapLabLayerStack.DecorateLayers(map);
            foreach (var layer in MapLabLayerStack.LayersBottomToTop(map))
            {
                preset.Layers.Add(new MapLabPresetLayer(layer.Name, layer.Kinds, layer.Sorted, decorate.Contains(layer), layer.CastShadows, layer.Sets.Where(set => set != null).Select(set => set.name)));
            }
            preset.Palette = map.World != null ? map.World.Palette : null;
            preset.TilesPerSide = map.World != null ? map.World.TilesPerSide : MegaChunkFormula.DefaultTilesPerSide;
        }

        private static void ApplyEnvironment(MapLabPreset preset, MapLabMap map)
        {
            foreach (var layer in preset.Layers)
            {
                MapLabBakeLayerSettings existing = map.BakeLayers.FirstOrDefault(settings => MapLabLayerStack.Same(settings.Name, layer.Name));
                if (existing == null)
                {
                    existing = new MapLabBakeLayerSettings(layer.Name, layer.Kinds) { Sorted = layer.Sorted };
                    map.BakeLayers.Add(existing);
                }
                else if (existing.Locked) continue;
                existing.Kinds = layer.Kinds;
                existing.Sorted = layer.Sorted;
                existing.CastShadows = layer.CastShadows;
                foreach (var set in MapLabSets.SetsOf(map).Where(set => layer.Sets.Contains(set.name) && !existing.Sets.Contains(set))) existing.Sets.Add(set);
            }
            foreach (var layer in preset.Layers.Where(layer => !layer.Sorted && layer.Decorate != MapLabLayerStack.IsDecorate(map, layer.Name)))
            {
                MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer.Name);
                if (settings != null && !settings.Locked) MapLabLayerStack.SetSide(map, layer.Name, layer.Decorate);
            }
            if (MapLabLayerStack.DecorateLayers(map).Count > 0) MapLabDrawLayers.EnsureShadowLayer(map);
            MapLabDrawLayers.Sync(map);
            MapLabWorldData world = map.World;
            if (world == null || preset.Palette == null) return;
            Undo.RecordObject(world, $"Apply Preset {preset.name}");
            world.Palette = preset.Palette;
            if (!world.Megas.Any(mega => mega.IsPainted)) world.TilesPerSide = preset.TilesPerSide;
            world.Touch();
            EditorUtility.SetDirty(world);
            MapLabWorldEdits.RebuildFloor(map);
        }

        private static string Clean(string name)
        {
            string clean = new string((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(clean) ? "Preset" : clean;
        }
        #endregion
    }
}
