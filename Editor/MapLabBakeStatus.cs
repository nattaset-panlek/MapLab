using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabBakeStatus
    {
        #region [Variables/Fields]
        private const string WalkableStampProperty = "_walkableStamp";
        private const string SourceStampProperty = "_sourceStamp";
        private static readonly HashSet<string> IgnoredProperties = new HashSet<string>
        {
            "m_Enabled", "m_GameObject", "m_Script", "m_ObjectHideFlags", "_visualBaked", "_shadowBaked", "_preset", "_baked"
        };
        #endregion

        #region [Properties]
        public enum State
        {
            NotBaked,
            UpToDate,
            OutOfDate
        }

        public readonly struct Status
        {
            public readonly State State;
            public readonly string Summary;

            public Status(State state, string summary)
            {
                State = state;
                Summary = summary;
            }
        }
        #endregion

        #region [Public Methods]
        public static Status Walkable(MapLabMap map)
        {
            if (map.WalkableData == null) return new Status(State.NotBaked, "Not baked yet");
            var data = JsonUtility.FromJson<WalkableAreaJson>(map.WalkableData.text);
            int walkable = MapLabWalkableBaker.DecodeCells(data.cells, data.CellCount).Count(cell => cell);
            string detail = $"{data.columns} x {data.rows} cells, {walkable * 100f / Mathf.Max(1, data.CellCount):0.#}% walkable";
            string stamp = new SerializedObject(map).FindProperty(WalkableStampProperty).stringValue;
            if (string.IsNullOrEmpty(stamp)) return new Status(State.OutOfDate, $"Bake again to start tracking changes ({detail})");
            if (stamp != WalkableStamp(map)) return new Status(State.OutOfDate, $"Out of date: {MapLabWalkableChanges.Describe(map) ?? "obstacles or settings changed"} ({detail})");
            return new Status(State.UpToDate, $"Up to date ({detail})");
        }

        public static Status StaticGround(MapLabMap map)
        {
            return StaticGround(map, MapLabLayerClaims.ClaimAll(map, out _));
        }

        public static Status StaticGround(MapLabMap map, Dictionary<string, List<Renderer>> claims)
        {
            List<MapLabBakeLayerSettings> layers = MapLabLayerStack.Layers(map);
            var baked = layers.Where(layer => MapLabLayerBaker.BakedLayer(map, layer.Name) != null).ToList();
            if (baked.Count == 0) return new Status(State.NotBaked, $"Live: {claims.Values.Sum(list => list.Count)} ground objects drawn one by one");
            int outOfDate = baked.Count(layer => Layer(map, layer, claims[layer.Name]).State == State.OutOfDate);
            string detail = $"{baked.Count} of {layers.Count} layers baked";
            if (outOfDate > 0) return new Status(State.OutOfDate, $"Out of date: {outOfDate} baked layers changed ({detail})");
            return new Status(State.UpToDate, $"Up to date ({detail})");
        }

        public static Status Layer(MapLabMap map, MapLabBakeLayerSettings settings, IReadOnlyList<Renderer> live)
        {
            MapLabBakedLayer layer = MapLabLayerBaker.BakedLayer(map, settings.Name);
            int loose = live == null ? 0 : settings.Sorted ? MapLabLayerClaims.CountShapes(live) : live.Count;
            if (layer == null) return new Status(State.NotBaked, loose == 0 ? "Live: nothing matches this layer" : $"Live: {loose} objects drawn one by one");

            MapLabChunkLayer chunks = layer.ChunkLayer;
            MapLabSortedLayer sorted = layer.SortedLayer;
            int pixelsPerUnit = chunks != null ? chunks.PixelsPerUnit : sorted != null ? sorted.PixelsPerUnit : map.BakePixelsPerUnit;
            string detail = chunks != null
                ? $"{chunks.Chunks.Count(chunk => chunk != null)} chunks, {pixelsPerUnit} px/unit"
                : $"{layer.BakedVisuals.Count} shapes in {(sorted != null ? sorted.Pieces.Count : 0)} images, {pixelsPerUnit} px/unit";
            var reasons = new List<string>();
            string stamp = new SerializedObject(layer).FindProperty(SourceStampProperty).stringValue;
            if (stamp != StaticStamp(map, layer)) reasons.Add("baked objects or layer settings changed");
            if (loose > 0) reasons.Add($"{loose} new objects are not in the image");
            bool chunkChanged = chunks != null && Mathf.Abs(chunks.ChunkSize - MapLabChunkRenderer.SnappedChunkSize(map)) > 0.001f;
            if (pixelsPerUnit != map.BakePixelsPerUnit || chunkChanged) reasons.Add("bake settings changed");
            if (reasons.Count > 0) return new Status(State.OutOfDate, $"Out of date: {string.Join(", ", reasons)} ({detail})");
            return new Status(State.UpToDate, $"Up to date ({detail})");
        }

        public static void RecordWalkableStamp(MapLabMap map)
        {
            var serialized = new SerializedObject(map);
            serialized.FindProperty(WalkableStampProperty).stringValue = WalkableStamp(map);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void RecordStaticStamp(MapLabMap map, MapLabBakedLayer layer)
        {
            var serialized = new SerializedObject(layer);
            serialized.FindProperty(SourceStampProperty).stringValue = StaticStamp(map, layer);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static string WalkableStamp(MapLabMap map)
        {
            var text = new StringBuilder();
            AppendRect(text, map.Bounds);
            text.Append(Number(map.CellSize)).Append('|').Append(map.ObstacleLayers.value).Append('\n');
            foreach (var collider in MapLabWalkableBaker.CollectObstacles(map)) AppendCollider(text, collider);
            text.Append(MapLabElevationBaker.StampText(map));
            return Hash128.Compute(text.ToString()).ToString();
        }

        public static string ColliderText(Collider2D collider)
        {
            var text = new StringBuilder();
            AppendCollider(text, collider);
            return text.ToString();
        }

        public static string StaticStamp(MapLabMap map, MapLabBakedLayer layer)
        {
            var text = new StringBuilder();
            MapLabBakeLayerSettings settings = MapLabLayerStack.Find(map, layer.LayerName);
            if (settings != null)
            {
                text.Append((int)settings.Kinds).Append('|');
                foreach (var set in settings.Sets.Where(set => set != null)) text.Append(GlobalObjectId.GetGlobalObjectIdSlow(set)).Append(';');
                text.Append('\n');
            }
            foreach (var renderer in layer.HiddenRenderers) AppendSource(text, renderer);
            foreach (var shape in layer.BakedVisuals) AppendSource(text, shape);
            foreach (var shape in layer.BakedShadows) AppendSource(text, shape);
            foreach (var floor in layer.BakedFloors) AppendFloor(text, floor);
            return Hash128.Compute(text.ToString()).ToString();
        }
        #endregion

        #region [Private Methods]
        private static void AppendFloor(StringBuilder text, MapLabFloor floor)
        {
            AppendSource(text, floor);
            if (floor == null || floor.World == null) return;
            text.Append("world ").Append(floor.World.Version).Append('|').Append(floor.World.TilesPerSide).Append('|');
            foreach (var mega in floor.World.Megas) text.Append(mega.Index.x).Append(',').Append(mega.Index.y).Append(':').Append(mega.Version).Append(';');
            if (floor.World.Palette != null) AppendSerialized(text, floor.World.Palette);
            text.Append('\n');
        }

        private static void AppendSource(StringBuilder text, Component source)
        {
            if (source == null)
            {
                text.Append("missing\n");
                return;
            }
            Transform transform = source.transform;
            AppendVector(text, transform.position);
            AppendVector(text, transform.eulerAngles);
            AppendVector(text, transform.lossyScale);
            AppendSerialized(text, source);
            text.Append('\n');
        }

        private static void AppendSerialized(StringBuilder text, Object target)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (IgnoredProperties.Contains(property.name)) continue;
                string value = Value(property);
                if (value != null) text.Append(property.propertyPath).Append('=').Append(value).Append(';');
            }
        }

        private static string Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                    return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "1" : "0";
                case SerializedPropertyType.Float:
                    return Number(property.floatValue);
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    Color color = property.colorValue;
                    return $"{Number(color.r)},{Number(color.g)},{Number(color.b)},{Number(color.a)}";
                case SerializedPropertyType.ObjectReference:
                    Object reference = property.objectReferenceValue;
                    if (reference == null) return "null";
                    return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long localId) ? $"{guid}:{localId}" : reference.name;
                default:
                    return null;
            }
        }

        private static void AppendCollider(StringBuilder text, Collider2D collider)
        {
            text.Append(collider.GetType().Name).Append('|').Append(collider.gameObject.layer).Append('|');
            Transform transform = collider.transform;
            switch (collider)
            {
                case PolygonCollider2D polygon:
                    for (int path = 0; path < polygon.pathCount; path++)
                    {
                        foreach (var point in polygon.GetPath(path)) AppendVector(text, transform.TransformPoint(point + polygon.offset));
                        text.Append('/');
                    }
                    break;
                case EdgeCollider2D edge:
                    foreach (var point in edge.points) AppendVector(text, transform.TransformPoint(point + edge.offset));
                    break;
                default:
                    AppendVector(text, collider.bounds.min);
                    AppendVector(text, collider.bounds.max);
                    break;
            }
            text.Append('\n');
        }

        private static void AppendRect(StringBuilder text, Rect rect)
        {
            text.Append(Number(rect.x)).Append(',').Append(Number(rect.y)).Append(',').Append(Number(rect.width)).Append(',').Append(Number(rect.height)).Append('|');
        }

        private static void AppendVector(StringBuilder text, Vector3 vector)
        {
            text.Append(Number(vector.x)).Append(',').Append(Number(vector.y)).Append(',').Append(Number(vector.z)).Append(' ');
        }

        private static string Number(float value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
        #endregion
    }
}
