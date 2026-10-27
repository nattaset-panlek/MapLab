using MapLab.Shapes;
using UnityEditor.EditorTools;

namespace MapLab.Editor
{
    [EditorTool("Edit MapLab Path", typeof(PathShape))]
    public class PathShapeTool : PointShapeTool
    {
        #region [Properties]
        protected override string IconName => MapLabIcons.PathShape;
        protected override string Tooltip => "Edit Path points";
        #endregion
    }
}
