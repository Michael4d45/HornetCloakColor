using System.Collections.Generic;
using System.Diagnostics;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-player behaviour that keeps cloak tint applied across animation / material swaps.
    /// Walks <b>all</b> <see cref="MeshRenderer"/>s under the player (including children and
    /// inactive objects). Some Hornet animations use separate renderers or layers; only
    /// touching the root missed those frames.
    ///
    /// This component owns the renderers in its own hierarchy. <see cref="CloakSceneScanner"/>
    /// then handles "orphan" Hornet renderers that the engine spawns elsewhere in the scene
    /// (steam-vent recoil pose, item-get pose, etc.).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal class CloakRecolor : MonoBehaviour
    {
        public CloakColor Color { get; private set; } = CloakColor.Default;
        public bool UseCloakShader { get; private set; } = true;

        /// <summary>Original sprite shader per renderer before we swapped in the cloak shader.</summary>
        private readonly Dictionary<MeshRenderer, Shader> _originalShaderByRenderer = new();

        private void LateUpdate()
        {
            CloakMaterialApplier.PruneDestroyed(_originalShaderByRenderer);
            if (PerfDiagnostics.Enabled)
            {
                var sw = Stopwatch.StartNew();
                var n = ApplyToAllMeshRenderersCore();
                sw.Stop();
                PerfDiagnostics.RecordRecolorLateUpdate(gameObject.name, n, sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                ApplyToAllMeshRenderersCore();
            }
        }

        public void Configure(CloakColor color, bool useCloakShader)
        {
            Color          = color;
            UseCloakShader = useCloakShader;
            ApplyToAllMeshRenderers();
        }

        public void SetColor(CloakColor color)
        {
            Color = color;
            ApplyToAllMeshRenderers();
        }

        private void ApplyToAllMeshRenderers() => _ = ApplyToAllMeshRenderersCore();

        /// <returns>Number of <see cref="MeshRenderer"/> instances returned by <c>GetComponentsInChildren</c> (includes null slots).</returns>
        private int ApplyToAllMeshRenderersCore()
        {
            // true = include inactive (some states toggle child meshes).
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return 0;

            foreach (var meshRenderer in renderers)
            {
                if (meshRenderer == null) continue;

                // SSMP's overhead name label is a TextMeshPro under a child named "Username"
                // on the local hero. It is not part of Hornet's sprite atlases — running the
                // cloak shader / tint on it turns the text black. Remote players apply
                // CloakRecolor only to PlayerObject ("Player Prefab"); Username hangs off the
                // container sibling, so it is never touched there.
                if (IsUnderSsmpUsernameObject(meshRenderer.transform))
                    continue;

                // Anything we touch here is by definition a Hornet renderer, so its
                // current atlas is a Hornet atlas. Register it so the scene scanner can
                // recognize the same atlas instance on orphan renderers (e.g. steam-vent
                // recoil pose, item-get pose) where matching by texture name is hopeless
                // (the game uses the generic name "atlas0" for many unrelated atlases).
                var shared = meshRenderer.sharedMaterial;
                if (shared != null)
                {
                    var heroTex = shared.mainTexture;
                    if (HornetTextureRegistry.Register(heroTex))
                        TextureDumper.TryDump(heroTex, "hero");
                }

                CloakMaterialApplier.Apply(
                    meshRenderer,
                    sprite: null,
                    Color,
                    UseCloakShader,
                    _originalShaderByRenderer);
            }

            return renderers.Length;
        }

        /// <summary>Matches <c>SSMP.Game.Client.PlayerManager.UsernameObjectName</c> ("Username").</summary>
        private static bool IsUnderSsmpUsernameObject(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name == "Username") return true;
            }
            return false;
        }

        public static CloakRecolor? AttachOrUpdate(GameObject? playerObject, CloakColor color, bool useCloakShader)
        {
            if (playerObject == null) return null;
            var comp = playerObject.GetComponent<CloakRecolor>();
            if (comp == null) comp = playerObject.AddComponent<CloakRecolor>();
            comp.Configure(color, useCloakShader);
            return comp;
        }
    }
}
