using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Loads cloak shaders from the AssetBundle embedded in the mod DLL
    /// (<c>HornetCloakColor.Resources.cloakshader.bundle</c>).
    /// </summary>
    internal static class CloakShaderManager
    {
        private const string ShaderName = "HornetCloakColor/CloakHueShift";
        private const string ShaderAssetName = "CloakHueShift";
        private const string MaskBakeShaderName = "HornetCloakColor/CloakMaskBake";
        private const string MaskBakeAssetName = "CloakMaskBake";

        private const string ResourceName = "HornetCloakColor.Resources.cloakshader.bundle";

        private static AssetBundle? _bundle;
        private static Shader? _shader;
        private static Shader? _maskBakeShader;

        public const int MaxCloakColors = 16;
        public const int MaxAvoidColors = 16;

        public static readonly int TargetHueId = Shader.PropertyToID("_TargetHue");
        public static readonly int TargetSatId = Shader.PropertyToID("_TargetSat");
        public static readonly int TargetValId = Shader.PropertyToID("_TargetVal");
        public static readonly int SrcColorsId = Shader.PropertyToID("_SrcColors");
        public static readonly int AvoidColorsId = Shader.PropertyToID("_AvoidColors");
        public static readonly int MatchRadiusId = Shader.PropertyToID("_MatchRadius");
        public static readonly int AvoidMatchRadiusId = Shader.PropertyToID("_AvoidMatchRadius");
        public static readonly int StrengthId = Shader.PropertyToID("_Strength");
        public static readonly int CloakMaskTexId = Shader.PropertyToID("_CloakMaskTex");

        /// <summary>The loaded cloak-tint shader, or null if the bundle is unavailable.</summary>
        public static Shader? Shader
        {
            get
            {
                EnsureInitialized();
                return _shader;
            }
        }

        /// <summary>
        /// GPU-only mask bake shader (same mask math as <see cref="Shader"/>). Null if the embedded bundle
        /// predates <c>CloakMaskBake.shader</c> — rebuild the bundle from the Unity editor menu.
        /// </summary>
        public static Shader? MaskBakeShader
        {
            get
            {
                EnsureInitialized();
                return _maskBakeShader;
            }
        }

        public static bool BundleMissing => _bundleInitialized && _shader == null;

        private static bool _bundleInitialized;

        private static void EnsureInitialized()
        {
            if (_bundleInitialized) return;
            _bundleInitialized = true;

            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Log.Warn($"Cloak shader bundle not embedded ({ResourceName}). " +
                         "Cloak-only recolor disabled; falling back to whole-character tint.");
                return;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            try
            {
                _bundle = AssetBundle.LoadFromMemory(ms.ToArray());
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to load cloak shader bundle: {ex}");
                return;
            }

            if (_bundle == null)
            {
                Log.Warn("AssetBundle.LoadFromMemory returned null for the cloak shader bundle.");
                return;
            }

            _shader = TryLoadCloakHueShift(_bundle);
            if (_shader != null)
                Log.Info($"Loaded cloak shader '{_shader.name}' from embedded bundle.");
            else
                Log.Warn($"Cloak shader not found in embedded bundle (expected '{ShaderAssetName}' or '{ShaderName}'). " +
                         "Rebuild the bundle from Shaders/.");

            _maskBakeShader = TryLoadMaskBake(_bundle);
        }

        private static Shader? TryLoadCloakHueShift(AssetBundle bundle)
        {
            var s = bundle.LoadAsset<Shader>(ShaderAssetName);
            if (s != null) return s;

            s = bundle.LoadAsset<Shader>(ShaderName);
            if (s != null) return s;

            var all = bundle.LoadAllAssets<Shader>();
            if (all == null || all.Length == 0) return null;

            foreach (var sh in all)
            {
                if (sh != null && sh.name == ShaderName) return sh;
            }

            return all.Length == 1 ? all[0] : null;
        }

        private static Shader? TryLoadMaskBake(AssetBundle bundle)
        {
            var s = bundle.LoadAsset<Shader>(MaskBakeAssetName);
            if (s != null) return s;

            s = bundle.LoadAsset<Shader>(MaskBakeShaderName);
            if (s != null) return s;

            foreach (var sh in bundle.LoadAllAssets<Shader>())
            {
                if (sh != null && sh.name == MaskBakeShaderName) return sh;
            }

            return null;
        }
    }
}
