using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace MapLab.Editor
{
    public static class MapLabLayerAtlas
    {
        #region [Variables/Fields]
        private const int LegacyBuildOnly = 3;
        private const int LegacyAlwaysOn = 4;
        private const int AtlasV2 = 5;
        private const int AtlasV2Build = 6;
        private const int Padding = 2;
        #endregion

        #region [Public Methods]
        public static string Ensure(MapLabMap map, string layer)
        {
            int mode = (int)EditorSettings.spritePackerMode;
            bool v2 = mode == AtlasV2 || mode == AtlasV2Build;
            bool v1 = mode == LegacyBuildOnly || mode == LegacyAlwaysOn;
            if (!v1 && !v2) return null;

            string folder = MapLabLayerFiles.LayerFolder(map, layer);
            string path = $"{folder}/{layer}{(v2 ? ".spriteatlasv2" : ".spriteatlas")}";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) return path;

            MapLabOutputPaths.EnsureFolder(MapLabLayerFiles.BaseFolder(map, layer));
            MapLabOutputPaths.EnsureFolder(MapLabLayerFiles.CutRoot(map, layer));
            Object[] packables =
            {
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(MapLabLayerFiles.BaseFolder(map, layer)),
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(MapLabLayerFiles.CutRoot(map, layer))
            };
            var packing = new SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, padding = Padding };
            var texture = new SpriteAtlasTextureSettings { filterMode = FilterMode.Bilinear, generateMipMaps = false, sRGB = true, readable = false };
            if (v2)
            {
                var asset = new SpriteAtlasAsset();
                asset.Add(packables);
                SpriteAtlasAsset.Save(asset, path);
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is SpriteAtlasImporter importer)
                {
                    importer.packingSettings = packing;
                    importer.textureSettings = texture;
                    importer.SaveAndReimport();
                }
                return path;
            }
            var atlas = new SpriteAtlas();
            atlas.SetPackingSettings(packing);
            atlas.SetTextureSettings(texture);
            atlas.Add(packables);
            AssetDatabase.CreateAsset(atlas, path);
            return path;
        }
        #endregion
    }
}
