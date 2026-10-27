using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MapLab.Editor
{
    public static class MapLabLayerNames
    {
        #region [Variables/Fields]
        public const string PaintToken = "_paint";
        private static readonly Regex PaintPattern = new Regex(@"^(?<layer>.+?)_paint(?:_(?<label>.*))?$", RegexOptions.IgnoreCase);
        #endregion

        #region [Public Methods]
        public static bool TryParsePaint(string name, IEnumerable<string> bakeLayers, out string layer, out string paintName)
        {
            layer = null;
            paintName = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string trimmed = name.Trim();
            foreach (var candidate in bakeLayers.Where(item => !string.IsNullOrEmpty(item)).OrderByDescending(item => item.Length))
            {
                string prefix = candidate + PaintToken;
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = trimmed.Substring(prefix.Length);
                if (rest.Length > 0 && rest[0] != '_') continue;
                layer = candidate;
                paintName = candidate + PaintToken + rest;
                return true;
            }
            return false;
        }

        public static bool LooksLikePaint(string name, out string layer)
        {
            layer = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            Match match = PaintPattern.Match(name.Trim());
            if (!match.Success) return false;
            layer = match.Groups["layer"].Value;
            return true;
        }

        public static bool IsBakeLayer(string name, IEnumerable<string> bakeLayers)
        {
            return !string.IsNullOrWhiteSpace(name) && bakeLayers.Any(item => string.Equals(item, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static string RenamePaint(string paintName, string oldLayer, string newLayer)
        {
            return newLayer + paintName.Substring(oldLayer.Length);
        }

        public static bool IsValidLayerName(string name, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(name)) reason = "A layer needs a name.";
            else if (name.IndexOf(PaintToken, StringComparison.OrdinalIgnoreCase) >= 0) reason = $"'{PaintToken}' is reserved for paint layers.";
            else if (name.TrimStart().StartsWith("[", StringComparison.Ordinal)) reason = "A layer name cannot start with '[' (MapLab keeps [Actors] for the actors line).";
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) reason = "The name is used as a folder name, so it cannot contain \\ / : * ? \" < > |.";
            return reason == null;
        }
        #endregion
    }
}
