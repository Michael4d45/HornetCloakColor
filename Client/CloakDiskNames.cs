using System;
using System.IO;
using System.Text;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Shared filesystem-safe names for <see cref="CloakMaskManager"/> and <see cref="TextureDumper"/>
    /// so dumps and masks line up under the same <c>&lt;collection&gt;/&lt;texture&gt;.png</c> layout.
    /// </summary>
    internal static class CloakDiskNames
    {
        public const string NoCollectionFolder = "_NoCollection";

        public static string CollectionFolder(string? tk2dCollectionName) =>
            string.IsNullOrWhiteSpace(tk2dCollectionName) ? NoCollectionFolder : SanitizeFileStem(tk2dCollectionName);

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
