using UnityEngine;

namespace MapLab
{
    [DisallowMultipleComponent]
    public class MapLabSet : MonoBehaviour
    {
        #region [Variables/Fields]
        [SerializeField] private Color _color = new Color(0.31f, 0.7f, 0.66f);
        #endregion

        #region [Properties]
        public Color Color => _color;
        #endregion

        #region [Public Methods]
        public void SetColor(Color color)
        {
            _color = color;
        }
        #endregion
    }
}
