using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP <c>MapManager.CreatePlayerIcon</c> returns immediately when <c>GetGameMap()</c> is null.
    /// <c>UpdatePlayerHasIcon</c> still sets <c>HasMapIcon</c> true, so the entry can sit with no
    /// <c>GameObject</c> if the network packet arrived before the local <c>GameMap</c> existed
    /// (common for joiners). <c>UpdatePlayerIcon</c> only retries when a position packet arrives;
    /// if none follow, the host's pin never appears. Re-run <c>CreatePlayerIcon</c> whenever
    /// <c>GameMap</c> becomes available and visibility is refreshed.
    /// </summary>
    internal static class MapRemoteIconDeferredCreate
    {
        private static bool _applied;
        private static float _nextLogPendingNoGameMap;

        internal static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            if (!SSMPBridge.IsAvailable) return;

            var mapManagerType = AccessTools.TypeByName("SSMP.Game.Client.MapManager");
            if (mapManagerType == null) return;

            var updateHasIcon = AccessTools.Method(mapManagerType, "UpdatePlayerHasIcon", new[] { typeof(ushort), typeof(bool) });
            if (updateHasIcon == null) return;

            harmony.Patch(
                updateHasIcon,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(MapRemoteIconDeferredCreate), nameof(UpdatePlayerHasIcon_Postfix))));

            var clientManagerType = AccessTools.TypeByName("SSMP.Game.Client.ClientManager");
            var alreadyInSceneType = AccessTools.TypeByName("SSMP.Networking.Packet.Data.ClientPlayerAlreadyInScene");
            if (clientManagerType != null && alreadyInSceneType != null)
            {
                var onAlready = AccessTools.Method(clientManagerType, "OnPlayerAlreadyInScene", new[] { alreadyInSceneType });
                if (onAlready != null)
                {
                    harmony.Patch(
                        onAlready,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(MapRemoteIconDeferredCreate), nameof(ClientManager_OnPlayerAlreadyInScene_Postfix))));
                    Log.Info("Hooked SSMP ClientManager.OnPlayerAlreadyInScene — retry remote map icons after late-join roster");
                }
            }

            _applied = true;
            Log.Info("Hooked SSMP MapManager.UpdatePlayerHasIcon — deferred remote map icon materialize");
        }

        /// <summary>
        /// Map icon packets can arrive before <c>GameMap</c> exists; after SSMP finishes applying
        /// <c>AlreadyInScene</c>, retry pending creates.
        /// </summary>
        private static void ClientManager_OnPlayerAlreadyInScene_Postfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Info("[MapIcon] OnPlayerAlreadyInScene finished — running deferred remote icon materialize pass.");

                var mm = __instance.GetType()
                    .GetField("_mapManager", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(__instance);
                TryMaterializePendingIcons(mm);
            }
            catch (Exception ex)
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Warn($"[MapIcon] OnPlayerAlreadyInScene materialize hook: {ex.Message}");
            }
        }

        /// <summary>
        /// After any icon flag change, retry creating objects that missed because <c>GameMap</c> was not ready.
        /// </summary>
        private static void UpdatePlayerHasIcon_Postfix(object __instance, ushort id, bool hasMapIcon)
        {
            if (__instance == null) return;
            if (CloakPaletteConfig.LogMapIconDiagnostics)
                Log.Info($"[MapIcon] UpdatePlayerHasIcon(player {id}, hasIcon={hasMapIcon}) — deferred materialize pass.");
            TryMaterializePendingIcons(__instance);
        }

        internal static void TryMaterializePendingIcons(object? mapManager)
        {
            if (mapManager == null)
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Warn("[MapIcon] TryMaterializePendingIcons: MapManager instance null.");
                return;
            }

            try
            {
                var mmType = mapManager.GetType();
                var getGameMap = mmType.GetMethod("GetGameMap", BindingFlags.Instance | BindingFlags.NonPublic);
                var gameMap = getGameMap?.Invoke(mapManager, null);

                var createIcon = mmType.GetMethod("CreatePlayerIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                if (createIcon == null)
                {
                    Log.Warn("[MapIcon] TryMaterializePendingIcons: CreatePlayerIcon not found on MapManager.");
                    return;
                }

                var dict = mmType.GetField("_mapEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mapManager);
                if (dict is not IEnumerable pairs)
                {
                    if (CloakPaletteConfig.LogMapIconDiagnostics)
                        Log.Warn("[MapIcon] TryMaterializePendingIcons: _mapEntries missing or not enumerable.");
                    return;
                }

                var toCreate = new List<(ushort playerId, object pos)>();
                foreach (var kvObj in pairs)
                {
                    if (kvObj == null) continue;
                    var kvType = kvObj.GetType();
                    var keyProp = kvType.GetProperty("Key");
                    var valProp = kvType.GetProperty("Value");
                    if (keyProp == null || valProp == null) continue;

                    var playerIdObj = keyProp.GetValue(kvObj);
                    var entry = valProp.GetValue(kvObj);
                    if (playerIdObj is not ushort playerId || entry == null)
                        continue;

                    var et = entry.GetType();
                    var hasEntryIcon = (bool)et.GetProperty("HasMapIcon")!.GetValue(entry)!;
                    var go = et.GetProperty("GameObject")!.GetValue(entry);
                    if (!hasEntryIcon || go != null)
                        continue;

                    var pos = et.GetProperty("Position")!.GetValue(entry);
                    if (pos == null) continue;

                    toCreate.Add((playerId, pos));
                }

                if (toCreate.Count == 0)
                    return;

                if (gameMap == null)
                {
                    if (CloakPaletteConfig.LogMapIconDiagnostics
                        && Time.realtimeSinceStartup >= _nextLogPendingNoGameMap)
                    {
                        _nextLogPendingNoGameMap = Time.realtimeSinceStartup + 2f;
                        var ids = string.Join(",", toCreate.ConvertAll(x => x.playerId.ToString()));
                        Log.Warn(
                            $"[MapIcon] {toCreate.Count} remote map entr(y/ies) need GameObjects (players {ids}) " +
                            "but GetGameMap() is null — will retry when GameMap exists.");
                    }
                    return;
                }

                var created = 0;
                foreach (var (playerId, pos) in toCreate)
                {
                    createIcon.Invoke(mapManager, new[] { playerId, pos });
                    created++;
                }

                if (created > 0 && CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Info($"[MapIcon] Deferred CreatePlayerIcon succeeded for {created} remote player(s) (GameMap was ready).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MapIcon] TryMaterializePendingIcons: {ex.Message}");
            }
        }
    }
}
