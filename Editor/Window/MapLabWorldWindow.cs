using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLab.Editor
{
    public class MapLabWorldWindow : EditorWindow
    {
        #region [Variables/Fields]
        private readonly MapLabMegaView _view = new MapLabMegaView();
        [SerializeField] private MapLabMap _map;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            titleContent = new GUIContent("MapLab World", MapLabGlyphs.Get(MapLabGlyph.MegaChunks));
            minSize = new Vector2(480f, 360f);
            wantsMouseMove = true;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            Undo.undoRedoPerformed -= Repaint;
            _view.Dispose();
        }

        private void OnFocus()
        {
            if (_map != null) _view.Refresh(_map);
        }

        private void OnGUI()
        {
            if (_map == null && !ReferenceEquals(_map, null)) _map = EditorUtility.InstanceIDToObject(_map.GetInstanceID()) as MapLabMap;
            if (_map == null)
            {
                EditorGUILayout.HelpBox("The map of this window is closed. Open it from the Mega chunks page of the MapLab window.", MessageType.Info);
                return;
            }
            _view.Draw(new Rect(0f, 0f, position.width, position.height), _map, Repaint, () =>
            {
                _view.Refresh(_map);
                Repaint();
            }, false);
        }
        #endregion

        #region [Public Methods]
        public static MapLabWorldWindow Open(MapLabMap map)
        {
            var window = CreateWindow<MapLabWorldWindow>();
            window._map = map;
            window._view.Refresh(map);
            window.Show();
            return window;
        }
        #endregion

        #region [Private Methods]
        private void OnSceneClosed(Scene scene)
        {
            if (_map == null) Repaint();
        }
        #endregion
    }
}
