# Cloak hue-shift shader

This folder contains the source for the **CloakHueShift** shader used by `HornetCloakColor`
to recolor *only* Hornet's cloak (red-dominant pixels) instead of the entire character.

The shader runs in the built-in render pipeline, sits in for `Sprites/Default`, and is
shipped as an `AssetBundle` so the mod can ship as a single DLL.

## Files

| File | Purpose |
| ---- | ------- |
| `CloakHueShift.shader` | Shader source (HSV hue/sat/value gating + hue replacement). |
| `Editor/BuildCloakShaderBundle.cs` | Unity editor menu that builds the AssetBundle. |

## How to bake the AssetBundle

You only need to do this once (or whenever the shader changes).

1. Install **Unity 6000.0.50** (the version Silksong ships with). Other 6000.x patches
   should also work; major version mismatches will refuse to load at runtime.
2. Create a new empty 3D project.
3. Copy `CloakHueShift.shader` into the project at `Assets/Shaders/CloakHueShift.shader`.
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

* If the bundle is embedded, the mod adds a `CloakRecolor` MonoBehaviour to each player
  that swaps `Sprites/Default` for `CloakHueShift` and pushes the chosen color in HSV.
* If the bundle is missing, the mod falls back to the legacy "tint everything"
  vertex-color path, so older builds keep working.
* The user can force the legacy mode via the **Cloak Only Mode** config toggle.

## Tuning the cloak hue range

Defaults match Hornet's red cloak. If you want to recolor a different region of the
texture, tweak the shader properties at runtime (or change the defaults in the shader):

| Property | Meaning |
| -------- | ------- |
| `_CenterHue` | Center of the matched hue band, normalized to 0-1 (0/1 = red). |
| `_HueWidth` | Half-width of the band on each side of the center hue. |
| `_MinSat` | Pixels below this saturation are ignored (keeps off whites/greys). |
| `_MinVal` | Pixels below this brightness are ignored (keeps off blacks). |
| `_TargetHue` | Replacement hue (set by the mod from the user's chosen color). |
| `_Strength` | Blend amount between the original and recolored pixel. |
