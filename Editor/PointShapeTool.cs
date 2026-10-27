using MapLab.Shapes;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace MapLab.Editor
{
    public abstract class PointShapeTool : EditorTool
    {
        #region [Variables/Fields]
        private GUIContent _icon;
        #endregion

        #region [Properties]
        protected abstract string IconName { get; }
        protected abstract string Tooltip { get; }

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_icon != null && _icon.image != null) return _icon;
                Texture2D texture = MapLabIcons.Load(IconName);
                _icon = texture != null ? new GUIContent(texture, Tooltip) : new GUIContent("ML", Tooltip);
                return _icon;
            }
        }
        #endregion

        #region [Public Methods]
        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            foreach (Object selected in targets)
            {
                if (selected is PointShape shape) ShapePointHandles.Draw(shape);
            }
        }
        #endregion
    }
}
