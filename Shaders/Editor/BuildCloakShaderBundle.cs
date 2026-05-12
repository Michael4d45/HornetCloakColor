// Drop this file into a Unity 6000.0.50 project under Assets/Editor/ together with
// Assets/Shaders/CloakHueShift.shader. Then run:
//
//     HornetCloakColor -> Build Shader Bundle
//
// from the editor menu. The built bundle is written to <project>/Build/cloakshader.bundle
// and should be copied to HornetCloakColor/Resources/<platform>/cloakshader.bundle in this repo
// (windows, linux, or mac — see CloakShaderManager).
//
// The mod loads the bundle as an embedded resource so end users do not have to copy it
// alongside the DLL.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HornetCloakColor.EditorTools
{
    public static class BuildCloakShaderBundle
    {
        private const string CloakHueShiftPath = "Assets/Shaders/CloakHueShift.shader";
        private const string BundleName = "cloakshader.bundle";

        [MenuItem("HornetCloakColor/Build Shader Bundle (Windows)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64);

        [MenuItem("HornetCloakColor/Build Shader Bundle (macOS)")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX);

        [MenuItem("HornetCloakColor/Build Shader Bundle (Linux)")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64);

        /// <summary>Whether Unity can build player content (and thus shader bundles) for this target in this install.</summary>
        private static bool IsBuildTargetReady(BuildTarget target)
        {
            var group = BuildPipeline.GetBuildTargetGroup(target);
            return BuildPipeline.IsBuildTargetSupported(group, target);
        }

        private static string HubModuleHint(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows64 =>
                    "Unity Hub → Installs → your version → Add modules → enable Windows Build Support (if you are on Mac/Linux and need a Windows bundle).",
                BuildTarget.StandaloneLinux64 =>
                    "Unity Hub → Installs → your version → Add modules → enable Linux Build Support (Monolithic or IL2CPP; either is fine for shader bundle export).",
                BuildTarget.StandaloneOSX =>
                    "Unity Hub → Installs → your version → Add modules → enable macOS Build Support.",
                _ => "Unity Hub → Installs → Add modules → enable build support for that platform.",
            };
        }

        private static void ShowBuildSupportMissingDialog(BuildTarget target, string extra = null)
        {
            var label = target switch
            {
                BuildTarget.StandaloneWindows64 => "Windows",
                BuildTarget.StandaloneLinux64 => "Linux",
                BuildTarget.StandaloneOSX => "macOS",
                _ => target.ToString(),
            };

            var body = "This editor install cannot build AssetBundles for " + label + " because the matching " +
                       "build-support module is not installed.\n\n" + HubModuleHint(target) +
                       "\n\nAfter install finishes, restart Unity and run the menu item again.";

            if (!string.IsNullOrEmpty(extra))
                body += "\n\nDetails: " + extra;

            EditorUtility.DisplayDialog("HornetCloakColor — platform module missing", body, "OK");
        }

        private static void Build(BuildTarget target)
        {
            if (!IsBuildTargetReady(target))
            {
                ShowBuildSupportMissingDialog(target);
                Debug.LogError($"[HornetCloakColor] Add {target} build support in Unity Hub (see dialog), then retry.");
                return;
            }

            var paths = new[] { CloakHueShiftPath };
            foreach (var path in paths)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    Debug.LogError($"[HornetCloakColor] Shader not found at {path}.");
                    return;
                }

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    Debug.LogError($"[HornetCloakColor] Could not get asset importer for {path}.");
                    return;
                }

                importer.assetBundleName = BundleName;
                importer.SaveAndReimport();
            }

            var outDir = Path.Combine(Application.dataPath, "../Build");
            Directory.CreateDirectory(outDir);

            AssetBundleManifest manifest = null;
            try
            {
                manifest = BuildPipeline.BuildAssetBundles(
                    outDir,
                    BuildAssetBundleOptions.None,
                    target);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HornetCloakColor] BuildAssetBundles threw: {ex}");
                ShowBuildSupportMissingDialog(target, ex.Message);
                return;
            }

            if (manifest == null)
            {
                Debug.LogError("[HornetCloakColor] BuildAssetBundles returned null. Check the Console for Unity errors " +
                               "(often: required Linux/macOS build module not installed).");
                ShowBuildSupportMissingDialog(target,
                    "Console may list: \"required module is not installed\". Install that platform's build support via Unity Hub.");
                return;
            }

            var produced = Path.Combine(outDir, BundleName);
            var modRel = target switch
            {
                BuildTarget.StandaloneWindows64 => "HornetCloakColor/Resources/windows/cloakshader.bundle",
                BuildTarget.StandaloneLinux64 => "HornetCloakColor/Resources/linux/cloakshader.bundle",
                BuildTarget.StandaloneOSX => "HornetCloakColor/Resources/mac/cloakshader.bundle",
                _ => "HornetCloakColor/Resources/<platform>/cloakshader.bundle",
            };
            Debug.Log($"[HornetCloakColor] Built {produced} for {target} (CloakHueShift). " +
                      $"Copy this to {modRel} in the mod repo (bake all three OS targets for release).");
        }
    }
}
#endif
