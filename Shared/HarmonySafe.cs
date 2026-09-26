using System;

namespace HornetCloakColor.Shared
{
    /// <summary>Swallows Harmony postfix exceptions so a bad tint hook cannot break game callbacks.</summary>
    internal static class HarmonySafe
    {
        internal static void Run(Action body, string context)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Log.Warn($"{context}: {ex}");
            }
        }
    }
}
