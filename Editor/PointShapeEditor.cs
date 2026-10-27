using System;
using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    [CustomEditor(typeof(PointShape), true)]
    [CanEditMultipleObjects]
    public class PointShapeEditor : UnityEditor.Editor
    {
        #region [Variables/Fields]
        private const float EditButtonHeight = 28f;
        private static readonly string[] HiddenFields = { "m_Script", "_points", "_sharp" };
        private static readonly string[] TierHiddenFields = { "m_Script", "_points", "_sharp", "_actorHeight", "_shadowOrder" };
        private static readonly string[] KeptFields = { "_ySort", "_sortingOrder", "_walkOnTop", "_generateCollision" };
        private const string OutlineKindPreference = "MapLab.OutlineKind";
        private const string OutlineLevelPreference = "MapLab.OutlineLevel";
        #endregion

        #region [Unity Methods]
        public override void OnInspectorGUI()
        {
            DrawEditButton();
            DrawBakedNotice();
            bool decorated = target is ExtrudedShape ground && ground.DrawsAsGround;
            if (decorated) DrawDecorateNotice((ExtrudedShape)target);
            EditorGUILayout.Space(4f);

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawFields(AllStacked() ? TierHiddenFields : HiddenFields, decorated);
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) RebuildTargets();

            if (target is ExtrudedShape shape && targets.Length == 1) DrawTierControls(shape);
            if (target is ExtrudedShape) DrawOutlineMaker(targets.OfType<ExtrudedShape>().ToList());

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Points: {serializedObject.FindProperty("_points").arraySize}\n" +
                "Edit Points, then in the Scene view:\n" +
                "  drag a square to move it, click a green dot to add a point,\n" +
                "  Shift + click to remove, Ctrl/Cmd + click to switch smooth / sharp corner.",
                MessageType.None);
            if (GUILayout.Button("Rebuild")) RebuildTargets();
        }
        #endregion

        #region [Public Methods]
        public static string PieceLayout(ExtrudedShape shape)
        {
            if (!shape.YSort || shape.MergesWalls) return "one piece";
            return shape.MergesTop ? "one top piece + wall bands" : "bands";
        }
        #endregion

        #region [Private Methods]
        private void DrawEditButton()
        {
            bool path = target is PathShape;
            Type toolType = path ? typeof(PathShapeTool) : typeof(ExtrudedShapeTool);
            bool active = ToolManager.activeToolType == toolType;
            Texture icon = MapLabIcons.Load(path ? MapLabIcons.PathShape : MapLabIcons.ExtrudedShape);
            var content = new GUIContent(active ? "  Editing Points" : "  Edit Points", icon);
            bool pressed = GUILayout.Toggle(active, content, "Button", GUILayout.Height(EditButtonHeight));
            if (pressed == active) return;
            if (pressed) ToolManager.SetActiveTool(toolType);
            else ToolManager.RestorePreviousTool();
        }

        private static void DrawDecorateNotice(ExtrudedShape shape)
        {
            MapLabMap map = shape.GetComponentInParent<MapLabMap>();
            string layer = map != null ? MapLabDrawLayers.DecorateLayers(map).Select(settings => settings.Name).FirstOrDefault(name => MapLabDrawLayers.OrderOf(map, name) == shape.GroundOrder) : null;
            string shadow = shape.GroundShadow ? "its shadow stays on" : "its shadow is off";
            EditorGUILayout.HelpBox($"Decorate  ·  {layer ?? "below the line"}\nBackground art drawn at order {shape.SortingOrder}: no Y-sort, no collider, no walking on top, {shadow}. The greyed settings are kept and come back when the layer moves above the line on the MapLab Layers page.", MessageType.Info);
        }

        private void DrawFields(string[] hidden, bool decorated)
        {
            SerializedProperty property = serializedObject.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (Array.IndexOf(hidden, property.name) >= 0) continue;
                bool kept = decorated && Array.IndexOf(KeptFields, property.name) >= 0;
                using (new EditorGUI.DisabledScope(kept))
                {
                    EditorGUILayout.PropertyField(property, kept ? new GUIContent($"{property.displayName} (kept)", property.tooltip) : null, true);
                }
            }
        }

        private static void DrawOutlineMaker(List<ExtrudedShape> shapes)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(shapes.Count == 1 ? $"2D Shape From '{shapes[0].name}'" : $"2D Shapes From {shapes.Count} Shapes", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var kind = (ExtrudedShapeOutlineKind)EditorPrefs.GetInt(OutlineKindPreference, 0);
                var level = (ExtrudedShapeOutlineLevel)EditorPrefs.GetInt(OutlineLevelPreference, 0);
                var newKind = (ExtrudedShapeOutlineKind)EditorGUILayout.Popup(new GUIContent("Make", "What the new GameObject gets"), (int)kind, ExtrudedShapeOutlines.KindLabels);
                var newLevel = (ExtrudedShapeOutlineLevel)EditorGUILayout.Popup(new GUIContent("From", "Ground outline: where the walls meet the ground, like the shape's own collider. Top outline: the top face, lifted by the height"), (int)level, ExtrudedShapeOutlines.LevelLabels);
                if (newKind != kind) EditorPrefs.SetInt(OutlineKindPreference, (int)newKind);
                if (newLevel != level) EditorPrefs.SetInt(OutlineLevelPreference, (int)newLevel);
                bool ring = shapes.Any(shape => shape.Type == ExtrudedShapeType.Ring);
                string what = newKind == ExtrudedShapeOutlineKind.EdgeCollider ? ring ? "an Edge Collider 2D along each outline (outside and inside of a ring)" : "an Edge Collider 2D along the outline"
                    : newKind == ExtrudedShapeOutlineKind.PolygonCollider ? ring ? "a Polygon Collider 2D filling the ring, hole left open" : "a Polygon Collider 2D filling the shape"
                    : "a flat Mesh of the shape (MeshFilter + MeshRenderer), saved under Outlines/ next to the map's bakes";
                EditorGUILayout.LabelField($"Adds a new GameObject next to it with {what}. It is a copy: make it again after you change the points.", EditorStyles.wordWrappedMiniLabel);
                if (!GUILayout.Button($"Create {ExtrudedShapeOutlines.KindLabels[(int)newKind]}", GUILayout.Height(24f))) return;
                List<GameObject> created = shapes.Select(shape => ExtrudedShapeOutlines.Create(shape, newKind, newLevel)).ToList();
                Selection.objects = created.ToArray<Object>();
                EditorGUIUtility.PingObject(created[0]);
                GUIUtility.ExitGUI();
            }
        }

        private static void DrawTierControls(ExtrudedShape shape)
        {
            EditorGUILayout.Space();
            var stack = MapLabSets.TierStack(shape);
            string heading = stack.Count == 1 ? "Tiers: none on top yet" : $"Tiers: {stack.Count} layers, {stack.Count - 1} on top of '{stack[0].Shape.name}'";
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            Vector2 iconSize = EditorGUIUtility.GetIconSize();
            EditorGUIUtility.SetIconSize(new Vector2(16f, 16f));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    (ExtrudedShape layer, int depth) = stack[i];
                    bool current = layer == shape;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(depth * 12f);
                        Texture icon = MapLabIcons.Load(depth == 0 ? MapLabIcons.ExtrudedShape : MapLabIcons.Tier);
                        GUILayout.Label(new GUIContent($" {MapLabSets.TierLabel(depth)}: {layer.name}", icon), current ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.MinWidth(60f));
                        GUILayout.Label($"{layer.BaseElevation:0.##} to {layer.TopElevation:0.##}", EditorStyles.miniLabel, GUILayout.Width(72f));
                        using (new EditorGUI.DisabledScope(current))
                        {
                            if (GUILayout.Button(current ? "Editing" : "Select", EditorStyles.miniButton, GUILayout.Width(56f)))
                            {
                                Selection.activeGameObject = layer.gameObject;
                                EditorGUIUtility.SetIconSize(iconSize);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }
            EditorGUIUtility.SetIconSize(iconSize);
            EditorGUILayout.LabelField($"Drawn as {PieceLayout(shape)} (actor height {shape.ActorHeight:0.##})", EditorStyles.miniLabel);
            DrawRampControls(shape);
            var content = new GUIContent($"  Add Tier On Top of '{shape.name}'", MapLabIcons.Load(MapLabIcons.Tier));
            if (!GUILayout.Button(content, GUILayout.Height(EditButtonHeight))) return;
            ExtrudedShape tier = ExtrudedShapeMenu.AddTier(shape);
            Undo.RegisterCreatedObjectUndo(tier.gameObject, "Add MapLab Tier");
            Selection.activeGameObject = tier.gameObject;
            GUIUtility.ExitGUI();
        }

        private static void DrawRampControls(ExtrudedShape shape)
        {
            var ramps = new List<RampShape>();
            foreach (Transform child in shape.transform)
            {
                var ramp = child.GetComponent<RampShape>();
                if (ramp != null) ramps.Add(ramp);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(ramps.Count == 0 ? "Ramps: none yet" : $"Ramps: {ramps.Count}", EditorStyles.boldLabel);
            foreach (var ramp in ramps)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{ramp.name}: {ramp.Style}, {ramp.Width:0.##} wide, {ramp.Length:0.##} long, {ramp.SlopeDegrees:0} deg", EditorStyles.label, GUILayout.MinWidth(60f));
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(56f)))
                    {
                        Selection.activeGameObject = ramp.gameObject;
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (!shape.WalkOnTop && ramps.Count > 0) EditorGUILayout.HelpBox("Tick Walk On Top so the ramps lead to a top characters can stand on.", MessageType.Warning);
            bool placing = ShapePointHandles.PlacingRamp && ToolManager.activeToolType == typeof(ExtrudedShapeTool);
            var content = new GUIContent(placing ? "  Click an Edge in the Scene View (Esc stops)" : "  Add Ramp: Click an Edge", MapLabIcons.Load(MapLabIcons.Tier));
            bool pressed = GUILayout.Toggle(placing, content, "Button", GUILayout.Height(EditButtonHeight));
            if (pressed == placing) return;
            ShapePointHandles.PlacingRamp = pressed;
            if (pressed && ToolManager.activeToolType != typeof(ExtrudedShapeTool)) ToolManager.SetActiveTool<ExtrudedShapeTool>();
            SceneView.RepaintAll();
        }

        private void DrawBakedNotice()
        {
            if (!(target is PointShape shape) || targets.Length != 1) return;
            bool shadow = shape is ExtrudedShape extruded && extruded.ShadowBaked;
            if (!shape.VisualBaked && !shadow) return;
            string message = shape.VisualBaked
                ? "This shape is drawn by a baked layer image, so it is hidden here. Show its layer live to see it while you edit, then Rebake."
                : "This shape's ground shadow is in a baked layer image. Rebake that layer after you change it.";
            EditorGUILayout.HelpBox(message, MessageType.Info);
            MapLabMap map = shape.GetComponentInParent<MapLabMap>(true);
            if (map != null && GUILayout.Button("Open Layers in the MapLab Window")) MapLabWindow.Open(map, MapLabWindow.Page.Layers);
        }

        private bool AllStacked()
        {
            foreach (Object selected in targets)
            {
                if (!(selected is ExtrudedShape shape) || !shape.IsStacked) return false;
            }
            return true;
        }

        private void RebuildTargets()
        {
            foreach (Object selected in targets)
            {
                if (selected is PointShape shape) shape.Rebuild();
            }
            SceneView.RepaintAll();
        }
        #endregion
    }
}
