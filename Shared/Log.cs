using SSMP.Logging;

namespace HornetCloakColor.Shared
{
    internal class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Error(string message) { }
        public void Info(string message) { }
        public void Message(string message) { }
        public void Warn(string message) { }
    }

    internal static class Log
    {
        static ILogger _logger = new NullLogger();

        public static void SetLogger(ILogger logger) => _logger = logger;

        public static void Info(string msg) => _logger.Info($"[HornetCloakColor] {msg}");
        public static void Warn(string msg) => _logger.Warn($"[HornetCloakColor] {msg}");
        public static void Error(string msg) => _logger.Error($"[HornetCloakColor] {msg}");
        public static void Debug(string msg) => _logger.Debug($"[HornetCloakColor] {msg}");
    }
}
