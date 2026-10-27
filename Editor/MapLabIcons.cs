using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabIcons
    {
        #region [Variables/Fields]
        public const string ExtrudedShape = "MapLab_ExtrudedShape";
        public const string PathShape = "MapLab_PathShape";
        public const string Map = "MapLab_Map";
        public const string Occluder = "MapLab_Occluder";
        public const string Navigation = "MapLab_Navigation";
        public const string Tier = "MapLab_Tier";
        public const string Logo = "MapLab_Logo";
        public const string FigmaSource = "https://www.figma.com/design/zZ5QnVfPMSFuiMFjpIGbOC/Whiper-of-pale?node-id=1631-4186";

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        #endregion

        #region [Properties]
        public static string Folder => MapLabSceneBuilder.RootFolder + "/Editor/Icons";
        #endregion

        #region [Public Methods]
        public static string PathOf(string name)
        {
            return $"{Folder}/{name}.png";
        }

        public static Texture2D Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(name));
            if (texture != null) Cache[name] = texture;
            return texture;
        }
        #endregion
    }
}
