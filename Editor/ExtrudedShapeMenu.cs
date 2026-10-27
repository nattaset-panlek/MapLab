using System;
using System.Collections.Generic;
using System.IO;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class ExtrudedShapeMenu
    {
        #region [Variables/Fields]
        public const string ShaderName = "MapLab/Shape Lit";
        public const string MaterialName = "MapLab_ShapeLit.mat";
        public const int CharacterOrder = 6;

        private const int MenuPriority = 10;
        private const float TierNudge = 0.35f;
        public const float DefaultRampWidth = 2f;
        private const float MinRampLength = 1.5f;
        private const float RampRunPerRise = 1.6f;
        private static readonly Color StoneWall = new Color(0.72f, 0.7f, 0.8f);
        private static readonly Color GrassTop = new Color(0.8f, 0.92f, 0.58f);
        private static readonly Color GrassLip = new Color(0.92f, 0.98f, 0.74f);
        private const float PlateauLip = 0.16f;
        private static readonly Color StoneTop = new Color(0.86f, 0.84f, 0.92f);
        private static readonly Color RockTop = new Color(0.66f, 0.64f, 0.74f);
        private static readonly Color RockWall = new Color(0.5f, 0.48f, 0.58f);
        private static readonly Color VoidFill = new Color(0.07f, 0.07f, 0.09f);
        #endregion

        #region [Properties]
        public static string MaterialPath => MapLabSceneBuilder.RootFolder + "/Runtime/Materials/" + MaterialName;
        #endregion

        #region [Public Methods]
        [MenuItem("GameObject/MapLab/Map", false, MenuPriority - 1)]
        public static void CreateMapFromMenu(MenuCommand command)
        {
            MapLabMap map = CreateMap((command.context as GameObject)?.transform);
            Selection.activeGameObject = map.gameObject;
        }

        public static MapLabMap CreateMap(Transform parent)
        {
            var go = new GameObject("[MapLab] Map");
            Undo.RegisterCreatedObjectUndo(go, "Create MapLab Map");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Create MapLab Map");
            return Undo.AddComponent<MapLabMap>(go);
        }

        [MenuItem("GameObject/MapLab/Set", false, MenuPriority - 1)]
        public static void CreateSetFromMenu(MenuCommand command)
        {
            MapLabMap map = (command.context as GameObject)?.GetComponentInParent<MapLabMap>(true);
            if (map == null && Selection.activeGameObject != null) map = Selection.activeGameObject.GetComponentInParent<MapLabMap>(true);
            if (map == null)
            {
                EditorUtility.DisplayDialog("MapLab", "Select the map (or anything inside it) first. A set lives inside one map.", "OK");
                return;
            }
            Selection.activeGameObject = MapLabSets.CreateSet(map, "New Set").gameObject;
        }

        [MenuItem("GameObject/MapLab/Plateau Shape", false, MenuPriority)]
        public static void CreatePlateau(MenuCommand command)
        {
            CreateFromMenu(ExtrudedShapeType.Plateau, command, false);
        }

        [MenuItem("GameObject/MapLab/Void Shape (cave rock)", false, MenuPriority + 1)]
        public static void CreateVoid(MenuCommand command)
        {
            CreateFromMenu(ExtrudedShapeType.Void, command, false);
        }

        [MenuItem("GameObject/MapLab/Ring Shape (walled area)", false, MenuPriority + 2)]
        public static void CreateRing(MenuCommand command)
        {
            CreateFromMenu(ExtrudedShapeType.Ring, command, false);
        }

        [MenuItem("GameObject/MapLab/Box Shape", false, MenuPriority + 3)]
        public static void CreateBox(MenuCommand command)
        {
            CreateFromMenu(ExtrudedShapeType.Plateau, command, true);
        }

        [MenuItem("GameObject/MapLab/Path", false, MenuPriority + 4)]
        public static void CreatePathFromMenu(MenuCommand command)
        {
            var points = new[] { new Vector2(-4f, -0.5f), new Vector2(0f, 0.8f), new Vector2(4f, -0.5f) };
            PathShape path = CreatePath("Path", (command.context as GameObject)?.transform, MenuPosition(), points, 1.5f, -92);
            Undo.RegisterCreatedObjectUndo(path.gameObject, "Create MapLab Path");
            Selection.activeGameObject = path.gameObject;
        }

        public static ExtrudedShape Create(ExtrudedShapeType type, string name, Transform parent, Vector3 position, IReadOnlyList<Vector2> points, float height, float rimWidth, int sortingOrder, IReadOnlyList<bool> sharp = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            if (parent != null) go.layer = parent.gameObject.layer;

            var shape = go.AddComponent<ExtrudedShape>();
            var serialized = new SerializedObject(shape);
            serialized.FindProperty("_type").enumValueIndex = (int)type;
            serialized.FindProperty("_height").floatValue = height;
            serialized.FindProperty("_rimWidth").floatValue = rimWidth;
            serialized.FindProperty("_sortingOrder").intValue = sortingOrder;
            serialized.FindProperty("_material").objectReferenceValue = LoadOrCreateMaterial();
            serialized.FindProperty("_wallTexture").objectReferenceValue = Art(MapLabArtPainter.WallStone);
            serialized.FindProperty("_topTexture").objectReferenceValue = Art(type == ExtrudedShapeType.Plateau ? MapLabArtPainter.TopGrass : MapLabArtPainter.TopStone);
            serialized.FindProperty("_topTextureSize").floatValue = 2f;
            serialized.FindProperty("_topColor").colorValue = type == ExtrudedShapeType.Plateau ? GrassTop : type == ExtrudedShapeType.Ring ? StoneTop : RockTop;
            serialized.FindProperty("_wallColor").colorValue = type == ExtrudedShapeType.Void ? RockWall : StoneWall;
            serialized.FindProperty("_voidColor").colorValue = VoidFill;
            serialized.FindProperty("_borderWidth").floatValue = type == ExtrudedShapeType.Plateau ? PlateauLip : 0f;
            serialized.FindProperty("_borderColor").colorValue = GrassLip;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (points != null) shape.SetPoints(points, sharp);
            else shape.Rebuild();
            return shape;
        }

        public static PathShape CreatePath(string name, Transform parent, Vector3 position, IReadOnlyList<Vector2> points, float width, int sortingOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;

            var path = go.AddComponent<PathShape>();
            var serialized = new SerializedObject(path);
            serialized.FindProperty("_width").floatValue = width;
            serialized.FindProperty("_sortingOrder").intValue = sortingOrder;
            serialized.FindProperty("_material").objectReferenceValue = LoadOrCreateMaterial();
            serialized.FindProperty("_texture").objectReferenceValue = Art(MapLabArtPainter.PathEdge);
            serialized.FindProperty("_textureLength").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            path.SetPoints(points);
            return path;
        }

        public static ExtrudedShape AddTier(ExtrudedShape parent, float scale = 0.62f, float height = -1f)
        {
            var go = new GameObject($"Tier {parent.TierIndex + 2}") { layer = parent.gameObject.layer };
            go.transform.SetParent(parent.transform, false);
            var tier = go.AddComponent<ExtrudedShape>();
            EditorUtility.CopySerialized(parent, tier);
            if (height > 0f)
            {
                var serialized = new SerializedObject(tier);
                serialized.FindProperty("_height").floatValue = height;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            IReadOnlyList<Vector2> points = parent.Points;
            Vector2 center = Vector2.zero;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var point in points)
            {
                center += point;
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            center /= points.Count;
            Vector2 nudge = Vector2.up * ((maxY - minY) * (1f - scale) * TierNudge);
            var inset = new List<Vector2>(points.Count);
            foreach (var point in points) inset.Add(center + (point - center) * scale + nudge);
            tier.SetPoints(inset, parent.Sharp);
            return tier;
        }

        public static RampShape AddRamp(ExtrudedShape parent, float position)
        {
            int number = parent.GetComponentsInChildren<RampShape>(true).Length + 1;
            var go = new GameObject(number == 1 ? "Ramp" : $"Ramp {number}") { layer = parent.gameObject.layer };
            go.transform.SetParent(parent.transform, false);
            var ramp = go.AddComponent<RampShape>();
            var serialized = new SerializedObject(ramp);
            serialized.FindProperty("_material").objectReferenceValue = LoadOrCreateMaterial();
            serialized.FindProperty("_wallTexture").objectReferenceValue = Art(MapLabArtPainter.WallStone);
            serialized.FindProperty("_surfaceTexture").objectReferenceValue = Art(MapLabArtPainter.TopStone);
            serialized.FindProperty("_surfaceColor").colorValue = StoneTop;
            serialized.FindProperty("_wallColor").colorValue = StoneWall;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ramp.Configure(position, DefaultRampWidth, DefaultRampLength(parent), 0f);
            return ramp;
        }

        public static float DefaultRampLength(ExtrudedShape parent)
        {
            return Mathf.Max(MinRampLength, parent.Height * RampRunPerRise);
        }

        public static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{ShaderName}' not found.");

            string folder = Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        public static Texture2D Art(string name)
        {
            return MapLabArtPainter.Get(MapLabSceneBuilder.GeneratedFolder, name).texture;
        }
        #endregion

        #region [Private Methods]
        private static void CreateFromMenu(ExtrudedShapeType type, MenuCommand command, bool box)
        {
            var parent = (command.context as GameObject)?.transform;
            var points = new List<Vector2>();
            bool[] sharp = null;
            if (box)
            {
                points.AddRange(new[] { new Vector2(-1f, -0.6f), new Vector2(1f, -0.6f), new Vector2(1f, 0.6f), new Vector2(-1f, 0.6f) });
                sharp = new[] { true, true, true, true };
            }
            else
            {
                float radius = type == ExtrudedShapeType.Ring ? 4f : 3f;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * 2f / 8;
                    points.Add(new Vector2(Mathf.Cos(angle) * radius * 1.3f, Mathf.Sin(angle) * radius * 0.8f));
                }
            }

            string label = box ? "Box" : type.ToString();
            ExtrudedShape shape = Create(type, $"{label} Shape", parent, MenuPosition(), points, box ? 2f : 1.5f, type == ExtrudedShapeType.Ring ? 0.9f : 0.4f, CharacterOrder, sharp);
            if (type == ExtrudedShapeType.Plateau && !box)
            {
                var serialized = new SerializedObject(shape);
                serialized.FindProperty("_walkOnTop").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                shape.Rebuild();
            }
            Undo.RegisterCreatedObjectUndo(shape.gameObject, $"Create MapLab {label} Shape");
            Selection.activeGameObject = shape.gameObject;
        }

        private static Vector3 MenuPosition()
        {
            Vector3 position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            position.z = 0f;
            return position;
        }
        #endregion
    }
}
