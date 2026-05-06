using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP's <c>MapManager</c> toggles <c>_displayingIcons</c> from the zoomed-in <c>GameMap</c>
    /// (<c>PositionCompassAndCorpse</c> / <c>CloseQuickMap</c>). The maintainers intend remote players'
    /// icons <b>not</b> to appear on the wide zoomed-out area map (HKMP-style — icons show when zoomed in).
    /// We only run <see cref="RefreshZoomedGameMapRemoteIcons"/> from the zoomed <c>GameMap</c> hook so deferred
    /// remote icons materialize and stay consistent with SSMP when that UI is open — not from <c>InventoryWideMap</c>.
    /// </summary>
    internal static class RemoteMapIconVisibility
    {
        private static object? _clientManager;
        private static float _nextSyncLogTime;

        internal static void TryApplyClientManagerHook(Harmony harmony)
        {
            if (!SSMPBridge.IsAvailable) return;

            var cmType = SsmpReflect.ClientManager;
            var smType = SsmpReflect.ServerManager;
            if (cmType == null || smType == null) return;

            var init = AccessTools.Method(cmType, "Initialize", new[] { smType });
            if (init == null) return;

            harmony.Patch(
                init,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(RemoteMapIconVisibility), nameof(ClientManager_Initialize_Postfix))));
            Log.Info("Hooked SSMP ClientManager.Initialize (zoomed GameMap remote icon sync)");
        }

        private static void ClientManager_Initialize_Postfix(object __instance)
        {
            if (__instance != null)
                _clientManager = __instance;
        }

        /// <summary>
        /// Call when the zoomed-in <c>GameMap</c> is laying out compass/icons — aligns SSMP
        /// <c>_displayingIcons</c>, refreshes active remote pins, and retries deferred <c>CreatePlayerIcon</c>.
        /// No-op without SSMP or before <c>ClientManager.Initialize</c>. Not used for the wide zoomed-out map.
        /// </summary>
        internal static void RefreshZoomedGameMapRemoteIcons()
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
                        Log.Warn("[MapIcon] RefreshZoomedGameMapRemoteIcons: ClientManager has no _mapManager.");
                    return;
                }

                SetDisplayingIconsAndRefresh(mm, true);
                MapRemoteIconDeferredCreate.TryMaterializePendingIcons(mm);

                if (CloakPaletteConfig.LogMapIconDiagnostics
                    && Time.realtimeSinceStartup >= _nextSyncLogTime)
                {
                    _nextSyncLogTime = Time.realtimeSinceStartup + 1.25f;
                    Log.Info("[MapIcon] RefreshZoomedGameMapRemoteIcons: _displayingIcons=true, UpdateMapIconsActive, materialize pass.");
                }
            }
            catch (Exception ex)
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Warn($"[MapIcon] RefreshZoomedGameMapRemoteIcons: {ex.Message}");
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
