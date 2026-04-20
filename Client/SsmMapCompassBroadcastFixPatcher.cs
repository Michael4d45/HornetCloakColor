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
    ///
    /// <para><b>Performance:</b> This prefix runs on <i>every</i> hero tick while the net client is connected
    /// (including listen-host). All reflection metadata is resolved once at patch time — doing
    /// <c>GetField</c>/<c>GetMethod</c> every frame caused severe host lag.</para>
    /// </summary>
    internal static class SsmMapCompassBroadcastFixPatcher
    {
        private static bool _applied;
        private static float _nextLogDeferMapPos;
        private static float _nextLogMapPosSent;

        /// <summary>Cached reflection for <c>SSMP.Game.Client.MapManager</c> and related types.</summary>
        private static class MapBroadcastReflect
        {
            internal static bool Ready { get; private set; }

            internal static FieldInfo? NetClient;
            internal static PropertyInfo? NetIsConnected;
            internal static FieldInfo? ServerSettings;
            internal static PropertyInfo? AlwaysShowMapIcons;
            internal static PropertyInfo? OnlyBroadcastMapIconWithCompass;
            internal static FieldInfo? LastSentMapIcon;
            internal static FieldInfo? LastPosition;
            internal static MethodInfo? TryGetMapPosition;
            internal static PropertyInfo? NetUpdateManager;
            internal static MethodInfo? UpdatePlayerMapIconBool;
            internal static MethodInfo? UpdatePlayerMapPosition;
            internal static ConstructorInfo? SsmpVecCtorFf;

            internal static PropertyInfo? GameplayCompassTool;
            internal static PropertyInfo? CompassIsEquipped;

            internal static object[]? TryGetMapPosArgs;
            internal static object[]? InvokeOneArg;

            internal static bool TryBuild(Type mapManagerType)
            {
                if (Ready) return true;

                try
                {
                    NetClient = mapManagerType.GetField("_netClient", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (NetClient == null) return false;

                    var netType = NetClient.FieldType;
                    NetIsConnected = netType.GetProperty("IsConnected", BindingFlags.Public | BindingFlags.Instance);
                    if (NetIsConnected == null) return false;

                    NetUpdateManager = netType.GetProperty("UpdateManager", BindingFlags.Public | BindingFlags.Instance);
                    if (NetUpdateManager == null) return false;

                    var updateMgrType = NetUpdateManager.PropertyType;
                    UpdatePlayerMapIconBool = updateMgrType.GetMethod(
                        "UpdatePlayerMapIcon",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(bool) },
                        null);
                    if (UpdatePlayerMapIconBool == null) return false;

                    ServerSettings = mapManagerType.GetField("_serverSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (ServerSettings == null) return false;

                    var settingsType = ServerSettings.FieldType;
                    AlwaysShowMapIcons = settingsType.GetProperty(
                        "AlwaysShowMapIcons",
                        BindingFlags.Public | BindingFlags.Instance);
                    OnlyBroadcastMapIconWithCompass = settingsType.GetProperty(
                        "OnlyBroadcastMapIconWithCompass",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (AlwaysShowMapIcons == null || OnlyBroadcastMapIconWithCompass == null) return false;

                    LastSentMapIcon = mapManagerType.GetField("_lastSentMapIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                    LastPosition = mapManagerType.GetField("_lastPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (LastSentMapIcon == null || LastPosition == null) return false;

                    TryGetMapPosition = mapManagerType.GetMethod(
                        "TryGetMapPosition",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (TryGetMapPosition == null) return false;

                    var ssmpVecType = AccessTools.TypeByName("SSMP.Math.Vector2");
                    if (ssmpVecType == null) return false;

                    SsmpVecCtorFf = ssmpVecType.GetConstructor(new[] { typeof(float), typeof(float) });
                    if (SsmpVecCtorFf == null) return false;

                    UpdatePlayerMapPosition = updateMgrType.GetMethod(
                        "UpdatePlayerMapPosition",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { ssmpVecType },
                        null);
                    if (UpdatePlayerMapPosition == null) return false;

                    var gameplayType = AccessTools.TypeByName("Gameplay");
                    if (gameplayType == null) return false;

                    GameplayCompassTool = gameplayType.GetProperty("CompassTool", BindingFlags.Public | BindingFlags.Static);
                    if (GameplayCompassTool == null) return false;

                    var compassType = GameplayCompassTool.PropertyType;
                    CompassIsEquipped = compassType.GetProperty("IsEquipped", BindingFlags.Public | BindingFlags.Instance);
                    if (CompassIsEquipped == null) return false;

                    TryGetMapPosArgs = new object[1];
                    InvokeOneArg = new object[1];

                    Ready = true;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            if (!SSMPBridge.IsAvailable) return;

            var mapManagerType = AccessTools.TypeByName("SSMP.Game.Client.MapManager");
            if (mapManagerType == null) return;

            if (!MapBroadcastReflect.TryBuild(mapManagerType))
            {
                Log.Warn("SsmMapCompassBroadcastFix: could not cache MapManager reflection — compass broadcast fix skipped.");
                return;
            }

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
            if (__instance == null || !MapBroadcastReflect.Ready) return true;

            try
            {
                var netClient = MapBroadcastReflect.NetClient!.GetValue(__instance);
                if (netClient == null) return true;

                if (!(bool)MapBroadcastReflect.NetIsConnected!.GetValue(netClient)!) return true;

                var settings = MapBroadcastReflect.ServerSettings!.GetValue(__instance);
                if (settings == null) return true;

                var alwaysShow = (bool)MapBroadcastReflect.AlwaysShowMapIcons!.GetValue(settings)!;
                var onlyCompass = (bool)MapBroadcastReflect.OnlyBroadcastMapIconWithCompass!.GetValue(settings)!;

                var posOk = TryInvokeTryGetMapPosition(__instance, out var newPosition);

                var hasMapIcon = true;
                if (!alwaysShow)
                {
                    if (!onlyCompass)
                        hasMapIcon = false;
                    else if (!IsCompassEquipped())
                        hasMapIcon = false;
                }

                var lastSent = (bool)MapBroadcastReflect.LastSentMapIcon!.GetValue(__instance)!;
                if (hasMapIcon != lastSent)
                {
                    MapBroadcastReflect.LastSentMapIcon.SetValue(__instance, hasMapIcon);

                    var updateManager = MapBroadcastReflect.NetUpdateManager!.GetValue(netClient);
                    if (updateManager == null)
                    {
                        Log.Warn("[MapIcon] MapManager._netClient.UpdateManager is null — compass broadcast fix skipped for this frame.");
                        return true;
                    }

                    MapBroadcastReflect.UpdatePlayerMapIconBool!.Invoke(updateManager, new object[] { hasMapIcon });

                    if (CloakPaletteConfig.LogMapIconDiagnostics)
                    {
                        Log.Info(
                            $"[MapIcon] Queued PlayerMapUpdate hasIcon={hasMapIcon} " +
                            $"(TryGetMapPosition={posOk}, alwaysShow={alwaysShow}, onlyCompass={onlyCompass}, compassEquipped={IsCompassEquipped()}).");
                    }

                    if (!hasMapIcon)
                        MapBroadcastReflect.LastPosition!.SetValue(__instance, Vector2.zero);
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

                var lastPosition = (Vector2)MapBroadcastReflect.LastPosition!.GetValue(__instance)!;
                if (newPosition == lastPosition)
                    return false;

                var mathVec = MapBroadcastReflect.SsmpVecCtorFf!.Invoke(new object[] { newPosition.x, newPosition.y });
                var updateManager2 = MapBroadcastReflect.NetUpdateManager!.GetValue(netClient);
                if (updateManager2 == null)
                {
                    Log.Warn("[MapIcon] UpdateManager null while sending map position.");
                    return false;
                }

                MapBroadcastReflect.InvokeOneArg![0] = mathVec;
                MapBroadcastReflect.UpdatePlayerMapPosition!.Invoke(updateManager2, MapBroadcastReflect.InvokeOneArg);

                MapBroadcastReflect.LastPosition.SetValue(__instance, newPosition);
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
            MapBroadcastReflect.TryGetMapPosArgs![0] = Vector2.zero;
            var ok = (bool)MapBroadcastReflect.TryGetMapPosition!.Invoke(mapManager, MapBroadcastReflect.TryGetMapPosArgs)!;
            mapPosition = (Vector2)MapBroadcastReflect.TryGetMapPosArgs[0];
            return ok;
        }

        private static bool IsCompassEquipped()
        {
            var compass = MapBroadcastReflect.GameplayCompassTool!.GetValue(null);
            if (compass == null) return false;

            return (bool)MapBroadcastReflect.CompassIsEquipped!.GetValue(compass)!;
        }
    }
}
