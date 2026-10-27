using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace MapLab.Editor
{
    public static class MapLabOutputPaths
    {
        #region [Variables/Fields]
        public const string FolderSuffix = "_MapLab";
        public const string WalkableFile = "Walkable.json";
        public const string WalkableMaskFile = "Walkable_Mask.png";
        public const string ElevationFile = "Walkable_Elevation.json";
        public const string ElevationMaskFile = "Walkable_Elevation.png";
        #endregion

        #region [Public Methods]
        public static string MapFolder(MapLabMap map)
        {
            Scene scene = map.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene first: MapLab writes its bakes into a folder next to it.");
            string sceneFolder = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            return $"{sceneFolder}/{Path.GetFileNameWithoutExtension(scene.path)}{FolderSuffix}/{MapFolderName(map)}";
        }

        public static string WalkableJson(MapLabMap map)
        {
            return $"{MapFolder(map)}/{WalkableFile}";
        }

        public static string WalkableMask(MapLabMap map)
        {
            return $"{MapFolder(map)}/{WalkableMaskFile}";
        }

        public static string ElevationJson(MapLabMap map)
        {
            return $"{MapFolder(map)}/{ElevationFile}";
        }

        public static string ElevationMask(MapLabMap map)
        {
            return $"{MapFolder(map)}/{ElevationMaskFile}";
        }

        public static string MapFolderName(MapLabMap map)
        {
            string name = Clean(map.name);
            Scene scene = map.gameObject.scene;
            if (!scene.IsValid()) return name;
            var same = MapLabWalkableBaker.FindMaps(scene).Where(other => Clean(other.name) == name).ToList();
            int index = same.IndexOf(map);
            return index > 0 ? $"{name} {index + 1}" : name;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            if (Directory.Exists(folder))
            {
                AssetDatabase.ImportAsset(folder);
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.Refresh();
                if (AssetDatabase.IsValidFolder(folder)) return;
            }
            string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static long SizeOnDisk(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return 0;
            if (File.Exists(assetPath)) return new FileInfo(assetPath).Length;
            if (!Directory.Exists(assetPath)) return 0;
            return Directory.GetFiles(assetPath, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".meta")).Sum(file => new FileInfo(file).Length);
        }

        public static string Size(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / (1024f * 1024f):0.0} MB";
            return $"{Math.Max(1, (bytes + 1023) / 1024)} KB";
        }
        #endregion

        #region [Private Methods]
        private static string Clean(string name)
        {
            string clean = Regex.Replace(name, @"\[[^\]]*\]", " ");
            clean = new string(clean.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray());
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(clean) ? "Map" : clean;
        }
        #endregion
    }
}
