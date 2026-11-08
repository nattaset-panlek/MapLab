using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    [CustomEditor(typeof(MapLabMap))]
    public class MapLabMapEditor : UnityEditor.Editor
    {
        #region [Variables/Fields]
        private const float PreviewHeight = 160f;
        private int _bakedCount;
        private int _layerCount;
        private bool _countStale = true;
        #endregion

        #region [Unity Methods]
        private void OnEnable()
        {
            _countStale = true;
            EditorApplication.hierarchyChanged += MarkCountStale;
            Undo.undoRedoPerformed += MarkCountStale;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkCountStale;
            Undo.undoRedoPerformed -= MarkCountStale;
        }

        public override void OnInspectorGUI()
        {
            var map = (MapLabMap)target;
            if (GUILayout.Button(new GUIContent("  Open MapLab Window", MapLabIcons.Load(MapLabIcons.Map)), GUILayout.Height(28f)))
            {
                MapLabWindow.Open(map, MapLabWindow.Page.AllObjects);
            }
            EditorGUILayout.Space(4f);
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Bake Walkable Area", GUILayout.Height(28f)))
            {
                try
                {
                    Debug.Log(MapLabWalkableBaker.Bake(map));
                }
                catch (InvalidOperationException e)
                {
                    EditorUtility.DisplayDialog("MapLab", e.Message, "OK");
                }
                GUIUtility.ExitGUI();
            }

            DrawStaticBake(map);

            if (map.WalkableMask == null) return;
            EditorGUILayout.LabelField("White = walkable, black = blocked", EditorStyles.miniLabel);
            Rect area = GUILayoutUtility.GetRect(1f, PreviewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(area, map.WalkableMask, null, ScaleMode.ScaleToFit);
        }
        #endregion

        #region [Private Methods]
        private void MarkCountStale()
        {
            _countStale = true;
        }

        private void RefreshBakedCount(MapLabMap map)
        {
            if (!_countStale && _layerCount == MapLabLayerStack.Layers(map).Count) return;
            var layers = MapLabLayerStack.Layers(map);
            _layerCount = layers.Count;
            _bakedCount = layers.Count(layer => MapLabLayerBaker.BakedLayer(map, layer.Name) != null);
            _countStale = false;
        }

        private void DrawStaticBake(MapLabMap map)
        {
            EditorGUILayout.Space();
            RefreshBakedCount(map);
            int layers = _layerCount;
            int baked = _bakedCount;
            EditorGUILayout.LabelField($"Bake layers: {baked} of {layers} baked (everything below order {map.ActorSortingOrder} can be baked)", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Layers", GUILayout.Height(28f))) MapLabWindow.Open(map, MapLabWindow.Page.Layers);
                if (GUILayout.Button(baked == 0 ? "Bake All Layers" : "Rebake All", GUILayout.Height(28f)))
                {
                    try
                    {
                        Debug.Log(MapLabStaticBaker.Bake(map));
                        _countStale = true;
                    }
                    catch (InvalidOperationException e)
                    {
                        EditorUtility.DisplayDialog("MapLab", e.Message, "OK");
                    }
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(baked == 0))
                {
                    if (GUILayout.Button("Unbake", GUILayout.Height(28f), GUILayout.Width(90f)))
                    {
                        Debug.Log(MapLabStaticBaker.Unbake(map));
                        _countStale = true;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
        #endregion
    }
}
