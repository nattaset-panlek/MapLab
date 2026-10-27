using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabWidgets
    {
        #region [Variables/Fields]
        public const float HeaderHeight = 34f;
        #endregion

        #region [Public Methods]
        public static Rect HeaderBar()
        {
            Rect header = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, MapLabWindowStyles.Header);
            EditorGUI.DrawRect(new Rect(header.x, header.yMax - 1f, header.width, 1f), MapLabWindowStyles.Line);
            return header;
        }

        public static float Title(Rect header, float x, string title, float maxWidth = float.MaxValue)
        {
            var content = new GUIContent(title, title);
            float width = Mathf.Max(0f, Mathf.Min(MapLabWindowStyles.Title.CalcSize(content).x, maxWidth));
            GUI.Label(new Rect(x, header.y, width, header.height), content, MapLabWindowStyles.Title);
            return x + width + 8f;
        }

        public static void DashedBox(Rect area, Color color, float dash = 4f, float gap = 3f)
        {
            for (float x = area.x; x < area.xMax; x += dash + gap)
            {
                float width = Mathf.Min(dash, area.xMax - x);
                EditorGUI.DrawRect(new Rect(x, area.y, width, 1f), color);
                EditorGUI.DrawRect(new Rect(x, area.yMax - 1f, width, 1f), color);
            }
            for (float y = area.y; y < area.yMax; y += dash + gap)
            {
                float height = Mathf.Min(dash, area.yMax - y);
                EditorGUI.DrawRect(new Rect(area.x, y, 1f, height), color);
                EditorGUI.DrawRect(new Rect(area.xMax - 1f, y, 1f, height), color);
            }
        }

        public static void Icon(Rect area, Texture texture)
        {
            if (texture != null) GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
        }

        public static void Status(Rect area, MapLabBakeStatus.State state)
        {
            Color color = MapLabWindowStyles.StateColor(state);
            MapLabWindowStyles.Dot(area, color);
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = color }, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(area.x + 11f, area.y, area.width - 11f, area.height), MapLabWindowStyles.StateLabel(state), style);
        }

        public static void Caption(string title, string right = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(title, MapLabWindowStyles.Caption);
                GUILayout.FlexibleSpace();
                if (right != null) GUILayout.Label(right, MapLabWindowStyles.DimRight);
            }
        }

        public static Rect ListRow(bool selected)
        {
            Rect row = GUILayoutUtility.GetRect(0f, MapLabWindowStyles.RowHeight, GUILayout.ExpandWidth(true));
            if (selected) EditorGUI.DrawRect(row, MapLabWindowStyles.SelectedRow);
            return row;
        }

        public static bool Locate(Rect area, string tooltip, bool selected = false)
        {
            return MapLabWindowStyles.IconButton(area, MapLabWindowStyles.Crosshair, tooltip, selected ? Color.white : GUI.skin.label.normal.textColor);
        }

        public static float ChipWidth(string label)
        {
            return EditorStyles.miniBoldLabel.CalcSize(new GUIContent(label)).x + 20f;
        }

        public static void Chip(Rect area, string label, Color color, string tooltip = null)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(area, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, new Color(color.r, color.g, color.b, 0.25f), 0f, area.height * 0.5f);
                MapLabWindowStyles.Dot(new Rect(area.x + 6f, area.y, 7f, area.height), color);
            }
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.93f, 0.93f, 0.93f) : new Color(0.12f, 0.12f, 0.12f) } };
            GUI.Label(new Rect(area.x + 15f, area.y, area.width - 16f, area.height), new GUIContent(label, tooltip), style);
        }

        public static void FileRow(Object asset, string iconName, string label, string detail)
        {
            Rect row = ListRow(false);
            if (asset == null)
            {
                GUI.Label(row, "not baked yet", MapLabWindowStyles.Dim);
                return;
            }
            Icon(new Rect(row.x, row.y + 3f, 16f, 16f), EditorGUIUtility.IconContent(iconName).image);
            GUI.Label(new Rect(row.x + 22f, row.y, row.width - 120f, row.height), label, MapLabWindowStyles.Row);
            GUI.Label(new Rect(row.xMax - 100f, row.y, 72f, row.height), detail, MapLabWindowStyles.DimRight);
            if (!Locate(new Rect(row.xMax - 20f, row.y + 3f, 16f, 16f), "Show in the Project window")) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        #endregion
    }
}
