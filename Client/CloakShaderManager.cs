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

        // Resource paths — keep in sync with the EmbeddedResource entry in HornetCloakColor.csproj.
        private const string ResourceName = "HornetCloakColor.Resources.cloakshader.bundle";

        private static bool _attemptedLoad;
        private static AssetBundle? _bundle;
        private static Shader? _shader;

        // Property IDs are cached to avoid the per-call string lookup on every renderer update.
        public static readonly int TargetHueId = Shader.PropertyToID("_TargetHue");
        public static readonly int TargetSatId = Shader.PropertyToID("_TargetSat");
        public static readonly int TargetValId = Shader.PropertyToID("_TargetVal");
        public static readonly int StrengthId  = Shader.PropertyToID("_Strength");
        public static readonly int CenterHueId = Shader.PropertyToID("_CenterHue");
        public static readonly int HueWidthId  = Shader.PropertyToID("_HueWidth");
        public static readonly int MinSatId    = Shader.PropertyToID("_MinSat");
        public static readonly int MinValId    = Shader.PropertyToID("_MinVal");

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
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Log.Warn($"Cloak shader bundle not embedded ({ResourceName}). " +
                         "Cloak-only recolor disabled; falling back to whole-character tint.");
                return null;
            }

            // AssetBundle.LoadFromStream wants a seekable stream and is happy with a MemoryStream.
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
                return null;
            }

            if (_bundle == null)
            {
                Log.Warn("AssetBundle.LoadFromMemory returned null for the cloak shader bundle.");
                return null;
            }

            var shader = TryLoadShaderFromBundle(_bundle);
            if (shader == null)
            {
                Log.Warn($"Cloak shader not found in embedded bundle (expected asset name '{ShaderAssetName}' "
                         + $"or runtime name '{ShaderName}'). Rebuild the bundle from Shaders/CloakHueShift.shader.");
                return null;
            }

            Log.Info($"Loaded cloak shader '{shader.name}' from embedded bundle.");
            return shader;
        }

        private static Shader? TryLoadShaderFromBundle(AssetBundle bundle)
        {
            // 1) Asset name from file (Unity's default when baking CloakHueShift.shader).
            var s = bundle.LoadAsset<Shader>(ShaderAssetName);
            if (s != null) return s;

            // 2) Rare: asset renamed to match the shader's internal path.
            s = bundle.LoadAsset<Shader>(ShaderName);
            if (s != null) return s;

            // 3) Enumerate — handles odd renames; prefer runtime Shader.name match.
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
