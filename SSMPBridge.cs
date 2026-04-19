using System;
using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;
using HornetCloakColor.Shared;

namespace HornetCloakColor
{
    /// <summary>
    /// Indirection layer between the plugin and the SSMP assembly.
    ///
    /// SSMP types live only in <c>HornetCloakColor.SSMP.dll</c>, which is loaded at runtime
    /// when the SSMP plugin is present. The main assembly therefore has no SSMP metadata and
    /// loads cleanly in single-player without SSMP installed.
    /// </summary>
    internal static class SSMPBridge
    {
        /// <summary>BepInEx GUID of the SSMP plugin.</summary>
        public const string SSMPGuid = "ssmp";

        private static bool _registered;
        private static Action<CloakColor>? _notifyColorChanged;
        private static Func<ushort, CloakColor>? _getRemoteMapColor;

        /// <summary>True when the SSMP plugin is loaded in BepInEx.</summary>
        public static bool IsAvailable => Chainloader.PluginInfos.ContainsKey(SSMPGuid);

        /// <summary>True after we successfully registered our addons with SSMP.</summary>
        public static bool IsRegistered => _registered;

        /// <summary>
        /// Try to load the satellite assembly and register SSMP addons. No-op if SSMP isn't
        /// loaded. Catches failures so a broken install doesn't break local cloak tinting.
        /// </summary>
        public static bool TryRegister()
        {
            if (_registered) return true;
            if (!IsAvailable) return false;

            try
            {
                if (!TryBindSatellite())
                    return false;

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

        /// <summary>Send the local player's color to the server (no-op when SSMP isn't active).</summary>
        public static void NotifyLocalColorChanged(CloakColor color)
        {
            if (!_registered) return;
            _notifyColorChanged?.Invoke(color);
        }

        /// <summary>Remote player's map icon color when SSMP sync is active; otherwise default white.</summary>
        public static CloakColor GetRemoteMapColorOrDefault(ushort playerId) =>
            _getRemoteMapColor?.Invoke(playerId) ?? CloakColor.Default;

        private static bool TryBindSatellite()
        {
            var dir = Path.GetDirectoryName(typeof(HornetCloakColorPlugin).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                Log.Warn("Could not resolve plugin directory; cannot load HornetCloakColor.SSMP.dll.");
                return false;
            }

            var path = Path.Combine(dir, "HornetCloakColor.SSMP.dll");
            if (!File.Exists(path))
            {
                Log.Warn(
                    "SSMP is loaded but HornetCloakColor.SSMP.dll was not found next to HornetCloakColor.dll. " +
                    "Reinstall the mod so both DLLs are in the same folder.");
                return false;
            }

            var asm = Assembly.LoadFrom(path);
            var entry = asm.GetType("HornetCloakColor.SSMPIntegration.SatelliteEntry");
            if (entry == null)
            {
                Log.Warn("HornetCloakColor.SSMP.dll is missing the integration entry type.");
                return false;
            }

            var register = entry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            register?.Invoke(null, null);

            var notify = entry.GetMethod("NotifyLocalColorChanged", BindingFlags.Public | BindingFlags.Static);
            if (notify != null)
                _notifyColorChanged = (Action<CloakColor>)Delegate.CreateDelegate(typeof(Action<CloakColor>), notify);

            var getRemote = entry.GetMethod("GetRemoteMapColorOrDefault", BindingFlags.Public | BindingFlags.Static);
            if (getRemote != null)
                _getRemoteMapColor = (Func<ushort, CloakColor>)Delegate.CreateDelegate(typeof(Func<ushort, CloakColor>), getRemote);

            return true;
        }
    }
}
