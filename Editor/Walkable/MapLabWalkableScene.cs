using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLab.Editor
{
    public static class MapLabWalkableScene
    {
        #region [Variables/Fields]
        private const string ShowKey = "MapLab.Walkable.ShowInScene";
        private static readonly Color BlockedColor = new Color(0.82f, 0.34f, 0.36f, 0.5f);
        private static readonly Color FrameColor = new Color32(0x5B, 0x8D, 0xEF, 0xFF);

        private static MapLabMap _map;
        private static bool _listening;
        private static int _version;
        private static int _builtVersion = -1;
        private static TextAsset _builtFrom;
        private static List<Rect> _blocked = new List<Rect>();
        private static BoxBoundsHandle _handle;
        private static GUIStyle _label;
        private static Material _material;
        #endregion

        #region [Properties]
        public static bool Show
        {
            get => EditorPrefs.GetBool(ShowKey, true);
            set
            {
                EditorPrefs.SetBool(ShowKey, value);
                SceneView.RepaintAll();
            }
        }

        public static MapLabMap Target => _map;
        #endregion

        #region [Public Methods]
        public static void Track(MapLabMap map)
        {
            MapLabMap target = map != null && Show ? map : null;
            if (target == _map) return;
            _map = target;
            if (_map != null && !_listening)
            {
                SceneView.duringSceneGui += OnSceneGui;
                _listening = true;
            }
            else if (_map == null && _listening)
            {
                SceneView.duringSceneGui -= OnSceneGui;
                _listening = false;
            }
            SceneView.RepaintAll();
        }

        public static void Invalidate()
        {
            _version++;
            SceneView.RepaintAll();
        }

        public static List<Rect> BlockedRects(WalkableAreaJson data)
        {
            bool[] walkable = MapLabWalkableBaker.DecodeCells(data.cells, data.CellCount);
            var rects = new List<Rect>();
            var open = new Dictionary<Vector2Int, int>();
            for (int row = 0; row < data.rows; row++)
            {
                var next = new Dictionary<Vector2Int, int>();
                int column = 0;
                while (column < data.columns)
                {
                    if (walkable[row * data.columns + column])
                    {
                        column++;
                        continue;
                    }
                    int start = column;
                    while (column < data.columns && !walkable[row * data.columns + column]) column++;
                    var run = new Vector2Int(start, column);
                    if (open.TryGetValue(run, out int index))
                    {
                        Rect grown = rects[index];
                        grown.yMax += data.cellSize;
                        rects[index] = grown;
                        next[run] = index;
                        continue;
                    }
                    next[run] = rects.Count;
                    rects.Add(new Rect(data.bounds.x + start * data.cellSize, data.bounds.y + row * data.cellSize, (column - start) * data.cellSize, data.cellSize));
                }
                open = next;
            }
            return rects;
        }
        #endregion

        #region [Private Methods]
        private static void OnSceneGui(SceneView view)
        {
            if (_map == null)
            {
                Track(null);
                return;
            }
            if (Event.current.type == EventType.Repaint) DrawCells();
            DrawFrame();
        }

        private static void DrawCells()
        {
            Rebuild();
            if (_blocked.Count == 0) return;
            if (!Material().SetPass(0)) return;
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            GL.Begin(GL.QUADS);
            GL.Color(BlockedColor);
            foreach (var rect in _blocked)
            {
                GL.Vertex3(rect.xMin, rect.yMin, 0f);
                GL.Vertex3(rect.xMax, rect.yMin, 0f);
                GL.Vertex3(rect.xMax, rect.yMax, 0f);
                GL.Vertex3(rect.xMin, rect.yMax, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static Material Material()
        {
            if (_material != null) return _material;
            _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            return _material;
        }

        private static void Rebuild()
        {
            TextAsset source = _map.WalkableData;
            if (source == _builtFrom && _builtVersion == _version) return;
            _builtFrom = source;
            _builtVersion = _version;
            _blocked = source != null ? BlockedRects(JsonUtility.FromJson<WalkableAreaJson>(source.text)) : new List<Rect>();
        }

        private static void DrawFrame()
        {
            _handle ??= new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };
            _label ??= new GUIStyle(EditorStyles.whiteMiniLabel) { normal = { textColor = FrameColor }, fontStyle = FontStyle.Bold };
            Rect area = _map.Bounds;
            _handle.center = area.center;
            _handle.size = new Vector3(area.width, area.height, 0f);
            _handle.SetColor(FrameColor);
            EditorGUI.BeginChangeCheck();
            _handle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Vector2 size = _handle.size;
                var dragged = new Rect((Vector2)_handle.center - size * 0.5f, size);
                MapLabWalkableArea.SetBounds(_map, MapLabWalkableArea.SnapNearest(dragged, _map.CellSize), "Resize Walkable Area");
            }
            Vector2Int cells = MapLabWalkableArea.Cells(area, _map.CellSize);
            Handles.Label(new Vector3(area.xMin, area.yMax, 0f), $"Walkable area  {cells.x} x {cells.y} cells", _label);
        }
        #endregion
    }
}
