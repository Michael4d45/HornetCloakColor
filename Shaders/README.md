# Cloak hue-shift shader

This folder contains the source for the **CloakHueShift** shader used by `HornetCloakColor`
to recolor only Hornet's cloak by matching texture pixels to reference RGB colors, with an
optional second list that **suppresses** recoloring where pixels match (skin, metal, etc.).

The shader runs in the built-in render pipeline, sits in for `Sprites/Default`, and is
shipped as an `AssetBundle` so the mod can ship as a single DLL.

## Files

| File | Purpose |
| ---- | ------- |
| `CloakHueShift.shader` | Shader source (RGB cloak mask + optional avoid mask + HSV hue replacement). |
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
  that swaps `Sprites/Default` for `CloakHueShift` and uploads reference colors from
  `cloak_palette.json` plus the user's chosen tint in HSV.
* If the bundle is missing, the mod falls back to tinting the whole sprite via vertex color.

## Reference colors and matching

The mod loads **`cloak_palette.json`** next to `HornetCloakColor.dll` (see `Config/cloak_palette.json`
in the repo). Schema:

| Field | Role |
| ----- | ---- |
| `cloakColors` | Array of reference hex colors (up to **16**). See shipped `Config/cloak_palette.json`. |
| `avoidColors` | Optional array (up to **16**). Texels close to any avoid color get the recolor mask reduced. |
| `matchRadius` | Max RGB distance (0–1 scale) for cloak matching; larger = more pixels included. |
| `avoidMatchRadius` | Same idea for `avoidColors`. If omitted, defaults to `matchRadius`. |
| `debugLogging` | Optional verbose logs. |

At runtime, arrays are uploaded as `_SrcColors[16]` and `_AvoidColors[16]` (unused slots pushed far
away so they never match). The fragment shader:

1. Computes `min` RGB distance to cloak references → smoothstep cloak mask.
2. If `_AvoidMatchRadius` &gt; 0, computes `min` distance to avoid references → `avoidFactor = smoothstep(inner, outer, minAvoid)` and **multiplies** the cloak mask (close to an avoid color → factor → 0).
3. Replaces hue/saturation with the user's color while preserving value for shading.

**Adding more colors:** sample pixels from the relevant atlas and append to `cloakColors` or
`avoidColors`. Re-bake the AssetBundle after **shader** changes; JSON-only edits do not require Unity.
