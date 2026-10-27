using System.Collections.Generic;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    [CustomEditor(typeof(RampShape))]
    [CanEditMultipleObjects]
    public class RampShapeEditor : UnityEditor.Editor
    {
        #region [Variables/Fields]
        private const float HandleSize = 0.08f;
        private const float LabelOffset = 0.3f;
        private static readonly string[] Hidden = { "m_Script" };
        private static readonly Color OutlineColor = new Color(1f, 0.6f, 0.2f, 0.95f);
        private static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.95f);
        private static GUIStyle _label;
        #endregion

        #region [Unity Methods]
        public override void OnInspectorGUI()
        {
            var ramp = (RampShape)target;
            ExtrudedShape parent = ramp.Parent;
            if (parent == null)
            {
                EditorGUILayout.HelpBox("A ramp must be a child of a Plateau (or another Extruded Shape). Select a Plateau, press Add Ramp: Click an Edge, then click its edge.", MessageType.Warning);
            }
            else if (targets.Length == 1)
            {
                EditorGUILayout.HelpBox($"From {ramp.LowElevation:0.##} up to {ramp.HighElevation:0.##} on '{parent.name}' ({ramp.SlopeDegrees:0} degrees). Drag the handles in the Scene view: top middle slides along the edge, the side handle sets the width, the bottom handle sets the length and the angle.", MessageType.None);
                if (!parent.WalkOnTop)
                {
                    EditorGUILayout.HelpBox($"'{parent.name}' is not Walk On Top, so this ramp leads nowhere.", MessageType.Warning);
                    if (GUILayout.Button($"Turn On Walk On Top for '{parent.name}'"))
                    {
                        var shape = new SerializedObject(parent);
                        shape.FindProperty("_walkOnTop").boolValue = true;
                        shape.ApplyModifiedProperties();
                        parent.Rebuild();
                    }
                }
            }

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, Hidden);
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) RebuildTargets();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (parent != null && GUILayout.Button($"Select '{parent.name}'")) Selection.activeGameObject = parent.gameObject;
                if (GUILayout.Button("Rebuild")) RebuildTargets();
            }
        }

        private void OnSceneGUI()
        {
            var ramp = (RampShape)target;
            ExtrudedShape parent = ramp.Parent;
            if (parent == null) return;
            Transform space = parent.transform;
            RampCorners corners = ramp.LocalCorners();
            Vector3 World(Vector2 point) => space.TransformPoint(point);

            Handles.color = OutlineColor;
            Handles.DrawAAPolyLine(3f, World(corners.TopLeft), World(corners.TopRight), World(corners.BottomRight), World(corners.BottomLeft), World(corners.TopLeft));
            Handles.DrawAAPolyLine(2f, World(corners.TopCenter), World(corners.BottomCenter));

            Handles.color = HandleColor;
            Vector3 top = World(corners.TopCenter);
            float size = HandleUtility.GetHandleSize(top) * HandleSize;
            EditorGUI.BeginChangeCheck();
            Vector3 movedTop = Handles.FreeMoveHandle(top, size, Vector3.zero, Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                List<Vector2> outline = parent.Footprint();
                Change(ramp, "Slide MapLab Ramp", RampFormula.Project(outline, space.InverseTransformPoint(movedTop)), ramp.Width, ramp.Length, ramp.Angle);
            }

            Vector3 side = World(corners.TopRight);
            EditorGUI.BeginChangeCheck();
            Vector3 movedSide = Handles.FreeMoveHandle(side, size, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector2 tangent = (corners.TopRight - corners.TopLeft).normalized;
                float half = Mathf.Abs(Vector2.Dot((Vector2)space.InverseTransformPoint(movedSide) - corners.TopCenter, tangent));
                Change(ramp, "Resize MapLab Ramp", ramp.Position, half * 2f, ramp.Length, ramp.Angle);
            }

            Vector3 bottom = World(corners.BottomCenter);
            EditorGUI.BeginChangeCheck();
            Vector3 movedBottom = Handles.FreeMoveHandle(bottom, size * 1.2f, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector2 run = (Vector2)space.InverseTransformPoint(movedBottom) - corners.TopCenter;
                float angle = Event.current.shift ? ramp.Angle : Vector2.SignedAngle(corners.Outward, run);
                float length = Event.current.shift ? Vector2.Dot(run, corners.Direction) : run.magnitude;
                Change(ramp, "Stretch MapLab Ramp", ramp.Position, ramp.Width, length, angle);
            }

            _label ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = OutlineColor } };
            float offset = HandleUtility.GetHandleSize(bottom) * LabelOffset;
            Handles.Label(bottom + new Vector3(offset, -offset, 0f), $"{ramp.Length:0.##} long, {ramp.SlopeDegrees:0} deg, angle {ramp.Angle:0} (Shift: keep the angle)", _label);
        }
        #endregion

        #region [Private Methods]
        private static void Change(RampShape ramp, string action, float position, float width, float length, float angle)
        {
            Undo.RecordObject(ramp, action);
            ramp.Configure(position, width, length, angle);
            EditorUtility.SetDirty(ramp);
        }

        private void RebuildTargets()
        {
            foreach (Object selected in targets)
            {
                if (selected is RampShape ramp) ramp.Rebuild();
            }
            SceneView.RepaintAll();
        }
        #endregion
    }
}
