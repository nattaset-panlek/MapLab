using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabMegaStatus
    {
        #region [Public Methods]
        public static HashSet<Vector2Int> BakedMegas(MapLabMap map)
        {
            return new HashSet<Vector2Int>(LayerMegas(map).Values.SelectMany(megas => megas));
        }

        public static Dictionary<string, HashSet<Vector2Int>> LayerMegas(MapLabMap map)
        {
            var layers = new Dictionary<string, HashSet<Vector2Int>>(System.StringComparer.OrdinalIgnoreCase);
            if (!MapLabLayerStack.Saved(map)) return layers;
            int perMega = MapLabLayerFiles.PerMega(map);
            foreach (var layer in MapLabLayerStack.Layers(map).Where(layer => !layer.Sorted))
            {
                if (MapLabLayerBaker.BakedLayer(map, layer.Name) == null) continue;
                layers[layer.Name] = MapLabLayerFiles.Megas(MapLabLayerFiles.BaseFolder(map, layer.Name), perMega);
            }
            return layers;
        }

        public static MapLabBakeStatus.State Of(MapLabMegaChunk mega, HashSet<Vector2Int> baked)
        {
            if (!baked.Contains(mega.Index)) return MapLabBakeStatus.State.NotBaked;
            return mega.BakedCurrent ? MapLabBakeStatus.State.UpToDate : MapLabBakeStatus.State.OutOfDate;
        }

        public static List<Vector2Int> WithState(MapLabMap map, MapLabBakeStatus.State state)
        {
            if (map.World == null) return new List<Vector2Int>();
            HashSet<Vector2Int> baked = BakedMegas(map);
            return map.World.Megas.Where(mega => Of(mega, baked) == state).Select(mega => mega.Index).ToList();
        }

        public static string Bake(MapLabMap map, ICollection<Vector2Int> megas)
        {
            if (megas == null || megas.Count == 0) return "[MapLab] No mega chunk to bake";
            var lines = new List<string>();
            Dictionary<string, List<Renderer>> claims = MapLabLayerClaims.ClaimAll(map, out _);
            foreach (var layer in MapLabLayerStack.LayersBottomToTop(map).Where(layer => !layer.Sorted && !layer.Locked).ToList())
            {
                bool baked = MapLabLayerBaker.BakedLayer(map, layer.Name) != null;
                if (!baked && (!claims.TryGetValue(layer.Name, out List<Renderer> live) || live.Count == 0)) continue;
                lines.Add(MapLabLayerBaker.Bake(map, layer.Name, megas));
            }
            if (lines.Count == 0) return $"[MapLab] '{map.name}': no ground layer has anything to bake";
            string where = string.Join(", ", megas.Take(6).Select(mega => $"{mega.x},{mega.y}")) + (megas.Count > 6 ? ", ..." : "");
            return $"[MapLab] Baked {megas.Count} mega chunks ({where}) of '{map.name}':\n- " + string.Join("\n- ", lines.Select(line => line.Replace("[MapLab] ", "")));
        }
        #endregion
    }
}
