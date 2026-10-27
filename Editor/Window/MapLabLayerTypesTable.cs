using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabLayerTypesTable
    {
        #region [Variables/Fields]
        private const float RowHeight = 24f;
        private const float NameWidth = 112f;
        private const float CountWidth = 44f;
        private const float MinHintWidth = 90f;
        private static readonly Color Highlight = new Color32(0x2F, 0x3B, 0x4E, 0xFF);
        private static readonly Color HighlightLight = new Color32(0xB9, 0xCC, 0xE8, 0xFF);
        private static readonly Color Bar = new Color32(0x5B, 0x8D, 0xEF, 0xFF);
        #endregion

        #region [Public Methods]
        public static void Draw(MapLabMap map, MapLabBakeLayerSettings selected, IReadOnlyDictionary<MapLabBakeKinds, int> totals, float width, Action changed)
        {
            GUILayout.Space(12f);
            MapLabWidgets.Caption("WHAT GOES INTO EACH LAYER", width > 460f ? "Bake draws each object type into one layer's picture" : null);
            float dropWidth = Mathf.Clamp(width * 0.3f, 110f, 170f);
            Rect head = GUILayoutUtility.GetRect(0f, 16f, GUILayout.ExpandWidth(true));
            float hintWidth = head.width - NameWidth - CountWidth - dropWidth - 26f;
            var caption = new GUIStyle(MapLabWindowStyles.Caption) { fontSize = 9 };
            GUI.Label(new Rect(head.x + 10f, head.y, NameWidth, head.height), "OBJECT TYPE", caption);
            if (hintWidth >= MinHintWidth) GUI.Label(new Rect(head.x + 10f + NameWidth, head.y, hintWidth, head.height), "WHAT IT IS", caption);
            GUI.Label(new Rect(head.xMax - dropWidth - CountWidth - 10f, head.y, CountWidth, head.height), "COUNT", new GUIStyle(caption) { alignment = TextAnchor.MiddleRight });
            GUI.Label(new Rect(head.xMax - dropWidth, head.y, dropWidth, head.height), "DRAWN INTO LAYER", caption);

            foreach (var kind in MapLabLayerClaims.Kinds)
            {
                MapLabBakeLayerSettings owner = MapLabLayerTypes.Owner(map, kind);
                bool mine = selected != null && owner == selected;
                Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
                if (mine && Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(row, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 1f, EditorGUIUtility.isProSkin ? Highlight : HighlightLight, 0f, 3f);
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 2f, 3f, row.height - 4f), Bar);
                }
                GUI.Label(new Rect(row.x + 10f, row.y, NameWidth, row.height), MapLabLayerClaims.KindLabel(kind), mine ? MapLabWindowStyles.RowBold : MapLabWindowStyles.Row);
                if (hintWidth >= MinHintWidth) GUI.Label(new Rect(row.x + 10f + NameWidth, row.y, hintWidth, row.height), MapLabLayerClaims.KindHint(kind), MapLabWindowStyles.Dim);
                int count = totals != null && totals.TryGetValue(kind, out int found) ? found : 0;
                GUI.Label(new Rect(row.xMax - dropWidth - CountWidth - 10f, row.y, CountWidth, row.height), count.ToString(), MapLabWindowStyles.DimRight);
                var drop = new Rect(row.xMax - dropWidth, row.y + 3f, dropWidth, 18f);
                string current = owner != null ? owner.Name : MapLabLayerTypes.KeepLive;
                if (GUI.Button(drop, new GUIContent(current, "Pick the layer this type is drawn into. Keep live = not baked, it stays a normal object"), EditorStyles.popup)) Menu(map, kind, owner, changed).DropDown(drop);
            }

            GUILayout.Space(4f);
            GUILayout.Label("Change the layer and that type moves there. Keep live = not baked, it stays a normal object. Objects in a Decorate set skip this table.", MapLabWindowStyles.Wrap);
        }
        #endregion

        #region [Private Methods]
        private static GenericMenu Menu(MapLabMap map, MapLabBakeKinds kind, MapLabBakeLayerSettings owner, Action changed)
        {
            var menu = new GenericMenu();
            List<MapLabBakeLayerSettings> targets = MapLabLayerTypes.Targets(map, kind);
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                string name = targets[i].Name;
                menu.AddItem(new GUIContent(name), targets[i] == owner, () =>
                {
                    MapLabLayerTypes.Route(map, kind, name);
                    changed?.Invoke();
                });
            }
            if (targets.Count == 0)
            {
                string missing = kind == MapLabBakeKinds.SortedShapes ? "No sorted layer yet: Add Layer > Sorted layer" : "No layer above the line yet";
                menu.AddDisabledItem(new GUIContent(missing));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Keep live (not baked)"), owner == null, () =>
            {
                MapLabLayerTypes.Route(map, kind, null);
                changed?.Invoke();
            });
            return menu;
        }
        #endregion
    }
}
