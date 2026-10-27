using System.Collections.Generic;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class ShapePointHandles
    {
        #region [Variables/Fields]
        private const float PointSize = 0.09f;
        private const float InsertSize = 0.05f;
        private const float LabelOffset = 0.35f;
        private static GUIStyle _label;
        private static readonly Color OutlineColor = new Color(0.25f, 0.85f, 1f, 0.9f);
        private static readonly Color PointColor = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color InsertColor = new Color(0.4f, 1f, 0.5f, 0.9f);
        private static readonly Color RemoveColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color CornerColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        private static readonly Color RampColor = new Color(1f, 0.6f, 0.2f, 0.95f);
        #endregion

        #region [Properties]
        public static bool PlacingRamp { get; set; }
        #endregion

        #region [Public Methods]
        public static void Draw(PointShape shape)
        {
            if (shape == null || shape.Points.Count < shape.MinPointCount) return;

            Transform transform = shape.transform;
            DrawOutline(shape, transform);
            if (PlacingRamp && shape is ExtrudedShape placing)
            {
                DrawRampPlacement(placing);
                return;
            }

            bool removing = Event.current.shift;
            bool toggling = EditorGUI.actionKey;
            int count = shape.Points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 world = transform.TransformPoint(shape.Points[i]);
                float size = HandleUtility.GetHandleSize(world) * PointSize;
                bool sharp = shape.IsSharp(i);
                Handles.CapFunction cap = sharp ? Handles.DotHandleCap : Handles.RectangleHandleCap;

                if (removing || toggling)
                {
                    Handles.color = removing ? RemoveColor : CornerColor;
                    if (!Handles.Button(world, Quaternion.identity, size, size * 1.3f, cap)) continue;
                    if (removing && count > shape.MinPointCount)
                    {
                        Undo.RecordObject(shape, "Remove MapLab Point");
                        shape.RemovePoint(i);
                    }
                    else if (toggling)
                    {
                        Undo.RecordObject(shape, "Toggle MapLab Corner");
                        shape.SetSharp(i, !sharp);
                    }
                    GUIUtility.ExitGUI();
                }

                Handles.color = PointColor;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, cap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(shape, "Move MapLab Point");
                    shape.SetPoint(i, transform.InverseTransformPoint(moved));
                }
            }

            if (removing || toggling) return;
            DrawMoveAll(shape, transform);
            int spans = shape.Closed ? count : count - 1;
            for (int span = 0; span < spans; span++)
            {
                Vector2 local = shape.SpanMidpoint(span);
                Vector3 world = transform.TransformPoint(local);
                float size = HandleUtility.GetHandleSize(world) * InsertSize;
                Handles.color = InsertColor;
                if (!Handles.Button(world, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap)) continue;
                Undo.RecordObject(shape, "Add MapLab Point");
                shape.InsertPoint(span + 1, local);
                GUIUtility.ExitGUI();
            }
        }

        public static void MoveAll(PointShape shape, Vector3 worldOffset)
        {
            var moving = new List<PointShape> { shape };
            if (shape is ExtrudedShape extruded)
            {
                foreach (var tier in extruded.GetComponentsInChildren<ExtrudedShape>(true))
                {
                    if (tier != extruded) moving.Add(tier);
                }
            }
            Undo.RecordObjects(moving.ToArray(), "Move MapLab Points");
            foreach (var target in moving) target.OffsetPoints(target.transform.InverseTransformVector(worldOffset));
        }
        #endregion

        #region [Private Methods]
        private static void DrawRampPlacement(ExtrudedShape shape)
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                PlacingRamp = false;
                current.Use();
                SceneView.RepaintAll();
                return;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            float along = Mathf.Abs(ray.direction.z) > 0.0001f ? -ray.origin.z / ray.direction.z : 0f;
            Vector3 mouse = ray.origin + ray.direction * along;
            Transform transform = shape.transform;
            List<Vector2> outline = shape.Footprint();
            float position = RampFormula.Project(outline, transform.InverseTransformPoint(mouse));
            RampCorners corners = RampFormula.Corners(outline, position, ExtrudedShapeMenu.DefaultRampWidth, ExtrudedShapeMenu.DefaultRampLength(shape), 0f);
            Vector3[] ghost = { transform.TransformPoint(corners.TopLeft), transform.TransformPoint(corners.TopRight), transform.TransformPoint(corners.BottomRight), transform.TransformPoint(corners.BottomLeft), transform.TransformPoint(corners.TopLeft) };
            Handles.color = RampColor;
            Handles.DrawAAPolyLine(3f, ghost);
            Vector3 top = transform.TransformPoint(corners.TopCenter);
            Vector3 bottom = transform.TransformPoint(corners.BottomCenter);
            Handles.DrawAAPolyLine(2f, top, bottom);
            _label ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = OutlineColor } };
            Handles.Label(bottom, "Click to add a ramp here, Esc to stop", _label);

            if (current.type == EventType.MouseMove) SceneView.RepaintAll();
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt) return;
            RampShape ramp = ExtrudedShapeMenu.AddRamp(shape, position);
            Undo.RegisterCreatedObjectUndo(ramp.gameObject, "Add MapLab Ramp");
            PlacingRamp = false;
            current.Use();
            Selection.activeGameObject = ramp.gameObject;
            GUIUtility.ExitGUI();
        }

        private static void DrawMoveAll(PointShape shape, Transform transform)
        {
            Vector3 center = transform.TransformPoint(shape.PointsCenter());
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(center, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 offset = moved - center;
                offset.z = 0f;
                MoveAll(shape, offset);
            }
            _label ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = OutlineColor } };
            float size = HandleUtility.GetHandleSize(center);
            string tiers = shape is ExtrudedShape extruded && extruded.GetComponentsInChildren<ExtrudedShape>(true).Length > 1 ? " (with tiers)" : "";
            Handles.Label(center + new Vector3(size * LabelOffset, -size * LabelOffset, 0f), $"Move all points{tiers}", _label);
        }

        private static void DrawOutline(PointShape shape, Transform transform)
        {
            List<Vector2> outline = shape.SampleOutline();
            int length = outline.Count + (shape.Closed ? 1 : 0);
            var polyline = new Vector3[length];
            for (int i = 0; i < length; i++) polyline[i] = transform.TransformPoint(outline[i % outline.Count]);
            Handles.color = OutlineColor;
            Handles.DrawAAPolyLine(3f, polyline);
        }
        #endregion
    }
}
