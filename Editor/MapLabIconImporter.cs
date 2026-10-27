using System;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabIconImporter
    {
        #region [Variables/Fields]
        private static readonly string[] IconNames =
        {
            MapLabIcons.ExtrudedShape, MapLabIcons.Tier, MapLabIcons.PathShape, MapLabIcons.Map,
            MapLabIcons.Occluder, MapLabIcons.Navigation, MapLabIcons.Logo
        };

        private static readonly (string Icon, string TypeName)[] ScriptIcons =
        {
            (MapLabIcons.ExtrudedShape, "MapLab.Shapes.ExtrudedShape"),
            (MapLabIcons.PathShape, "MapLab.Shapes.PathShape"),
            (MapLabIcons.Map, "MapLab.MapLabMap"),
            (MapLabIcons.Occluder, "MapLab.Visuals.OccluderFadeController"),
            (MapLabIcons.Navigation, "MapLab.Samples.Navigation.NavigationManager")
        };
        #endregion

        #region [Public Methods]
        [MenuItem("Tools/MapLab/Developer/Apply Icons", false, 104)]
        public static void ApplyFromMenu()
        {
            Debug.Log(Apply());
        }

        public static void ApplyBatch()
        {
            MapLabSceneBuilder.RunBatch(() => Debug.Log(Apply()));
        }

        public static string Apply()
        {
            int imported = 0;
            foreach (string name in IconNames)
            {
                if (Configure(MapLabIcons.PathOf(name))) imported++;
            }
            int assigned = AssignScriptIcons();
            return $"[MapLab] {imported} icons in {MapLabIcons.Folder} set up as editor icons; icon set on {assigned} scripts (design: {MapLabIcons.FigmaSource})";
        }
        #endregion

        #region [Private Methods]
        private static bool Configure(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;
            importer.textureType = TextureImporterType.GUI;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return true;
        }

        private static int AssignScriptIcons()
        {
            int assigned = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { MapLabSceneBuilder.RootFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type type = script != null ? script.GetClass() : null;
                if (type == null) continue;
                foreach (var (icon, typeName) in ScriptIcons)
                {
                    if (type.FullName != typeName) continue;
                    if (AssetImporter.GetAtPath(path) is not MonoImporter importer) continue;
                    importer.SetIcon(MapLabIcons.Load(icon));
                    importer.SaveAndReimport();
                    assigned++;
                }
            }
            return assigned;
        }
        #endregion
    }
}
