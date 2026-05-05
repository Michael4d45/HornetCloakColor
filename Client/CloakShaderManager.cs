using System.IO;
using System.Reflection;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Loads the <c>CloakHueShift</c> shader from the AssetBundle embedded in the mod DLL
    /// and exposes a singleton handle to it.
    ///
    /// The bundle is shipped as an embedded resource (<c>HornetCloakColor.Resources.cloakshader.bundle</c>)
    /// so the user only ever copies a single DLL into BepInEx/plugins. If the bundle isn't
    /// present (e.g. someone forgot to bake it) we degrade gracefully — <see cref="Shader"/>
    /// returns null and callers fall back to the legacy full-character tint.
    /// </summary>
    internal static class CloakShaderManager
    {
        /// <summary>
        /// Value of <see cref="Shader.name"/> at runtime (the <c>Shader "…"</c> line in the .shader file).
        /// </summary>
        private const string ShaderName = "HornetCloakColor/CloakHueShift";

        /// <summary>
        /// Default Unity asset name for <c>CloakHueShift.shader</c>. <see cref="AssetBundle.LoadAsset{T}(string)"/>
        /// matches the **asset** name (usually the file name without extension), not <see cref="ShaderName"/>.
        /// </summary>
        private const string ShaderAssetName = "CloakHueShift";

        // Resource paths — keep in sync with the EmbeddedResource entries in HornetCloakColor.csproj.
        private const string ResourceNameWindows = "HornetCloakColor.Resources.cloakshader.bundle";
        private const string ResourceNameLinux = "HornetCloakColor.Resources.cloakshaderLinux.bundle";

        private static bool _attemptedLoad;
        private static AssetBundle? _bundle;
        private static Shader? _shader;

        /// <summary>Maximum number of cloak source colors the shader supports (must match the .shader define).</summary>
        public const int MaxCloakColors = 16;

        /// <summary>Maximum number of avoid colors (suppress recolor when close in RGB).</summary>
        public const int MaxAvoidColors = 16;

        public static readonly int TargetHueId = Shader.PropertyToID("_TargetHue");
        public static readonly int TargetSatId = Shader.PropertyToID("_TargetSat");
        public static readonly int TargetValId = Shader.PropertyToID("_TargetVal");
        public static readonly int SrcColorsId = Shader.PropertyToID("_SrcColors");
        public static readonly int AvoidColorsId = Shader.PropertyToID("_AvoidColors");
        public static readonly int MatchRadiusId = Shader.PropertyToID("_MatchRadius");
        public static readonly int AvoidMatchRadiusId = Shader.PropertyToID("_AvoidMatchRadius");
        public static readonly int StrengthId = Shader.PropertyToID("_Strength");

        /// <summary>The loaded cloak-tint shader, or null if the bundle is unavailable.</summary>
        public static Shader? Shader
        {
            get
            {
                if (_attemptedLoad) return _shader;
                _attemptedLoad = true;
                _shader = LoadShader();
                return _shader;
            }
        }

        /// <summary>True once we've tried to load and failed; lets callers log a single warning.</summary>
        public static bool BundleMissing => _attemptedLoad && _shader == null;

        private static Shader? LoadShader()
        {
            var asm = Assembly.GetExecutingAssembly();

            // Platform-preferred resource first, then the other as a fallback.
            var preferred = Application.platform == RuntimePlatform.LinuxPlayer
                ? ResourceNameLinux
                : ResourceNameWindows;
            var fallback = preferred == ResourceNameLinux
                ? ResourceNameWindows
                : ResourceNameLinux;

            var shader = TryLoadShaderFromResource(asm, preferred, isPreferred: true);
            if (shader != null) return shader;

            shader = TryLoadShaderFromResource(asm, fallback, isPreferred: false);
            return shader;
        }

        private static Shader? TryLoadShaderFromResource(Assembly asm, string resourceName, bool isPreferred)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                if (isPreferred)
                {
                    Log.Warn($"Cloak shader bundle not embedded ({resourceName}) for platform {Application.platform}. " +
                             "Trying fallback bundle; if that also fails the mod will use whole-character tint.");
                }
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            AssetBundle? bundle;
            try
            {
                bundle = AssetBundle.LoadFromMemory(ms.ToArray());
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to load cloak shader bundle '{resourceName}': {ex}");
                return null;
            }

            if (bundle == null)
            {
                Log.Warn($"AssetBundle.LoadFromMemory returned null for cloak shader bundle '{resourceName}'.");
                return null;
            }

            var shader = TryLoadShaderFromBundle(bundle);
            if (shader == null)
            {
                Log.Warn($"Cloak shader not found in embedded bundle '{resourceName}' (expected asset name '{ShaderAssetName}' "
                         + $"or runtime name '{ShaderName}'). Rebuild the bundle from Shaders/CloakHueShift.shader.");
                bundle.Unload(true);
                return null;
            }

            // Verify the shader is actually usable on this platform.
            if (!shader.isSupported)
            {
                Log.Warn($"Cloak shader from '{resourceName}' is not supported on platform {Application.platform} " +
                         "(no compiled variants for this build target). Falling back.");
                bundle.Unload(true);
                return null;
            }

            _bundle = bundle;
            Log.Info($"Loaded cloak shader '{shader.name}' from embedded bundle '{resourceName}' for {Application.platform}.");
            return shader;
        }

        private static Shader? TryLoadShaderFromBundle(AssetBundle bundle)
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
    }
}
