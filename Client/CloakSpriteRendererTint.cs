using System;
using System.Collections.Generic;
using HarmonyLib;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Narrow SpriteRenderer support for standalone Texture2D animation frames that have masks
    /// under <c>CloakMasks/Texture2D/&lt;texture-or-sprite&gt;.png</c>.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    internal sealed class CloakSpriteRendererTint : MonoBehaviour
    {
        private static readonly HashSet<CloakSpriteRendererTint> Instances = new();
        private static CloakColor _color = CloakColor.Default;

        private SpriteRenderer? _renderer;
        private Shader? _originalShader;
        private int _lastSpriteId;
        private int _lastMaterialId;
        private CloakColor _lastColor;
        private float _lastTextureSatBoost = float.NaN;
        private bool _applied;

        internal static void SetColor(CloakColor color)
        {
            _color = color;
            foreach (var inst in Instances)
            {
                if (inst != null) inst.ForceApply();
            }
        }

        /// <summary>
        /// Attach a lightweight watcher even if the current sprite has no mask. This is needed
        /// for SpriteRenderer animations whose first masked frame appears after scene load (Unity
        /// animations can update the native sprite without going through the C# property setter).
        /// No-mask states are memoized by sprite/material id, so watched static SpriteRenderers
        /// cost only a couple of integer comparisons per frame.
        /// </summary>
        internal static void Watch(SpriteRenderer renderer)
        {
            if (renderer == null) return;
            if (!IsLikelyMaskCandidate(renderer)) return;
            var comp = renderer.GetComponent<CloakSpriteRendererTint>();
            if (comp == null) comp = renderer.gameObject.AddComponent<CloakSpriteRendererTint>();
            comp.ForceApply();
        }

        /// <summary>
        /// Avoid attaching watchers to every inactive SpriteRenderer in the scene. A renderer is
        /// worth watching if the current frame already has a mask, or if its object/sprite/texture
        /// name points at one of the known standalone Texture2D-mask animation families.
        /// </summary>
        private static bool IsLikelyMaskCandidate(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            if (sprite == null) return false;

            if (CloakMaskManager.TryGetTexture2DMask(sprite.texture, sprite.name, out _))
                return true;

            return ContainsKnownTexture2DMaskStem(renderer.name)
                   || ContainsKnownTexture2DMaskStem(renderer.gameObject.name)
                   || ContainsKnownTexture2DMaskStem(sprite.name)
                   || (sprite.texture != null && ContainsKnownTexture2DMaskStem(sprite.texture.name));
        }

        /// <summary>
        /// True when <paramref name="value"/> contains any animation-family prefix derived from the
        /// mask PNGs shipped under <c>CloakMasks/Texture2D/</c> (frame digits stripped), so newly
        /// authored families (e.g. <c>witch_down_slash_follow_up</c>) are watched without code changes.
        /// </summary>
        private static bool ContainsKnownTexture2DMaskStem(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            foreach (var prefix in CloakMaskManager.GetTexture2DMaskFamilyPrefixes())
            {
                if (value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            Instances.Add(this);
        }

        private void OnEnable()
        {
            Instances.Add(this);
            ForceApply();
        }

        private void OnBecameVisible()
        {
            ForceApply();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            Restore();
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        private void LateUpdate()
        {
            ApplyIfNeeded(force: false);
        }

        private void ForceApply()
        {
            _lastSpriteId = 0;
            _lastMaterialId = 0;
            ApplyIfNeeded(force: true);
        }

        private void ApplyIfNeeded(bool force)
        {
            _renderer ??= GetComponent<SpriteRenderer>();
            var renderer = _renderer;
            if (renderer == null || renderer.sprite == null) return;

            var sprite = renderer.sprite;
            var shared = renderer.sharedMaterial;
            if (shared == null) return;
            if (!shared.HasProperty(CloakShaderManager.MainTexId)) return;

            var spriteId = sprite.GetInstanceID();
            var materialId = shared.GetInstanceID();
            var satBoost = CloakMaterialApplier.GetTextureSaturationBoost();
            if (!force
                && _lastSpriteId == spriteId
                && _lastMaterialId == materialId
                && _lastColor.Equals(_color)
                && Mathf.Approximately(_lastTextureSatBoost, satBoost))
                return;

            if (!CloakMaskManager.TryGetTexture2DMask(sprite.texture, sprite.name, out var mask))
            {
                Restore();
                _lastSpriteId = spriteId;
                _lastMaterialId = materialId;
                _lastColor = _color;
                return;
            }

            var shader = CloakShaderManager.Shader;
            if (shader == null) return;

            var mat = renderer.material;
            if (mat == null) return;

            if (_originalShader == null && mat.shader != shader)
                _originalShader = mat.shader;

            var tex = sprite.texture;
            mat.shader = shader;
            if (tex != null) mat.mainTexture = tex;

            CloakMaterialApplier.ApplyCloakShaderProperties(mat, _color, mask, satBoost);

            var finalShared = renderer.sharedMaterial;
            _lastSpriteId = spriteId;
            _lastMaterialId = finalShared != null ? finalShared.GetInstanceID() : materialId;
            _lastColor = _color;
            _lastTextureSatBoost = satBoost;
            _applied = true;
        }

        private void Restore()
        {
            if (!_applied || _renderer == null || _originalShader == null) return;
            var mat = _renderer.material;
            if (mat == null) return;
            var tex = _renderer.sprite != null ? _renderer.sprite.texture : mat.mainTexture;
            mat.shader = _originalShader;
            if (tex != null) mat.mainTexture = tex;
            _applied = false;
        }
    }

    /// <summary>
    /// Hooks SpriteRenderer sprite changes so masked standalone Texture2D frames are tinted as
    /// soon as animation swaps to them.
    /// </summary>
    internal static class CloakSpriteRendererTintPatcher
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied) return;
            _applied = true;

            var setter = AccessTools.PropertySetter(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite));
            if (setter == null)
            {
                Log.Warn("CloakSpriteRendererTintPatcher: SpriteRenderer.sprite setter not found; Texture2D SpriteRenderer tint disabled.");
                return;
            }

            harmony.Patch(
                setter,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CloakSpriteRendererTintPatcher), nameof(SpriteRenderer_SetSprite_Postfix))));
            Log.Info("Hooked SpriteRenderer.sprite setter for Texture2D mask tint.");
        }

        private static void SpriteRenderer_SetSprite_Postfix(SpriteRenderer __instance)
        {
            HarmonySafe.Run(
                () => CloakSpriteRendererTint.Watch(__instance),
                $"CloakSpriteRendererTintPatcher: postfix threw on '{__instance?.name ?? "(null)"}'");
        }
    }
}
