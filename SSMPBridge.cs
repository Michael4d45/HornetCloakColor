using System;
using BepInEx.Bootstrap;
using HornetCloakColor.Shared;

namespace HornetCloakColor
{
    /// <summary>
    /// Indirection layer between the plugin and the SSMP assembly.
    ///
    /// SSMP is a <i>soft</i> dependency: the mod also runs single-player. Every method here
    /// touches SSMP types only inside its body, so the CLR will only resolve them on first
    /// invocation. As long as <see cref="IsAvailable"/> is checked before calling into the
    /// "*Core" methods, the assembly loads fine even when SSMP.dll isn't present.
    /// </summary>
    internal static class SSMPBridge
    {
        /// <summary>BepInEx GUID of the SSMP plugin.</summary>
        public const string SSMPGuid = "ssmp";

        private static bool _registered;

        /// <summary>True when the SSMP plugin is loaded in BepInEx.</summary>
        public static bool IsAvailable => Chainloader.PluginInfos.ContainsKey(SSMPGuid);

        /// <summary>True after we successfully registered our addons with SSMP.</summary>
        public static bool IsRegistered => _registered;

        /// <summary>
        /// Try to register the client/server addons with SSMP. No-op (and safe) if SSMP isn't
        /// loaded. Catches type-load failures so a broken SSMP version doesn't break us.
        /// </summary>
        public static bool TryRegister()
        {
            if (_registered) return true;
            if (!IsAvailable) return false;

            try
            {
                RegisterCore();
                _registered = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"SSMP detected but addon registration failed: {ex.Message}. " +
                         "Multiplayer cloak sync disabled; local recolor still works.");
                return false;
            }
        }

        // Body kept in a separate method so SSMP types are only JITted when SSMP is present.
        private static void RegisterCore()
        {
            var client = new HornetCloakColor.Client.ClientAddon();
            var server = new HornetCloakColor.Server.ServerAddon();
            SSMP.Api.Client.ClientAddon.RegisterAddon(client);
            SSMP.Api.Server.ServerAddon.RegisterAddon(server);
        }

        /// <summary>Send the local player's color to the server (no-op when SSMP isn't loaded).</summary>
        public static void NotifyLocalColorChanged(CloakColor color)
        {
            if (!_registered) return;
            NotifyLocalColorChangedCore(color);
        }

        private static void NotifyLocalColorChangedCore(CloakColor color)
        {
            HornetCloakColor.Client.ClientAddon.Instance?.SetLocalColor(color);
        }
    }
}
