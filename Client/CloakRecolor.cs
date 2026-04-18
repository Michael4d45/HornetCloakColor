using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-player MonoBehaviour that keeps the cloak tint applied across animation /
    /// material swaps. Attached to both the local Hornet (HeroController) and to remote
    /// player GameObjects spawned by SSMP.
    ///
    /// Two paths:
    /// <list type="bullet">
    ///   <item><b>Cloak-only mode</b> (default): swaps the renderer's shader for our
    ///         <c>CloakHueShift</c> shader and pushes the user's color in HSV. Only red
    ///         pixels (the cloak) get recolored; the rest of Hornet stays untouched.</item>
    ///   <item><b>Legacy mode</b>: tints the entire sprite via the tk2dSprite vertex
    ///         color (matches the original behaviour and works without the shader bundle).</item>
    /// </list>
    ///
    /// We poll in <see cref="LateUpdate"/> because tk2dSpriteAnimator may switch the
    /// renderer's material between sprite collections during animation; checking each
    /// frame and re-asserting the shader is the simplest way to stay correct without
    /// hooking into tk2d internals.
    /// </summary>
    [DisallowMultipleComponent]
    internal class CloakRecolor : MonoBehaviour
    {
        public CloakColor Color { get; private set; } = CloakColor.Default;
        public bool CloakOnly { get; private set; } = true;

        // Match parameters; mirrored from CloakColorConfig so both modes can be tuned live.
        public float CenterHue { get; private set; } = 0.98f;
        public float HueWidth  { get; private set; } = 0.50f;
        public float MinSat    { get; private set; } = 0.30f;
        public float Strength  { get; private set; } = 1.0f;

        private MeshRenderer? _renderer;
        private tk2dSprite? _sprite;
        private Shader? _originalShader;
        private bool _shaderApplied;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _sprite   = GetComponent<tk2dSprite>();
        }

        public void Configure(CloakColor color, bool cloakOnly,
                              float centerHue, float hueWidth, float minSat, float strength)
        {
            Color      = color;
            CloakOnly  = cloakOnly;
            CenterHue  = centerHue;
            HueWidth   = hueWidth;
            MinSat     = minSat;
            Strength   = strength;
            ApplyImmediate();
        }

        public void SetColor(CloakColor color)
        {
            Color = color;
            ApplyImmediate();
        }

        public void SetCloakOnly(bool cloakOnly)
        {
            CloakOnly = cloakOnly;
            ApplyImmediate();
        }

        private void LateUpdate() => ApplyImmediate();

        private void ApplyImmediate()
        {
            if (_renderer == null) return;

            // Touching `.material` clones the shared sprite-collection material so we don't
            // pollute other renderers using the same tk2d collection.
            var mat = _renderer.material;
            if (mat == null) return;

            if (CloakOnly && CloakShaderManager.Shader != null)
            {
                EnsureCloakShader(mat);
                ApplyShaderProperties(mat);
            }
            else
            {
                RestoreOriginalShader(mat);
                ApplyVertexTint(mat);
            }
        }

        private void EnsureCloakShader(Material mat)
        {
            var cloakShader = CloakShaderManager.Shader!;
            if (mat.shader == cloakShader)
            {
                _shaderApplied = true;
                return;
            }

            // Remember the first non-cloak shader we saw so the user can toggle back at runtime.
            if (!_shaderApplied)
            {
                _originalShader = mat.shader;
            }

            // Preserve the active sprite texture across the shader swap. tk2d updates
            // mainTexture via SetSprite() each frame, so we don't strictly need to copy
            // it here, but doing so avoids a one-frame flash on hot-swap.
            var tex = mat.mainTexture;
            mat.shader = cloakShader;
            if (tex != null) mat.mainTexture = tex;
            _shaderApplied = true;
        }

        private void RestoreOriginalShader(Material mat)
        {
            if (!_shaderApplied) return;
            if (_originalShader == null) return;
            if (mat.shader == _originalShader) return;

            var tex = mat.mainTexture;
            mat.shader = _originalShader;
            if (tex != null) mat.mainTexture = tex;
            _shaderApplied = false;
        }

        private void ApplyShaderProperties(Material mat)
        {
            // Keep vertex tint at white so the shader-controlled cloak color isn't multiplied
            // by an old vertex tint left over from a previous color choice.
            if (_sprite != null && _sprite.color != UnityEngine.Color.white)
            {
                _sprite.color = UnityEngine.Color.white;
            }

            Color.ToHSV(out var h, out var s, out var v);

            mat.SetFloat(CloakShaderManager.TargetHueId, h);
            // We send saturation and value as multipliers of 1.0 (default) but bake the
            // chosen color's intensity into them so picks like Obsidian (low value) come
            // through. 1.0 corresponds to "use the source pixel's saturation/value".
            mat.SetFloat(CloakShaderManager.TargetSatId, s <= 0.001f ? 0f : 1.0f);
            mat.SetFloat(CloakShaderManager.TargetValId, Mathf.Lerp(0.6f, 1.4f, v));

            mat.SetFloat(CloakShaderManager.CenterHueId, CenterHue);
            mat.SetFloat(CloakShaderManager.HueWidthId,  HueWidth);
            mat.SetFloat(CloakShaderManager.MinSatId,    MinSat);
            mat.SetFloat(CloakShaderManager.StrengthId,  Strength);
        }

        private void ApplyVertexTint(Material mat)
        {
            var unityColor = Color.ToUnityColor();
            if (_sprite != null) _sprite.color = unityColor;
            mat.color = unityColor;
        }

        /// <summary>
        /// Convenience: get-or-add the component on a player object and configure it in one shot.
        /// Returns null if the GameObject is null.
        /// </summary>
        public static CloakRecolor? AttachOrUpdate(GameObject? playerObject, CloakColor color, bool cloakOnly,
                                                   float centerHue, float hueWidth, float minSat, float strength)
        {
            if (playerObject == null) return null;
            var comp = playerObject.GetComponent<CloakRecolor>();
            if (comp == null) comp = playerObject.AddComponent<CloakRecolor>();
            comp.Configure(color, cloakOnly, centerHue, hueWidth, minSat, strength);
            return comp;
        }
    }
}
