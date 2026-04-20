using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP 0.1.0 <c>MapManager.HeroControllerOnUpdate</c> used an inverted compass check: with default
    /// <c>OnlyBroadcastMapIconWithCompass</c>, it cleared <c>hasMapIcon</c> when the compass <i>was</i> equipped,
    /// so the server never stored <c>HasMapIcon</c> and clients never created remote compass clones.
    /// FreeCompass makes the compass effectively always equipped, so the bug showed up as "no one sees anyone".
    /// This prefix replaces the whole callback with the corrected logic so stock SSMP.dll still works.
    /// SSMP (and an earlier version of this patch) also tied <c>hasMapIcon</c> to <c>TryGetMapPosition</c>,
    /// which is false while <c>GameManager.gameMap</c> is null (often until the map UI has been opened).
    /// The joiner may open the map immediately and notify the server; the host can keep <c>gameMap</c> null
    /// and never send <c>HasIcon</c>, so clients only see the joiner's pin. The network flag follows
    /// compass/settings only; position packets still require a resolvable map position.
    /// </summary>
    internal static class SsmMapCompassBroadcastFixPatcher
    {
        private static bool _applied;
        private static float _nextLogDeferMapPos;
        private static float _nextLogMapPosSent;

        internal static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            if (!SSMPBridge.IsAvailable) return;

            var mapManagerType = AccessTools.TypeByName("SSMP.Game.Client.MapManager");
            if (mapManagerType == null) return;

            var target = AccessTools.Method(mapManagerType, "HeroControllerOnUpdate", new[] { typeof(HeroController) });
            if (target == null)
            {
                Log.Warn("SSMP MapManager.HeroControllerOnUpdate not found — compass broadcast fix skipped");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(SsmMapCompassBroadcastFixPatcher), nameof(HeroControllerOnUpdate_Prefix))));

            _applied = true;
            Log.Info("Hooked SSMP MapManager.HeroControllerOnUpdate — corrected compass map-icon broadcast logic");
        }

        private static bool HeroControllerOnUpdate_Prefix(object __instance, HeroController heroController)
        {
            if (__instance == null) return true;

            try
            {
                var mmType = __instance.GetType();
                if (mmType.FullName != "SSMP.Game.Client.MapManager")
                    return true;

                var netClient = mmType.GetField("_netClient", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(__instance);
                if (netClient == null) return true;

                var connected = (bool)netClient.GetType().GetProperty("IsConnected")!.GetValue(netClient)!;
                if (!connected) return true;

                var settings = mmType.GetField("_serverSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(__instance);
                if (settings == null) return true;

                var st = settings.GetType();
                var alwaysShow = (bool)st.GetProperty("AlwaysShowMapIcons")!.GetValue(settings)!;
                var onlyCompass = (bool)st.GetProperty("OnlyBroadcastMapIconWithCompass")!.GetValue(settings)!;

                var posOk = TryInvokeTryGetMapPosition(__instance, out var newPosition);

                // Do not require TryGetMapPosition for the *network* icon flag — vanilla does, which leaves
                // HasMapIcon false on the server until GameMap exists (often only after opening the map).
                var hasMapIcon = true;
                if (!alwaysShow)
                {
                    if (!onlyCompass)
                        hasMapIcon = false;
                    else if (!IsCompassEquipped())
                        hasMapIcon = false;
                }

                var lastSentField = mmType.GetField("_lastSentMapIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                if (lastSentField == null)
                {
                    Log.Warn("[MapIcon] MapManager._lastSentMapIcon not found — compass broadcast fix skipped for this frame (vanilla runs).");
                    return true;
                }

                var lastSent = (bool)lastSentField.GetValue(__instance)!;
                if (hasMapIcon != lastSent)
                {
                    lastSentField.SetValue(__instance, hasMapIcon);
                    var updateManager = netClient.GetType().GetProperty("UpdateManager")!.GetValue(netClient)!;
                    updateManager.GetType().GetMethod("UpdatePlayerMapIcon", new[] { typeof(bool) })!
                        .Invoke(updateManager, new object[] { hasMapIcon });

                    if (CloakPaletteConfig.LogMapIconDiagnostics)
                    {
                        Log.Info(
                            $"[MapIcon] Queued PlayerMapUpdate hasIcon={hasMapIcon} " +
                            $"(TryGetMapPosition={posOk}, alwaysShow={alwaysShow}, onlyCompass={onlyCompass}, compassEquipped={IsCompassEquipped()}).");
                    }

                    if (!hasMapIcon)
                    {
                        var lastPosField = mmType.GetField("_lastPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                        lastPosField?.SetValue(__instance, Vector2.zero);
                    }
                }

                if (!hasMapIcon || GameManager.instance == null || GameManager.instance.IsInSceneTransition)
                    return false;

                if (!posOk)
                {
                    if (CloakPaletteConfig.LogMapIconDiagnostics
                        && Time.realtimeSinceStartup >= _nextLogDeferMapPos)
                    {
                        _nextLogDeferMapPos = Time.realtimeSinceStartup + 2f;
                        Log.Info(
                            "[MapIcon] Network HasIcon is true but TryGetMapPosition=false (GameMap often null until the map opens); " +
                            "position packets deferred.");
                    }
                    return false;
                }

                var lastPosField2 = mmType.GetField("_lastPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                if (lastPosField2 == null)
                {
                    Log.Warn("[MapIcon] MapManager._lastPosition not found — cannot send map position from compass fix.");
                    return false;
                }

                var lastPosition = (Vector2)lastPosField2.GetValue(__instance)!;
                if (newPosition == lastPosition)
                    return false;

                var ssmpVecType = AccessTools.TypeByName("SSMP.Math.Vector2");
                if (ssmpVecType == null)
                {
                    Log.Warn("[MapIcon] SSMP.Math.Vector2 type not resolved — cannot send map position.");
                    return false;
                }

                var mathVec = Activator.CreateInstance(ssmpVecType, newPosition.x, newPosition.y);
                var updateManager2 = netClient.GetType().GetProperty("UpdateManager")!.GetValue(netClient)!;
                updateManager2.GetType().GetMethod("UpdatePlayerMapPosition", new[] { ssmpVecType })!
                    .Invoke(updateManager2, new[] { mathVec });

                lastPosField2.SetValue(__instance, newPosition);
                if (CloakPaletteConfig.LogMapIconDiagnostics
                    && Time.realtimeSinceStartup >= _nextLogMapPosSent)
                {
                    _nextLogMapPosSent = Time.realtimeSinceStartup + 0.75f;
                    Log.Info($"[MapIcon] Sent map position update ({newPosition.x:F1}, {newPosition.y:F1}).");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Warn($"SsmMapCompassBroadcastFix: falling back to vanilla MapManager update ({ex.Message})");
                return true;
            }
        }

        private static bool TryInvokeTryGetMapPosition(object mapManager, out Vector2 mapPosition)
        {
            mapPosition = Vector2.zero;
            var m = mapManager.GetType().GetMethod(
                "TryGetMapPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) return false;

            var args = new object[] { Vector2.zero };
            var ok = (bool)m.Invoke(mapManager, args)!;
            mapPosition = (Vector2)args[0];
            return ok;
        }

        private static bool IsCompassEquipped()
        {
            var gameplayType = AccessTools.TypeByName("Gameplay");
            if (gameplayType == null) return false;

            var compassProp = gameplayType.GetProperty("CompassTool", BindingFlags.Public | BindingFlags.Static);
            var compass = compassProp?.GetValue(null);
            if (compass == null) return false;

            var eq = compass.GetType().GetProperty("IsEquipped");
            return eq != null && (bool)eq.GetValue(compass)!;
        }
    }
}
