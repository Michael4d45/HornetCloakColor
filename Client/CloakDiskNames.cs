using System;
using System.IO;
using System.Text;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Filesystem-safe folder and file stems for <see cref="CloakMaskManager"/> mask PNGs
    /// (<c>&lt;collection&gt;/&lt;texture&gt;.png</c> under <c>CloakMasks/</c>).
    /// </summary>
    internal static class CloakDiskNames
    {
        public const string NoCollectionFolder = "_NoCollection";

        public static string CollectionFolder(string? tk2dCollectionName) =>
            string.IsNullOrWhiteSpace(tk2dCollectionName) ? NoCollectionFolder : SanitizeFileStem(tk2dCollectionName);

        /// <summary>
        /// Secondary compatibility folder after <see cref="CollectionFolder"/> of the raw tk2d name: SSMP remote
        /// bodies report <c>Player Prefab</c> while shipped gameplay masks often live under <c>Knight/</c>.
        /// Resolution tries <c>CloakMasks/&lt;raw&gt;/</c> first (dumps, hand-authored masks), then this alias.
        /// </summary>
        public static string? MaskCollectionNameForLookup(string? tk2dCollectionName)
        {
            if (string.Equals(tk2dCollectionName, "Player Prefab", StringComparison.OrdinalIgnoreCase))
                return "Knight";

            return tk2dCollectionName;
        }

        /// <summary>
        /// First token of a tk2d material name. Patchwork keeps this (<c>atlas0</c>) after it
        /// replaces <c>mainTexture.name</c> with a Unity temp-buffer name.
        /// <c>atlas0 (Instance)</c> → <c>atlas0</c>.
        /// </summary>
        public static string? MaterialSpritesheetStem(string? materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return null;

            var token = materialName.Trim();
            var space = token.IndexOf(' ');
            if (space == 0)
                return null;
            if (space > 0)
                token = token.Substring(0, space);

            if (token.Length == 0 || token == "(Instance)" || token == "(Clone)")
                return null;

            return SanitizeFileStem(token);
        }

        public static string SanitizeFileStem(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "tex";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(Math.Max(name.Length, 4));
            foreach (var ch in name)
            {
                if (Array.IndexOf(invalid, ch) >= 0 || ch < 32)
                    sb.Append('_');
                else
                    sb.Append(ch);
            }

            var s = sb.ToString();
            if (s.Length == 0 || s == "." || s == "..")
                return "tex";
            return s;
        }
    }
}
