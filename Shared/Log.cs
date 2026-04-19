using BepInEx.Logging;

namespace HornetCloakColor.Shared
{
    internal static class Log
    {
        private static ManualLogSource? _fallback;

        private static ManualLogSource Source =>
            HornetCloakColorPlugin.LogSource
            ?? (_fallback ??= Logger.CreateLogSource("HornetCloakColor"));

        public static void Info(string msg) => Source.LogInfo(msg);
        public static void Warn(string msg) => Source.LogWarning(msg);
        public static void Error(string msg) => Source.LogError(msg);
        public static void Debug(string msg) => Source.LogDebug(msg);
    }
}
