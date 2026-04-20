using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP's <c>MapManager</c> only flips <c>_displayingIcons</c> from
    /// <c>GameMap.PositionCompassAndCorpse</c> / <c>GameMap.CloseQuickMap</c>.
    /// The inventory "wide" world map updates <c>InventoryWideMap</c> without touching that
    /// flag, so remote players' compass clones stay <c>SetActive(false)</c> even though they exist.
    /// Mirror the quick-map "map is open" state while the wide map is being laid out.
    /// </summary>
    internal static class RemoteMapIconVisibility
    {
        private static object? _clientManager;
        private static float _nextSyncLogTime;

        internal static void RegisterClientManager(object clientManager) => _clientManager = clientManager;

        internal static void TryApplyClientManagerHook(Harmony harmony)
        {
            var cmType = AccessTools.TypeByName("SSMP.Game.Client.ClientManager");
            var smType = AccessTools.TypeByName("SSMP.Game.Server.ServerManager");
            if (cmType == null || smType == null) return;

            var init = AccessTools.Method(cmType, "Initialize", new[] { smType });
            if (init == null) return;

            harmony.Patch(
                init,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(RemoteMapIconVisibility), nameof(ClientManager_Initialize_Postfix))));
            Log.Info("Hooked SSMP ClientManager.Initialize (inventory map remote icon visibility)");
        }

        private static void ClientManager_Initialize_Postfix(object __instance)
        {
            if (__instance != null)
                RegisterClientManager(__instance);
        }

        /// <summary>
        /// Call when any in-game map UI is showing remote players' pins (wide inventory map or zoomed GameMap).
        /// </summary>
        internal static void SyncRemoteMapIconsVisible()
        {
            if (_clientManager == null) return;

            try
            {
                var mm = _clientManager.GetType()
                    .GetField("_mapManager", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(_clientManager);
                if (mm == null)
                {
                    if (CloakPaletteConfig.LogMapIconDiagnostics)
                        Log.Warn("[MapIcon] SyncRemoteMapIconsVisible: ClientManager has no _mapManager.");
                    return;
                }

                SetDisplayingIconsAndRefresh(mm, true);
                MapRemoteIconDeferredCreate.TryMaterializePendingIcons(mm);

                if (CloakPaletteConfig.LogMapIconDiagnostics
                    && Time.realtimeSinceStartup >= _nextSyncLogTime)
                {
                    _nextSyncLogTime = Time.realtimeSinceStartup + 1.25f;
                    Log.Info("[MapIcon] SyncRemoteMapIconsVisible: set _displayingIcons=true, UpdateMapIconsActive, materialize pass.");
                }
            }
            catch (Exception ex)
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Warn($"[MapIcon] SyncRemoteMapIconsVisible: {ex.Message}");
            }
        }

        private static void SetDisplayingIconsAndRefresh(object mapManager, bool showing)
        {
            try
            {
                var mmType = mapManager.GetType();
                mmType.GetField("_displayingIcons", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(mapManager, showing);
                mmType.GetMethod("UpdateMapIconsActive", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(mapManager, null);
            }
            catch
            {
                // Non-fatal; SSMP internals may change between versions.
            }
        }
    }
}
