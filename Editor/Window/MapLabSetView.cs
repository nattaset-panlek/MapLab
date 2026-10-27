using System;
using System.Collections.Generic;
using System.Linq;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public class MapLabSetView
    {
        #region [Variables/Fields]
        private const string RenameControl = "MapLabSetRename";
        private const float ColumnHeight = 18f;
        private const float FooterHeight = 30f;
        private const float NameX = 36f;
        private const float CompactHeaderWidth = 400f;
        private const float DropHeight = 56f;
        private static readonly string[] ColourNames = { "Teal", "Blue", "Indigo", "Violet", "Sky", "Slate" };

        private Vector2 _scroll;
        private int _lastClicked = -1;
        private MapLabSet _renaming;
        private string _renameText = "";
        private bool _focusRename;
        private int _targetSet;
        private readonly HashSet<int> _collapsed = new HashSet<int>();

        private readonly struct Columns
        {
            public readonly float Type;
            public readonly float Set;
            public readonly float End;
            public readonly float Actions;
            public readonly bool ShowSet;

            public Columns(float type, float set, float end, float actions, bool showSet)
            {
                Type = type;
                Set = set;
                End = end;
                Actions = actions;
                ShowSet = showSet;
            }
        }
        #endregion

        #region [Public Methods]
        public void BeginRename(MapLabSet set)
        {
            _renaming = set;
            _renameText = set.name;
            _focusRename = true;
        }

        public void Draw(Rect area, MapLabMap map, MapLabWindow.Page page, MapLabSet set, string search, Action<MapLabWindow.Page, MapLabSet> navigate)
        {
            List<GameObject> items = Items(map, page, set);
            if (!string.IsNullOrEmpty(search)) items = items.Where(item => item.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            GUILayout.BeginArea(area);
            DrawHeader(map, page, set, items, navigate);
            DrawColumns(page, area.width);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < items.Count; i++)
            {
                DrawRow(map, page, items, i, area.width);
                ExtrudedShape shape = items[i].GetComponent<ExtrudedShape>();
                if (shape != null && !_collapsed.Contains(items[i].GetInstanceID())) DrawTierRows(shape, page, area.width);
            }
            if (items.Count == 0) GUILayout.Label(EmptyText(page, search), MapLabWindowStyles.Wrap);
            if (page == MapLabWindow.Page.Set) DrawDropHint();
            EditorGUILayout.EndScrollView();
            DrawFooter(map, page, set);
            GUILayout.EndArea();

            if (page == MapLabWindow.Page.Set) HandleDrop(new Rect(0f, 0f, area.width, area.height), set, area);
        }

        public static void AcceptDrop(Rect target, MapLabSet set)
        {
            Event current = Event.current;
            if ((current.type != EventType.DragUpdated && current.type != EventType.DragPerform) || !target.Contains(current.mousePosition)) return;
            var dragged = DragAndDrop.objectReferences.OfType<GameObject>().Where(item => MapLabSets.CanAdd(set, item)).ToList();
            if (dragged.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                MapLabSets.Add(set, dragged);
            }
            current.Use();
        }

        public static Texture IconFor(GameObject target)
        {
            if (target.GetComponent<ExtrudedShape>() != null) return MapLabIcons.Load(MapLabIcons.ExtrudedShape);
            if (target.GetComponent<PathShape>() != null) return MapLabIcons.Load(MapLabIcons.PathShape);
            if (target.GetComponent<MapLabSet>() != null) return MapLabIcons.Load(MapLabIcons.Map);
            return EditorGUIUtility.ObjectContent(target, typeof(GameObject)).image;
        }

        public static void ShowSetMenu(MapLabMap map, MapLabSet set, MapLabSetView view)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => view.BeginRename(set));
            for (int i = 0; i < MapLabSets.Palette.Length; i++)
            {
                Color colour = MapLabSets.Palette[i];
                menu.AddItem(new GUIContent($"Colour/{ColourNames[i]}"), set.Color == colour, () => MapLabSets.Recolor(set, colour));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Set (keeps its objects)"), false, () => MapLabSets.Delete(map, set));
            menu.ShowAsContext();
        }
        #endregion

        #region [Private Methods]
        private static List<GameObject> Items(MapLabMap map, MapLabWindow.Page page, MapLabSet set)
        {
            switch (page)
            {
                case MapLabWindow.Page.Set: return set != null ? MapLabSets.Members(set) : new List<GameObject>();
                case MapLabWindow.Page.Unassigned: return MapLabSets.Unassigned(map);
                default: return MapLabSets.AllObjects(map);
            }
        }

        private void DrawHeader(MapLabMap map, MapLabWindow.Page page, MapLabSet set, List<GameObject> items, Action<MapLabWindow.Page, MapLabSet> navigate)
        {
            Rect header = MapLabWidgets.HeaderBar();
            bool setPage = page == MapLabWindow.Page.Set && set != null;
            bool compact = header.width < CompactHeaderWidth;
            float buttonWidth = compact ? 56f : 78f;

            float right = header.xMax - 8f;
            if (setPage)
            {
                right -= 22f;
                if (GUI.Button(new Rect(right, header.y + 8f, 20f, 18f), EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton)) ShowSetMenu(map, set, this);
                right -= 4f;
            }
            right -= buttonWidth;
            if (GUI.Button(new Rect(right, header.y + 8f, buttonWidth, 18f), new GUIContent(compact ? "Select" : "Select All", "Select every object on this page"), EditorStyles.miniButton)) Selection.objects = items.ToArray();
            right -= buttonWidth + 4f;
            if (GUI.Button(new Rect(right, header.y + 8f, buttonWidth, 18f), new GUIContent(compact ? "Locate" : "Locate All", "Select them all and frame them in the Scene view"), EditorStyles.miniButton)) MapLabSets.Locate(items);

            float x = header.x + 12f;
            float limit = right - 8f;
            if (setPage) EditorGUI.DrawRect(new Rect(header.x, header.y + 4f, 3f, header.height - 8f), set.Color);
            if (setPage && _renaming == set)
            {
                DrawRename(new Rect(x, header.y + 7f, Mathf.Max(60f, Mathf.Min(160f, limit - x)), 20f));
                return;
            }
            string title = setPage ? set.name : page == MapLabWindow.Page.Unassigned ? "Unassigned" : "All objects";
            x = MapLabWidgets.Title(header, x, title, limit - x);
            var count = new GUIContent(items.Count == 1 ? "1 object" : $"{items.Count} objects");
            float countWidth = MapLabWindowStyles.Dim.CalcSize(count).x;
            if (x + countWidth <= limit) GUI.Label(new Rect(x, header.y, countWidth, header.height), count, MapLabWindowStyles.Dim);
        }

        private void DrawRename(Rect field)
        {
            Event current = Event.current;
            bool commit = current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == RenameControl;
            bool cancel = current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && GUI.GetNameOfFocusedControl() == RenameControl;
            GUI.SetNextControlName(RenameControl);
            _renameText = EditorGUI.TextField(field, _renameText);
            if (_focusRename)
            {
                EditorGUI.FocusTextInControl(RenameControl);
                _focusRename = false;
                return;
            }
            bool lostFocus = current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != RenameControl;
            if (cancel)
            {
                _renaming = null;
                current.Use();
            }
            else if (commit || lostFocus)
            {
                if (_renaming != null) MapLabSets.Rename(_renaming, _renameText);
                _renaming = null;
                if (commit) current.Use();
            }
        }

        private static void DrawColumns(MapLabWindow.Page page, float width)
        {
            Columns layout = Layout(page, width);
            Rect columns = GUILayoutUtility.GetRect(0f, ColumnHeight, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(NameX, columns.y, layout.Type - NameX, columns.height), "Name", MapLabWindowStyles.Caption);
            GUI.Label(new Rect(layout.Type, columns.y, layout.Set - layout.Type, columns.height), "Type", MapLabWindowStyles.Caption);
            if (layout.ShowSet) GUI.Label(new Rect(layout.Set, columns.y, layout.End - layout.Set, columns.height), "Set", MapLabWindowStyles.Caption);
            EditorGUI.DrawRect(new Rect(0f, columns.yMax - 1f, width, 1f), MapLabWindowStyles.Line);
        }

        private void DrawRow(MapLabMap map, MapLabWindow.Page page, List<GameObject> items, int index, float width)
        {
            GameObject item = items[index];
            bool selected = Selection.gameObjects.Contains(item);
            Rect row = MapLabWidgets.ListRow(selected);

            Columns layout = Layout(page, row.width);
            float actions = layout.Actions;
            ExtrudedShape plateau = item.GetComponent<ExtrudedShape>();
            if (plateau != null && MapLabSets.TierStack(plateau).Count > 1)
            {
                int id = item.GetInstanceID();
                bool open = EditorGUI.Foldout(new Rect(row.x + 2f, row.y, 12f, row.height), !_collapsed.Contains(id), GUIContent.none, true);
                if (open) _collapsed.Remove(id);
                else _collapsed.Add(id);
            }
            MapLabWidgets.Icon(new Rect(row.x + 14f, row.y + 3f, MapLabWindowStyles.IconSize, MapLabWindowStyles.IconSize), IconFor(item));
            GUI.Label(new Rect(row.x + NameX, row.y, layout.Type - NameX - 6f, row.height), Clipped(item.name), MapLabWindowStyles.Row);
            GUI.Label(new Rect(layout.Type, row.y, layout.Set - layout.Type - 6f, row.height), Clipped(MapLabSets.TypeLabel(item)), MapLabWindowStyles.Dim);
            MapLabSet owner = MapLabSets.SetOf(item);
            if (layout.ShowSet)
            {
                if (owner != null) MapLabWindowStyles.Tag(new Rect(layout.Set, row.y, 3f, row.height), owner.Color);
                GUI.Label(new Rect(layout.Set + 8f, row.y, layout.End - layout.Set - 8f, row.height), Clipped(owner != null ? owner.name : "none"), MapLabWindowStyles.Dim);
            }

            Color tint = selected ? Color.white : GUI.skin.label.normal.textColor;
            var locate = new Rect(actions, row.y + 3f, 16f, 16f);
            if (MapLabWidgets.Locate(locate, "Locate: select and frame in the Scene view", selected)) MapLabSets.Locate(new[] { item });
            var remove = new Rect(actions + 22f, row.y + 3f, 16f, 16f);
            if (owner != null && page != MapLabWindow.Page.Unassigned
                && MapLabWindowStyles.IconButton(remove, EditorGUIUtility.IconContent("Toolbar Minus").image, $"Remove from '{owner.name}' (stays in the map)", tint))
            {
                MapLabSets.Remove(map, new[] { item });
                GUIUtility.ExitGUI();
            }

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !row.Contains(current.mousePosition) || locate.Contains(current.mousePosition) || remove.Contains(current.mousePosition)) return;
            if (current.clickCount == 2) MapLabSets.Locate(new[] { item });
            else Select(items, index, current);
            current.Use();
        }

        private static void DrawTierRows(ExtrudedShape plateau, MapLabWindow.Page page, float width)
        {
            var stack = MapLabSets.TierStack(plateau);
            for (int i = 1; i < stack.Count; i++)
            {
                (ExtrudedShape tier, int depth) = stack[i];
                GameObject target = tier.gameObject;
                bool selected = Selection.gameObjects.Contains(target);
                Rect row = MapLabWidgets.ListRow(selected);
                Columns layout = Layout(page, row.width);
                float indent = 14f + depth * 14f;
                MapLabWidgets.Icon(new Rect(row.x + indent, row.y + 3f, MapLabWindowStyles.IconSize, MapLabWindowStyles.IconSize), MapLabIcons.Load(MapLabIcons.Tier));
                GUI.Label(new Rect(row.x + indent + 22f, row.y, layout.Type - indent - 28f, row.height), Clipped(tier.name), MapLabWindowStyles.Row);
                GUI.Label(new Rect(layout.Type, row.y, layout.Set - layout.Type - 6f, row.height), Clipped($"{MapLabSets.TierLabel(depth)}, top {tier.TopElevation:0.##}"), MapLabWindowStyles.Dim);

                var locate = new Rect(layout.Actions, row.y + 3f, 16f, 16f);
                if (MapLabWidgets.Locate(locate, "Locate this tier", selected)) MapLabSets.Locate(new[] { target });
                Event current = Event.current;
                if (current.type != EventType.MouseDown || current.button != 0 || !row.Contains(current.mousePosition) || locate.Contains(current.mousePosition)) continue;
                if (current.clickCount == 2) MapLabSets.Locate(new[] { target });
                else
                {
                    Selection.activeGameObject = target;
                    EditorGUIUtility.PingObject(target);
                }
                current.Use();
            }
        }

        private void Select(List<GameObject> items, int index, Event current)
        {
            var selection = new List<Object>(Selection.objects);
            if (current.shift && _lastClicked >= 0 && _lastClicked < items.Count)
            {
                int from = Mathf.Min(_lastClicked, index);
                int to = Mathf.Max(_lastClicked, index);
                for (int i = from; i <= to; i++)
                {
                    if (!selection.Contains(items[i])) selection.Add(items[i]);
                }
            }
            else if (current.control || current.command)
            {
                if (!selection.Remove(items[index])) selection.Add(items[index]);
            }
            else
            {
                selection = new List<Object> { items[index] };
            }
            Selection.objects = selection.ToArray();
            EditorGUIUtility.PingObject(items[index]);
            _lastClicked = index;
        }

        private static void DrawDropHint()
        {
            GUILayout.Space(14f);
            Rect space = GUILayoutUtility.GetRect(0f, DropHeight, GUILayout.ExpandWidth(true));
            var box = new Rect(space.x + 12f, space.y, Mathf.Max(40f, space.width - 24f), space.height);
            bool dragging = DragAndDrop.objectReferences.Length > 0;
            Color primary = MapLabWindowStyles.Primary;
            if (dragging) EditorGUI.DrawRect(box, new Color(primary.r, primary.g, primary.b, 0.12f));
            MapLabWidgets.DashedBox(box, dragging ? primary : MapLabWindowStyles.DimText);
            var style = new GUIStyle(MapLabWindowStyles.Wrap) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(box, "Drag objects here from the Hierarchy,\nor select them and press Add Selected.", style);
        }

        private void DrawFooter(MapLabMap map, MapLabWindow.Page page, MapLabSet set)
        {
            Rect footer = GUILayoutUtility.GetRect(0f, FooterHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(footer.x, footer.y, footer.width, 1f), MapLabWindowStyles.Line);
            GameObject[] selected = Selection.gameObjects;
            var button = new Rect(footer.x + 10f, footer.y + 6f, 130f, 20f);
            if (page == MapLabWindow.Page.Set && set != null)
            {
                int addable = selected.Count(target => MapLabSets.CanAdd(set, target));
                using (new EditorGUI.DisabledScope(addable == 0))
                {
                    if (GUI.Button(button, $"Add Selected ({addable})", addable > 0 ? MapLabWindowStyles.PrimaryButton : EditorStyles.miniButton)) MapLabSets.Add(set, selected);
                }
                button.x += 136f;
                button.width = 150f;
                int removable = selected.Count(target => MapLabSets.SetOf(target) == set);
                using (new EditorGUI.DisabledScope(removable == 0))
                {
                    if (GUI.Button(button, $"Remove Selected ({removable})", EditorStyles.miniButton)) MapLabSets.Remove(map, selected.Where(target => MapLabSets.SetOf(target) == set));
                }
                return;
            }

            List<MapLabSet> sets = MapLabSets.SetsOf(map);
            if (page == MapLabWindow.Page.AllObjects)
            {
                int removable = selected.Count(target => MapLabSets.SetOf(target) != null);
                using (new EditorGUI.DisabledScope(removable == 0))
                {
                    button.width = 170f;
                    if (GUI.Button(button, $"Remove Selected from Sets ({removable})", EditorStyles.miniButton)) MapLabSets.Remove(map, selected);
                }
                return;
            }

            GUI.Label(new Rect(button.x, button.y, 120f, 20f), "Add selected to", MapLabWindowStyles.Dim);
            if (sets.Count == 0)
            {
                GUI.Label(new Rect(button.x + 96f, button.y, 200f, 20f), "no sets yet: press New Set", MapLabWindowStyles.Dim);
                return;
            }
            _targetSet = Mathf.Clamp(_targetSet, 0, sets.Count - 1);
            _targetSet = EditorGUI.Popup(new Rect(button.x + 96f, button.y + 1f, 120f, 18f), _targetSet, sets.Select(item => item.name).ToArray());
            int count = selected.Count(target => MapLabSets.CanAdd(sets[_targetSet], target));
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUI.Button(new Rect(button.x + 222f, button.y, 80f, 20f), $"Add ({count})", count > 0 ? MapLabWindowStyles.PrimaryButton : EditorStyles.miniButton)) MapLabSets.Add(sets[_targetSet], selected);
            }
        }

        private static void HandleDrop(Rect local, MapLabSet set, Rect area)
        {
            if (set == null) return;
            GUILayout.BeginArea(area);
            AcceptDrop(local, set);
            GUILayout.EndArea();
        }

        private static string EmptyText(MapLabWindow.Page page, string search)
        {
            if (!string.IsNullOrEmpty(search)) return $"Nothing matches '{search}'.";
            switch (page)
            {
                case MapLabWindow.Page.Unassigned: return "Every MapLab shape is in a set.";
                case MapLabWindow.Page.Set: return "This set is empty.";
                default: return "No MapLab objects in this map yet. Add shapes from GameObject > MapLab.";
            }
        }

        private static Columns Layout(MapLabWindow.Page page, float width)
        {
            float actions = width - (page == MapLabWindow.Page.Unassigned ? 24f : 46f);
            float end = actions - 6f;
            float span = Mathf.Max(60f, end - NameX);
            bool showSet = page == MapLabWindow.Page.AllObjects;
            float type = NameX + span * (showSet ? 0.42f : 0.55f);
            float set = showSet ? NameX + span * 0.72f : end;
            return new Columns(type, set, end, actions, showSet);
        }

        private static GUIContent Clipped(string text)
        {
            return new GUIContent(text, text);
        }
        #endregion
    }
}
