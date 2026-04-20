using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using HornetCloakColor;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP only broadcasts PlayerMapUpdate when a player's map icon state <i>changes</i>.
    /// Late joiners never receive the current flag for players already in the scene, so their
    /// <c>MapManager</c> never calls <c>CreatePlayerIcon</c> for those players. The server
    /// notifies the <i>entering</i> client of co-scene peers via this replay, but peers already
    /// in the room never get a delta for the entering player if that player's icon flag did not
    /// change — so we also push the entering player's <c>HasMapIcon</c> / <c>MapPosition</c> to
    /// each co-scene peer's <c>ServerUpdateManager</c>.
    /// </summary>
    internal static class ServerMapStateSyncPatcher
    {
        private static bool _applied;
        private static bool _warnedMissingUpdatePosMethod;

        internal static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            if (!SSMPBridge.IsAvailable) return;

            var serverManagerType = AccessTools.TypeByName("SSMP.Game.Server.ServerManager");
            var serverPlayerDataType = AccessTools.TypeByName("SSMP.Game.Server.ServerPlayerData");
            if (serverManagerType == null || serverPlayerDataType == null)
            {
                Log.Warn("SSMP server types not found — late-join map icon sync patch skipped");
                return;
            }

            var onEnter = AccessTools.Method(serverManagerType, "OnClientEnterScene", new[] { serverPlayerDataType });
            if (onEnter == null)
            {
                Log.Warn("SSMP ServerManager.OnClientEnterScene(ServerPlayerData) not found — map sync skipped");
                return;
            }

            harmony.Patch(
                onEnter,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ServerMapStateSyncPatcher), nameof(OnClientEnterScene_Postfix))));

            _applied = true;
            Log.Info("Hooked SSMP ServerManager.OnClientEnterScene — co-scene map icon replay + peer push");
        }

        /// <summary>
        /// SSMP declares <c>_netServer</c> / <c>_playerData</c> as private on the abstract <c>ServerManager</c>
        /// base class. <see cref="Type.GetField(string,BindingFlags)"/> on the concrete runtime type does not
        /// see inherited private fields, so a simple GetField returned null and late-join replay never ran.
        /// </summary>
        private static object? GetInstanceFieldFromHierarchy(object target, string fieldName)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null)
                    return f.GetValue(target);
            }
            return null;
        }

        private static void OnClientEnterScene_Postfix(object __instance, object playerData)
        {
            if (__instance == null || playerData == null) return;

            try
            {
                var netServer = GetInstanceFieldFromHierarchy(__instance, "_netServer");
                var playerDict = GetInstanceFieldFromHierarchy(__instance, "_playerData");
                if (netServer == null || playerDict == null)
                {
                    Log.Warn(
                        "[MapIcon] ServerMapStateSync: could not read _netServer or _playerData via reflection " +
                        $"(netServer={(netServer != null)}, playerDict={(playerDict != null)}) — late-join replay skipped.");
                    return;
                }

                var pdType = playerData.GetType();
                var enteringId = (ushort)pdType.GetProperty("Id")!.GetValue(playerData)!;
                var enteringScene = (string)pdType.GetProperty("CurrentScene")!.GetValue(playerData)!;

                var getUm = netServer.GetType().GetMethod("GetUpdateManagerForClient", new[] { typeof(ushort) });
                if (getUm == null)
                {
                    Log.Warn("[MapIcon] ServerMapStateSync: GetUpdateManagerForClient not found on NetServer.");
                    return;
                }

                var um = getUm.Invoke(netServer, new object[] { enteringId });
                if (um == null)
                {
                    Log.Warn(
                        $"[MapIcon] ServerMapStateSync: no ServerUpdateManager for entering client {enteringId} " +
                        $"(scene {enteringScene}) — late-join map icon replay skipped.");
                    return;
                }

                var umType = um.GetType();
                var updateIcon = AccessTools.Method(umType, "UpdatePlayerMapIcon", new[] { typeof(ushort), typeof(bool) });
                if (updateIcon == null)
                {
                    Log.Warn("ServerMapStateSync: ServerUpdateManager.UpdatePlayerMapIcon(ushort,bool) not found — late-join map icon sync disabled");
                    return;
                }

                var v2 = AccessTools.TypeByName("SSMP.Math.Vector2");
                var updatePos = v2 != null
                    ? AccessTools.Method(umType, "UpdatePlayerMapPosition", new[] { typeof(ushort), v2 })
                    : null;
                if (updatePos == null && !_warnedMissingUpdatePosMethod)
                {
                    _warnedMissingUpdatePosMethod = true;
                    Log.Warn("[MapIcon] ServerMapStateSync: UpdatePlayerMapPosition not resolved — replaying HasIcon flags only (no stored positions).");
                }

                var valuesObj = playerDict.GetType().GetProperty("Values")?.GetValue(playerDict);
                if (valuesObj is not IEnumerable others)
                {
                    Log.Warn("[MapIcon] ServerMapStateSync: could not enumerate _playerData.Values.");
                    return;
                }

                var enteringHasIcon = (bool)pdType.GetProperty("HasMapIcon")!.GetValue(playerData)!;
                var enteringMapPos = pdType.GetProperty("MapPosition")?.GetValue(playerData);

                var detailToEntering = new List<string>();
                var detailToPeers = new List<string>();
                var replayed = 0;

                foreach (var other in others)
                {
                    if (other == null) continue;

                    var ot = other.GetType();
                    var oid = (ushort)ot.GetProperty("Id")!.GetValue(other)!;
                    if (oid == enteringId) continue;

                    var oscene = (string)ot.GetProperty("CurrentScene")!.GetValue(other)!;
                    if (!string.Equals(oscene, enteringScene, StringComparison.Ordinal)) continue;

                    var hasIcon = (bool)ot.GetProperty("HasMapIcon")!.GetValue(other)!;
                    updateIcon.Invoke(um, new object[] { oid, hasIcon });

                    var sentPosToEntering = false;
                    if (hasIcon && updatePos != null)
                    {
                        var mapPosProp = ot.GetProperty("MapPosition");
                        var mapPos = mapPosProp?.GetValue(other);
                        if (mapPos != null)
                        {
                            updatePos.Invoke(um, new object[] { oid, mapPos });
                            sentPosToEntering = true;
                        }
                    }

                    var peerUm = getUm.Invoke(netServer, new object[] { oid });
                    if (peerUm != null)
                    {
                        updateIcon.Invoke(peerUm, new object[] { enteringId, enteringHasIcon });
                        var sentPosToPeer = false;
                        if (enteringHasIcon && updatePos != null && enteringMapPos != null)
                        {
                            updatePos.Invoke(peerUm, new object[] { enteringId, enteringMapPos });
                            sentPosToPeer = true;
                        }

                        if (CloakPaletteConfig.LogMapIconDiagnostics)
                            detailToPeers.Add(
                                $"peer {oid} ← entering {enteringId}: HasIcon={enteringHasIcon}, pos={(sentPosToPeer ? "sent" : "none")}");
                    }
                    else if (CloakPaletteConfig.LogMapIconDiagnostics)
                    {
                        detailToPeers.Add($"peer {oid}: no ServerUpdateManager (skipped push)");
                    }

                    replayed++;
                    if (CloakPaletteConfig.LogMapIconDiagnostics)
                        detailToEntering.Add($"{oid}:HasIcon={hasIcon},pos={(sentPosToEntering ? "sent" : "none")}");
                }

                if (CloakPaletteConfig.LogMapIconDiagnostics && replayed > 0)
                {
                    var sb = new StringBuilder();
                    sb.Append($"[MapIcon] Server late-join map replay → client {enteringId} scene={enteringScene}: ");
                    sb.Append($"{replayed} co-scene peer(s) [");
                    sb.Append(string.Join("; ", detailToEntering));
                    sb.Append("]; push entering state to peers [");
                    sb.Append(string.Join("; ", detailToPeers));
                    sb.Append("].");
                    Log.Info(sb.ToString());
                }
                else if (CloakPaletteConfig.LogMapIconDiagnostics && replayed == 0)
                {
                    Log.Info(
                        $"[MapIcon] Server late-join map replay → client {enteringId} scene={enteringScene}: " +
                        "no other players in this scene on server (nothing to replay).");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[MapIcon] ServerMapStateSync exception: {ex.Message}");
            }
        }
    }
}
