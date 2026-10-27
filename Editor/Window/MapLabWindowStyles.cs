using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabWindowStyles
    {
        #region [Variables/Fields]
        public const float RowHeight = 22f;
        public const float IconSize = 16f;
        private const int ButtonRadius = 3;

        public static readonly Color Primary = new Color32(0x3D, 0x6F, 0xD6, 0xFF);
        public static readonly Color UpToDate = new Color32(0x4F, 0xB3, 0xA9, 0xFF);
        public static readonly Color OutOfDate = new Color32(0xA9, 0x8B, 0xEA, 0xFF);
        public static readonly Color NotBaked = new Color32(0x95, 0x95, 0x95, 0xFF);

        private static readonly Color PrimaryHover = new Color32(0x4A, 0x7C, 0xE3, 0xFF);
        private static readonly Color PrimaryPressed = new Color32(0x30, 0x5C, 0xB8, 0xFF);
        private static readonly Dictionary<Color, Texture2D> Rounds = new Dictionary<Color, Texture2D>();

        private static GUIStyle _primaryButton;
        private static GUIStyle _title;
        private static GUIStyle _caption;
        private static GUIStyle _row;
        private static GUIStyle _rowBold;
        private static GUIStyle _dim;
        private static GUIStyle _dimRight;
        private static GUIStyle _wrap;
        private static GUIStyle _zoomBox;
        private static GUIStyle _toggleOn;
        private static GUIStyle _toggleOff;
        private static GUIStyle _swatchLabel;
        private static GUIStyle _swatchLabelSelected;
        private static GUIStyle _nameField;
        private static Texture2D _crosshair;
        private static Texture2D _arrowUp;
        private static Texture2D _arrowDown;
        #endregion

        #region [Properties]
        public static Color Pane => EditorGUIUtility.isProSkin ? new Color32(0x33, 0x33, 0x33, 0xFF) : new Color32(0xCB, 0xCB, 0xCB, 0xFF);
        public static Color Header => EditorGUIUtility.isProSkin ? new Color32(0x3A, 0x3A, 0x3A, 0xFF) : new Color32(0xD6, 0xD6, 0xD6, 0xFF);
        public static Color Line => EditorGUIUtility.isProSkin ? new Color32(0x23, 0x23, 0x23, 0xFF) : new Color32(0x99, 0x99, 0x99, 0xFF);
        public static Color SelectedRow => EditorGUIUtility.isProSkin ? new Color32(0x2C, 0x5D, 0x87, 0xFF) : new Color32(0x3A, 0x72, 0xB0, 0xFF);
        public static Color DimText => EditorGUIUtility.isProSkin ? new Color32(0x95, 0x95, 0x95, 0xFF) : new Color32(0x55, 0x55, 0x55, 0xFF);
        public static Color Window => EditorGUIUtility.isProSkin ? new Color32(0x38, 0x38, 0x38, 0xFF) : new Color32(0xC8, 0xC8, 0xC8, 0xFF);
        public static Color Toolbar => EditorGUIUtility.isProSkin ? new Color32(0x3C, 0x3C, 0x3C, 0xFF) : new Color32(0xCB, 0xCB, 0xCB, 0xFF);
        public static Color ToolIdle => EditorGUIUtility.isProSkin ? new Color32(0x47, 0x47, 0x47, 0xFF) : new Color32(0xDF, 0xDF, 0xDF, 0xFF);
        public static Color Card => EditorGUIUtility.isProSkin ? new Color32(0x3E, 0x3E, 0x3E, 0xFF) : new Color32(0xD9, 0xD9, 0xD9, 0xFF);
        public static Color CardLine => EditorGUIUtility.isProSkin ? new Color32(0x2A, 0x2A, 0x2A, 0xFF) : new Color32(0xA8, 0xA8, 0xA8, 0xFF);
        public static Color Chip => EditorGUIUtility.isProSkin ? new Color32(0x45, 0x45, 0x45, 0xFF) : new Color32(0xC4, 0xC4, 0xC4, 0xFF);
        public static Color ToolGlyph => EditorGUIUtility.isProSkin ? new Color32(0xD2, 0xD2, 0xD2, 0xFF) : new Color32(0x30, 0x30, 0x30, 0xFF);

        public static GUIStyle ZoomBox => _zoomBox ??= new GUIStyle(EditorStyles.miniTextField) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fixedHeight = 22f };
        public static GUIStyle ToggleOn => _toggleOn ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, fontStyle = FontStyle.Bold };
        public static GUIStyle ToggleOff => _toggleOff ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        public static GUIStyle SwatchLabel => _swatchLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, fontSize = 9, normal = { textColor = DimText }, clipping = TextClipping.Clip };
        public static GUIStyle SwatchLabelSelected => _swatchLabelSelected ??= new GUIStyle(SwatchLabel) { fontStyle = FontStyle.Bold, normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black } };
        public static GUIStyle NameField => _nameField ??= new GUIStyle(EditorStyles.textField) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 22f, alignment = TextAnchor.MiddleLeft };

        public static GUIStyle PrimaryButton => _primaryButton ??= BuildPrimaryButton();
        public static GUIStyle Title => _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        public static GUIStyle Caption => _caption ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = DimText } };
        public static GUIStyle Row => _row ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        public static GUIStyle RowBold => _rowBold ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        public static GUIStyle Dim => _dim ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = DimText }, clipping = TextClipping.Clip };
        public static GUIStyle DimRight => _dimRight ??= new GUIStyle(Dim) { alignment = TextAnchor.MiddleRight };
        public static GUIStyle Wrap => _wrap ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = DimText } };

        public static Texture2D Crosshair
        {
            get
            {
                if (_crosshair == null) _crosshair = BuildCrosshair();
                return _crosshair;
            }
        }

        public static Texture2D ArrowUp
        {
            get
            {
                if (_arrowUp == null) _arrowUp = BuildArrow(true);
                return _arrowUp;
            }
        }

        public static Texture2D ArrowDown
        {
            get
            {
                if (_arrowDown == null) _arrowDown = BuildArrow(false);
                return _arrowDown;
            }
        }
        #endregion

        #region [Public Methods]
        public static Color StateColor(MapLabBakeStatus.State state)
        {
            switch (state)
            {
                case MapLabBakeStatus.State.UpToDate: return UpToDate;
                case MapLabBakeStatus.State.OutOfDate: return OutOfDate;
                default: return NotBaked;
            }
        }

        public static string StateLabel(MapLabBakeStatus.State state)
        {
            switch (state)
            {
                case MapLabBakeStatus.State.UpToDate: return "Up to date";
                case MapLabBakeStatus.State.OutOfDate: return "Out of date";
                default: return "Not baked";
            }
        }

        public static void Dot(Rect area, Color color)
        {
            var dot = new Rect(area.x, area.center.y - 3.5f, 7f, 7f);
            GUI.DrawTexture(dot, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, color, 0f, 3.5f);
        }

        public static void Tag(Rect area, Color color)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.y + 4f, 3f, area.height - 8f), color);
        }

        public static bool IconButton(Rect area, Texture texture, string tooltip, Color tint)
        {
            Color previous = GUI.color;
            GUI.color = tint;
            bool pressed = GUI.Button(area, new GUIContent(texture, tooltip), GUIStyle.none);
            GUI.color = previous;
            return pressed;
        }
        #endregion

        #region [Private Methods]
        private static GUIStyle BuildPrimaryButton()
        {
            var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, border = new RectOffset(ButtonRadius + 1, ButtonRadius + 1, ButtonRadius + 1, ButtonRadius + 1) };
            SetState(style.normal, Primary);
            SetState(style.hover, PrimaryHover);
            SetState(style.active, PrimaryPressed);
            SetState(style.focused, Primary);
            SetState(style.onNormal, Primary);
            return style;
        }

        private static void SetState(GUIStyleState state, Color color)
        {
            state.background = Rounded(color);
            state.scaledBackgrounds = new Texture2D[0];
            state.textColor = Color.white;
        }

        private static Texture2D Rounded(Color color)
        {
            if (Rounds.TryGetValue(color, out Texture2D texture) && texture != null) return texture;
            const int size = ButtonRadius * 4;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(ButtonRadius - (x + 0.5f), x + 0.5f - (size - ButtonRadius), 0f);
                    float dy = Mathf.Max(ButtonRadius - (y + 0.5f), y + 0.5f - (size - ButtonRadius), 0f);
                    float alpha = Mathf.Clamp01(ButtonRadius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            texture.Apply();
            Rounds[color] = texture;
            return texture;
        }

        private static Texture2D BuildArrow(bool up)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float py = y + 0.5f;
                    float fromApex = up ? 11f - py : py - 5f;
                    float inside = py < 5f || py > 11f ? 0f : fromApex - Mathf.Abs(x + 0.5f - 8f) + 0.5f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(inside)));
                }
            }
            texture.Apply();
            return texture;
        }

        private static Texture2D BuildCrosshair()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            var center = new Vector2(15.5f, 15.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2(x, y);
                    float ring = 1.4f - Mathf.Abs(Vector2.Distance(point, center) - 9f);
                    bool vertical = Mathf.Abs(x - center.x) < 1.2f && (y < 6 || y > 25);
                    bool horizontal = Mathf.Abs(y - center.y) < 1.2f && (x < 6 || x > 25);
                    float alpha = Mathf.Clamp01(Mathf.Max(ring, vertical || horizontal ? 1f : 0f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return texture;
        }
        #endregion
    }
}
