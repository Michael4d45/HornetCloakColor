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
