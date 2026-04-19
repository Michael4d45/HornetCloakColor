using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Per-player MonoBehaviour that keeps the cloak tint applied across animation /
    /// material swaps. Uses the <c>CloakHueShift</c> shader when the bundle is present
    /// (matches texture pixels to fixed front/under reference colors from
    /// <c>cloak_palette.json</c>); otherwise tints the whole sprite.
    /// </summary>
    [DisallowMultipleComponent]
    internal class CloakRecolor : MonoBehaviour
    {
        public CloakColor Color { get; private set; } = CloakColor.Default;
        public bool UseCloakShader { get; private set; } = true;

        private MeshRenderer? _renderer;
        private tk2dSprite? _sprite;
        private Shader? _originalShader;
        private bool _shaderApplied;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _sprite   = GetComponent<tk2dSprite>();
        }

        public void Configure(CloakColor color, bool useCloakShader)
        {
            Color          = color;
            UseCloakShader = useCloakShader;
            ApplyImmediate();
        }

        public void SetColor(CloakColor color)
        {
            Color = color;
            ApplyImmediate();
        }

        private void LateUpdate() => ApplyImmediate();

        private void ApplyImmediate()
        {
            if (_renderer == null) return;

            var mat = _renderer.material;
            if (mat == null) return;

            if (UseCloakShader && CloakShaderManager.Shader != null)
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

            if (!_shaderApplied)
            {
                _originalShader = mat.shader;
            }

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
            if (_sprite != null && _sprite.color != UnityEngine.Color.white)
            {
                _sprite.color = UnityEngine.Color.white;
            }

            mat.SetColor(CloakShaderManager.SrcFrontId, CloakPaletteConfig.FrontUnity);
            mat.SetColor(CloakShaderManager.SrcUnderId, CloakPaletteConfig.UnderUnity);
            mat.SetFloat(CloakShaderManager.MatchRadiusId, CloakPaletteConfig.MatchRadius);

            // Preset "White" / #FFFFFF = no tint. Feeding white through RGB→HSV gives S=0 and the
            // fragment shader would output grey/off-white cloak pixels — so disable recolor entirely.
            if (Color.Equals(CloakColor.Default))
            {
                mat.SetFloat(CloakShaderManager.StrengthId, 0f);
                return;
            }

            Color.ToHSV(out var h, out var s, out var v);

            mat.SetFloat(CloakShaderManager.TargetHueId, h);
            mat.SetFloat(CloakShaderManager.TargetSatId, s <= 0.001f ? 0f : 1.0f);
            mat.SetFloat(CloakShaderManager.TargetValId, Mathf.Lerp(0.6f, 1.4f, v));
            mat.SetFloat(CloakShaderManager.StrengthId, 1f);
        }

        private void ApplyVertexTint(Material mat)
        {
            var unityColor = Color.ToUnityColor();
            if (_sprite != null) _sprite.color = unityColor;
            mat.color = unityColor;
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
