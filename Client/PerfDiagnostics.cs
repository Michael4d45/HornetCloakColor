using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Aggregated performance logging (gated by <see cref="CloakPaletteConfig.PerfDiagnostics"/>).
    /// Flushes on a wall-clock window so we do not log every frame.
    /// </summary>
    internal static class PerfDiagnostics
    {
        private const float FlushIntervalSec = 2f;

        private static float _windowStart = -1f;

        private static int _recolorLateUpdates;
        private static double _recolorMsTotal;
        private static double _recolorMsMax;
        private static string _recolorMaxOwner = "";
        private static int _recolorMeshMax;
        private static long _recolorMeshSum;

        private static int _scannerRuns;
        private static long _scannerSpritesTotal;
        private static int _scannerSpritesMax;
        private static long _scannerAppliedTotal;
        private static double _scannerFindMsTotal;
        private static double _scannerLoopMsTotal;

        private static int _mapSyncCalls;
        private static double _mapSyncMsTotal;
        private static double _mapSyncMsMax;

        public static bool Enabled => CloakPaletteConfig.PerfDiagnostics;

        /// <summary>Call from <see cref="CloakRecolor"/> after counting mesh renderers.</summary>
        public static void RecordRecolorLateUpdate(string ownerName, int meshRendererCount, double elapsedMs)
        {
            if (!Enabled) return;

            _recolorLateUpdates++;
            _recolorMsTotal += elapsedMs;
            if (elapsedMs > _recolorMsMax)
            {
                _recolorMsMax = elapsedMs;
                _recolorMaxOwner = ownerName;
            }

            _recolorMeshSum += meshRendererCount;
            if (meshRendererCount > _recolorMeshMax)
                _recolorMeshMax = meshRendererCount;

            MaybeFlushWindow();
        }

        /// <summary>Call from <see cref="CloakSceneScanner"/> once per scan pass.</summary>
        public static void RecordSceneScan(int tk2dSpriteCount, int appliedCount, double findObjectsMs, double loopMs)
        {
            if (!Enabled) return;

            _scannerRuns++;
            _scannerSpritesTotal += tk2dSpriteCount;
            if (tk2dSpriteCount > _scannerSpritesMax)
                _scannerSpritesMax = tk2dSpriteCount;
            _scannerAppliedTotal += appliedCount;
            _scannerFindMsTotal += findObjectsMs;
            _scannerLoopMsTotal += loopMs;

            MaybeFlushWindow();
        }

        /// <summary>Call from <see cref="RemoteMapIconVisibility.SyncRemoteMapIconsVisible"/>.</summary>
        public static void RecordMapSyncVisible(double elapsedMs)
        {
            if (!Enabled) return;

            _mapSyncCalls++;
            _mapSyncMsTotal += elapsedMs;
            if (elapsedMs > _mapSyncMsMax)
                _mapSyncMsMax = elapsedMs;

            MaybeFlushWindow();
        }

        private static void MaybeFlushWindow()
        {
            var now = Time.realtimeSinceStartup;
            if (_windowStart < 0f)
                _windowStart = now;

            if (now - _windowStart < FlushIntervalSec)
                return;

            Emit();

            _recolorLateUpdates = 0;
            _recolorMsTotal = 0;
            _recolorMsMax = 0;
            _recolorMaxOwner = "";
            _recolorMeshMax = 0;
            _recolorMeshSum = 0;

            _scannerRuns = 0;
            _scannerSpritesTotal = 0;
            _scannerSpritesMax = 0;
            _scannerAppliedTotal = 0;
            _scannerFindMsTotal = 0;
            _scannerLoopMsTotal = 0;

            _mapSyncCalls = 0;
            _mapSyncMsTotal = 0;
            _mapSyncMsMax = 0;

            _windowStart = now;
        }

        private static void Emit()
        {
            if (_recolorLateUpdates > 0)
            {
                var avgMs = _recolorMsTotal / _recolorLateUpdates;
                var avgMesh = (double)_recolorMeshSum / _recolorLateUpdates;
                Log.Info(
                    $"[HCC/Perf] CloakRecolor: {_recolorLateUpdates} LateUpdate(s) in ~{FlushIntervalSec:F0}s " +
                    $"(≈{_recolorLateUpdates / FlushIntervalSec:F0}/s) — total {_recolorMsTotal:F1}ms, " +
                    $"avg {avgMs:F3}ms/update, max {_recolorMsMax:F3}ms on '{_recolorMaxOwner}', " +
                    $"MeshRenderer avg {avgMesh:F1}, max {_recolorMeshMax} " +
                    $"(multiple players ⇒ multiple components ⇒ cost scales up).");
            }

            if (_scannerRuns > 0)
            {
                var avgSprites = (double)_scannerSpritesTotal / _scannerRuns;
                var avgApplied = (double)_scannerAppliedTotal / _scannerRuns;
                var avgFind = _scannerFindMsTotal / _scannerRuns;
                var avgLoop = _scannerLoopMsTotal / _scannerRuns;
                Log.Info(
                    $"[HCC/Perf] CloakSceneScanner: {_scannerRuns} scan(s) — tk2dSprite count avg {avgSprites:F0}, max in one pass {_scannerSpritesMax}, " +
                    $"Apply() calls avg {avgApplied:F1}, FindObjects {avgFind:F2}ms/scan, loop+Apply {avgLoop:F2}ms/scan " +
                    $"(MP adds sprites ⇒ larger FindObjects + longer loop).");
            }

            if (_mapSyncCalls > 0)
            {
                var avgMs = _mapSyncMsTotal / _mapSyncCalls;
                Log.Info(
                    $"[HCC/Perf] SyncRemoteMapIconsVisible: {_mapSyncCalls} call(s) in ~{FlushIntervalSec:F0}s " +
                    $"(≈{_mapSyncCalls / FlushIntervalSec:F0}/s) — total {_mapSyncMsTotal:F2}ms, avg {avgMs:F3}ms, max {_mapSyncMsMax:F3}ms " +
                    $"(reflection into SSMP MapManager; spikes if GameMap updates compass often).");
            }
        }
    }
}
