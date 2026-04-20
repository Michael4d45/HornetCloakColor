using System;
using System.Reflection;
using HarmonyLib;
using HornetCloakColor.Shared;
using HornetCloakColor;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Hooks the three places the Hornet "mask" map markers are positioned/instantiated:
    /// <list type="bullet">
    ///   <item><see cref="GameMap.PositionCompassAndCorpse"/> — local player icon on the
    ///         zoomed-in (per-area) map.</item>
    ///   <item><c>InventoryWideMap.UpdatePositions</c> — local player icon on the wide
    ///         "overall" map (different GameObject from the zoomed-in one).</item>
    ///   <item><c>SSMP.Game.Client.MapManager.CreatePlayerIcon</c> — remote players' icons
    ///         when SSMP multiplayer is active.</item>
    /// </list>
    /// </summary>
    internal static class MapMaskHarmonyPatcher
    {
        private const string HarmonyId = "hornet.cloak.color.mapmask";
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;

            var harmony = new Harmony(HarmonyId);

            var gameMapPosition = AccessTools.Method(typeof(GameMap), nameof(GameMap.PositionCompassAndCorpse));
            if (gameMapPosition != null)
            {
                harmony.Patch(
                    gameMapPosition,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(MapMaskHarmonyPatcher), nameof(GameMap_PositionCompassAndCorpse_Postfix))));
            }

            // Wide / overall map uses a separate compass icon Transform on InventoryWideMap.
            // Two hooks because UpdatePositions might not be called on every state change:
            //   - UpdatePositions: re-hook on each wide-map open
            //   - PositionIcon: per-icon (compass + corpse), called whenever an icon is placed
            var wideMapType = AccessTools.TypeByName("InventoryWideMap");
            if (wideMapType != null)
            {
                var update = AccessTools.Method(wideMapType, "UpdatePositions");
                if (update != null)
                {
                    harmony.Patch(
                        update,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(MapMaskHarmonyPatcher), nameof(InventoryWideMap_UpdatePositions_Postfix))));
                    Log.Info("Hooked InventoryWideMap.UpdatePositions");
                }
                else
                {
                    Log.Warn("InventoryWideMap.UpdatePositions not found");
                }

                var positionIcon = AccessTools.Method(wideMapType, "PositionIcon");
                if (positionIcon != null)
                {
                    harmony.Patch(
                        positionIcon,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(MapMaskHarmonyPatcher), nameof(InventoryWideMap_PositionIcon_Postfix))));
                    Log.Info("Hooked InventoryWideMap.PositionIcon");
                }
            }
            else
            {
                Log.Warn("InventoryWideMap type not found — wide-map tint disabled");
            }

            var mapManagerType = AccessTools.TypeByName("SSMP.Game.Client.MapManager");
            var vec2Type = AccessTools.TypeByName("SSMP.Math.Vector2");
            if (mapManagerType != null && vec2Type != null)
            {
                var createIcon = AccessTools.Method(mapManagerType, "CreatePlayerIcon", new[] { typeof(ushort), vec2Type });
                if (createIcon != null)
                {
                    harmony.Patch(
                        createIcon,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(MapMaskHarmonyPatcher), nameof(MapManager_CreatePlayerIcon_Postfix))));
                    Log.Info("Hooked SSMP MapManager.CreatePlayerIcon");
                }
                else
                {
                    Log.Warn("SSMP MapManager type found but CreatePlayerIcon(ushort, Vector2) not — remote map tint disabled");
                }
            }
            else if (SSMPBridge.IsAvailable)
            {
                Log.Warn(
                    "SSMP plugin loaded but MapManager / Math.Vector2 types not found via reflection — " +
                    "remote map tint disabled. SSMP may have changed namespaces.");
            }

            // Host-only: replays remote players' map-icon state to clients entering a scene (SSMP gap).
            ServerMapStateSyncPatcher.TryApply(harmony);

            // Client: wide inventory map never toggles SSMP MapManager._displayingIcons; fix remote clones inactive.
            RemoteMapIconVisibility.TryApplyClientManagerHook(harmony);

            // Stock SSMP inverted the compass check — with FreeCompass / compass equipped, map icons never broadcast.
            SsmMapCompassBroadcastFixPatcher.TryApply(harmony);

            // Joiners can receive HasMapIcon before GameMap exists; CreatePlayerIcon no-ops but HasMapIcon stays true with no GameObject.
            MapRemoteIconDeferredCreate.TryApply(harmony);
        }

        private static void GameMap_PositionCompassAndCorpse_Postfix(GameMap __instance)
        {
            var plugin = HornetCloakColorPlugin.Instance;
            if (plugin == null) return;

            LocalMapMaskTint.Refresh(__instance, plugin.ColorConfig.CurrentColor);
            // Idempotent with SSMP's MapManager.OnPositionCompass; keeps remote clones in sync.
            RemoteMapIconVisibility.SyncRemoteMapIconsVisible();
        }

        private static void InventoryWideMap_UpdatePositions_Postfix(object __instance)
        {
            var plugin = HornetCloakColorPlugin.Instance;
            if (plugin == null) return;

            try
            {
                var iconTransform = ResolveWideMapCompassIcon(__instance);
                if (iconTransform == null)
                {
                    if (CloakPaletteConfig.DebugLogging)
                        Log.Warn("InventoryWideMap: could not resolve compass icon transform in UpdatePositions postfix");
                    return;
                }

                if (CloakPaletteConfig.DebugLogging)
                    Log.Info($"InventoryWideMap.UpdatePositions tint -> {iconTransform.name}");

                LocalMapMaskTint.RefreshObject(iconTransform.gameObject, plugin.ColorConfig.CurrentColor);

                // SSMP never sets MapManager._displayingIcons from the wide map — only from GameMap.
                RemoteMapIconVisibility.SyncRemoteMapIconsVisible();
            }
            catch (Exception ex)
            {
                Log.Warn($"MapMaskHarmonyPatcher: wide-map tint failed: {ex.Message}");
            }
        }

        // Resolve the wide-map compass icon. The serialized `compassIcon` Transform field
        // is sometimes null at the time UpdatePositions runs (depending on prefab wiring),
        // so we fall back to a child lookup. Both the prefab and the runtime hierarchy use
        // the literal name "Compass Icon" (see logs).
        private static Transform? ResolveWideMapCompassIcon(object instance)
        {
            var compassField = instance.GetType().GetField("compassIcon", BindingFlags.Public | BindingFlags.Instance);
            if (compassField?.GetValue(instance) is Transform fieldTransform && fieldTransform != null)
                return fieldTransform;

            if (instance is MonoBehaviour mb && mb != null)
            {
                var found = mb.transform.Find("Compass Icon");
                if (found != null) return found;
            }

            return null;
        }

        // PositionIcon(Transform icon, Vector2 mapBoundsPos, bool isActive, MapZone)
        // Called for both the compass and the corpse icon. We tint only when this is the
        // compass icon (matched against the InventoryWideMap.compassIcon field if set,
        // else against the literal name).
        private static void InventoryWideMap_PositionIcon_Postfix(object __instance, Transform icon, bool isActive)
        {
            var plugin = HornetCloakColorPlugin.Instance;
            if (plugin == null) return;
            if (icon == null) return;

            try
            {
                var compassRef = ResolveWideMapCompassIcon(__instance);
                var isCompass = compassRef != null
                    ? ReferenceEquals(icon, compassRef)
                    : icon.name == "Compass Icon";
                if (!isCompass) return;

                if (CloakPaletteConfig.DebugLogging)
                    Log.Info($"InventoryWideMap.PositionIcon compass tint (isActive={isActive}) -> {icon.name}");

                LocalMapMaskTint.RefreshObject(icon.gameObject, plugin.ColorConfig.CurrentColor);

                if (isActive)
                    RemoteMapIconVisibility.SyncRemoteMapIconsVisible();
            }
            catch (Exception ex)
            {
                Log.Warn($"MapMaskHarmonyPatcher: PositionIcon tint failed: {ex.Message}");
            }
        }

        private static void MapManager_CreatePlayerIcon_Postfix(ushort id, object __instance)
        {
            var go = TryGetMapIconGameObject(__instance, id);
            if (go == null)
            {
                if (CloakPaletteConfig.LogMapIconDiagnostics)
                    Log.Warn($"[MapIcon] CreatePlayerIcon postfix: no GameObject on map entry for player {id} (create failed or entry missing).");
                return;
            }

            var tint = go.GetComponent<MapMaskTint>();
            if (tint == null) tint = go.AddComponent<MapMaskTint>();

            var color = SSMPBridge.GetRemoteMapColorOrDefault(id);
            if (CloakPaletteConfig.LogMapIconDiagnostics)
                Log.Info($"[MapIcon] CreatePlayerIcon → MapMaskTint on '{go.name}' player {id} (active={go.activeInHierarchy}) color={color}");
            tint.InitRemote(id, color);
        }

        private static GameObject? TryGetMapIconGameObject(object mapManager, ushort id)
        {
            try
            {
                var mapType = mapManager.GetType();
                var entriesField = mapType.GetField("_mapEntries", BindingFlags.Instance | BindingFlags.NonPublic);
                var dict = entriesField?.GetValue(mapManager);
                if (dict == null) return null;

                var tryGet = dict.GetType().GetMethod("TryGetValue");
                if (tryGet == null) return null;

                var args = new object?[] { id, null };
                var invokeResult = tryGet.Invoke(dict, args);
                if (invokeResult is not bool ok || !ok || args[1] == null) return null;

                var entry = args[1]!;
                var goProp = entry.GetType().GetProperty("GameObject", BindingFlags.Public | BindingFlags.Instance);
                return goProp?.GetValue(entry) as GameObject;
            }
            catch (Exception ex)
            {
                Log.Warn($"MapMaskHarmonyPatcher: could not read map icon for player {id}: {ex.Message}");
                return null;
            }
        }
    }
}
