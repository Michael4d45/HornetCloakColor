using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HornetCloakColor.Server
{
    /// <summary>
    /// Optional JSON next to <c>HornetCloakColor.SSMP.dll</c> so dedicated/console hosts can set rules
    /// without BepInEx. In-game hosts can use the same file or rely on the default.
    /// </summary>
    internal static class ServerUsernameRulesStore
    {
        private const string FileName = "HornetCloakColor.server.json";

        /// <summary>
        /// When <c>customUsernameColorsOverrideTeamColors</c> is true, username tints from the mod
        /// override SSMP team colors. Default false (team colors win when teams are in use).
        /// </summary>
        internal static bool LoadCustomUsernameColorsOverrideTeamColors()
        {
            try
            {
                var dir = Path.GetDirectoryName(typeof(ServerAddon).Assembly.Location);
                if (string.IsNullOrEmpty(dir)) return false;

                var path = Path.Combine(dir, FileName);
                if (!File.Exists(path)) return false;

                var text = File.ReadAllText(path);
                var m = Regex.Match(
                    text,
                    @"""customUsernameColorsOverrideTeamColors""\s*:\s*(true|false)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return m.Success && string.Equals(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
