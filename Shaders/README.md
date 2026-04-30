# Cloak hue-shift shader

This folder contains **CloakHueShift** (in-game: R mask × HSV tint only).

The shader runs in the built-in render pipeline, sits in for `Sprites/Default`, and is
shipped as an `AssetBundle` so the mod can ship as a single DLL.

## Files

| File | Purpose |
| ---- | ------- |
| `CloakHueShift.shader` | In-game shader: samples `_CloakMaskTex.r` and applies HSV tint (no RGB distance at draw time). |
| `Editor/BuildCloakShaderBundle.cs` | Unity editor menu that builds the AssetBundle. |

## How to bake the AssetBundle

You only need to do this once (or whenever the shader changes).

1. Install **Unity 6000.0.50** (the version Silksong ships with). Other 6000.x patches
   should also work; major version mismatches will refuse to load at runtime.
2. Create a new empty 3D project.
3. Copy `CloakHueShift.shader` into the project at
   `Assets/Shaders/CloakHueShift.shader`.
4. Copy `Editor/BuildCloakShaderBundle.cs` into the project at
   `Assets/Editor/BuildCloakShaderBundle.cs`.
5. From the menu bar, choose **HornetCloakColor → Build Shader Bundle (Windows)**
   (or macOS / Linux as appropriate).
6. Find the produced bundle at `<project>/Build/cloakshader.bundle` and copy it to:

   ```
   HornetCloakColor/Resources/cloakshader.bundle
   ```

7. Rebuild the mod with `dotnet build -c Release`. The csproj will automatically embed
   the bundle into the DLL when it's present.

**Note:** `AssetBundle.LoadAsset` looks up assets by their **Unity asset name** (usually the
shader file name without extension, e.g. `CloakHueShift`), not by the `Shader "Path/Name"`
string inside the `.shader` file. The mod loader tries both plus a full scan so either works.

## Runtime behavior

* If the bundle is embedded, the mod swaps `Sprites/Default` for `CloakHueShift`, binds the
  per-atlas **R mask** from `CloakMasks/...`, and uploads the user's tint in HSV.
* Missing mask PNGs are left untouched; the mod no longer bakes masks at runtime.
* If the bundle is missing, the mod falls back to tinting the whole sprite via vertex color.

## Mask PNGs

The mod loads mask PNGs from `CloakMasks/<tk2d collection>/<atlas>.png` next to the DLL
(legacy `CloakMasks/<atlas>.png` is still supported). The R channel is the recolor weight.

The **CloakHueShift** fragment shader only: samples mask R × `_Strength`, then replaces hue/saturation
while preserving value for shading.

Re-bake the AssetBundle after **shader** changes. Mask PNG edits do not require Unity.
