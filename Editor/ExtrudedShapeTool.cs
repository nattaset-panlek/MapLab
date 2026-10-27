using MapLab.Shapes;
using UnityEditor.EditorTools;

namespace MapLab.Editor
{
    [EditorTool("Edit MapLab Extruded Shape", typeof(ExtrudedShape))]
    public class ExtrudedShapeTool : PointShapeTool
    {
        #region [Properties]
        protected override string IconName => MapLabIcons.ExtrudedShape;
        protected override string Tooltip => "Edit Extruded Shape points";
        #endregion
    }
}
