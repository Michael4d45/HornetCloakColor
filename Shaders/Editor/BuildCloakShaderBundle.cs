// Drop this file into a Unity 6000.0.50 project under Assets/Editor/ together with the
// shader at Assets/Shaders/CloakHueShift.shader. Then run:
//
//     HornetCloakColor -> Build Shader Bundle
//
// from the editor menu. The built bundle is written to <project>/Build/cloakshader.bundle
// and should be copied to HornetCloakColor/Resources/cloakshader.bundle in this repo.
//
// The mod loads the bundle as an embedded resource so end users do not have to copy it
// alongside the DLL.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HornetCloakColor.EditorTools
{
    public static class BuildCloakShaderBundle
    {
        private const string ShaderAssetPath = "Assets/Shaders/CloakHueShift.shader";
        private const string BundleName = "cloakshader.bundle";

        [MenuItem("HornetCloakColor/Build Shader Bundle (Windows)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64);

        [MenuItem("HornetCloakColor/Build Shader Bundle (macOS)")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX);

        [MenuItem("HornetCloakColor/Build Shader Bundle (Linux)")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64);

        private static void Build(BuildTarget target)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
            if (shader == null)
            {
                Debug.LogError($"[HornetCloakColor] Shader not found at {ShaderAssetPath}.");
                return;
            }

            var importer = AssetImporter.GetAtPath(ShaderAssetPath);
            if (importer == null)
            {
                Debug.LogError("[HornetCloakColor] Could not get asset importer for shader.");
                return;
            }

            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();

            var outDir = Path.Combine(Application.dataPath, "../Build");
            Directory.CreateDirectory(outDir);

            var manifest = BuildPipeline.BuildAssetBundles(
                outDir,
                BuildAssetBundleOptions.None,
                target);

            if (manifest == null)
            {
                Debug.LogError("[HornetCloakColor] BuildAssetBundles returned null. Check the console for errors.");
                return;
            }

            var produced = Path.Combine(outDir, BundleName);
            Debug.Log($"[HornetCloakColor] Built {produced} for {target}. " +
                      $"Copy this to HornetCloakColor/Resources/cloakshader.bundle in the mod repo.");
        }
    }
}
#endif
