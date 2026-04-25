# Cloak hue-shift shader

This folder contains **CloakHueShift** (in-game: R mask × HSV tint only) and **CloakMaskBake**
(bakes those R masks from `cloak_palette.json` reference and avoid colors).

The shader runs in the built-in render pipeline, sits in for `Sprites/Default`, and is
shipped as an `AssetBundle` so the mod can ship as a single DLL.

## Files

| File | Purpose |
| ---- | ------- |
| `CloakHueShift.shader` | In-game shader: samples `_CloakMaskTex.r` and applies HSV tint (no RGB distance at draw time). |
| `CloakMaskBake.shader` | RGB cloak + avoid mask math — used to bake `CloakMasks/**/*.png` on disk (must ship in the same bundle). |
| `Editor/BuildCloakShaderBundle.cs` | Unity editor menu that builds the AssetBundle (both shaders). |

## How to bake the AssetBundle

You only need to do this once (or whenever the shader changes).

1. Install **Unity 6000.0.50** (the version Silksong ships with). Other 6000.x patches
   should also work; major version mismatches will refuse to load at runtime.
2. Create a new empty 3D project.
3. Copy **both** `CloakHueShift.shader` and `CloakMaskBake.shader` into the project at
   `Assets/Shaders/CloakHueShift.shader` and `Assets/Shaders/CloakMaskBake.shader`.
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
  per-atlas **R mask** (`CloakMasks/...` or a 1×1 black fallback), and uploads the user's tint in HSV.
* **`cloak_palette.json` colors** are not sent to `CloakHueShift`; they are used when **generating**
  mask PNGs via `CloakMaskBake` (GPU blit).
* If the bundle is missing, the mod falls back to tinting the whole sprite via vertex color.

## `cloak_palette.json` and mask baking

The mod loads **`cloak_palette.json`** next to `HornetCloakColor.dll` (see `Config/cloak_palette.json`).
Fields that affect **mask generation** (`CloakMaskBake`):

| Field | Role |
| ----- | ---- |
| `cloakColors` | Array of reference hex colors (up to **16**). |
| `avoidColors` | Optional array (up to **16**). Texels close to any avoid color get the baked mask reduced. |
| `matchRadius` | RGB distance (0–1 scale) for cloak matching in the bake shader. |
| `avoidMatchRadius` | Same for `avoidColors`. If omitted, defaults to `matchRadius`. |
| `debugLogging` | Optional verbose logs. |

The **CloakHueShift** fragment shader only: samples mask R × `_Strength`, then replaces hue/saturation
while preserving value for shading.

**Adding more colors:** sample pixels from the relevant atlas and append to `cloakColors` or
`avoidColors`, then re-run mask generation (delete that atlas PNG or let the mod bake a missing file).
Re-bake the AssetBundle after **shader** changes; JSON-only edits do not require Unity.
