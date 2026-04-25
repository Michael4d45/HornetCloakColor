# HornetCloakColor

A BepInEx mod for **Hollow Knight: Silksong** that lets you change the color of Hornet's cloak.

Works **single-player out of the box**. If **[SSMP (Silksong Multiplayer)](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/)** is also installed, the chosen color is automatically synchronized to every other player on the server so everyone sees your custom cloak in real time.

**Source:** [github.com/Michael4d45/HornetCloakColor](https://github.com/Michael4d45/HornetCloakColor)

## Features

- Pick from 12 tasteful presets (Crimson, Scarlet, Amber, Gold, Emerald, Teal, Azure, Royal, Violet, Magenta, Obsidian, Ivory) or supply your own hex / RGB color.
- **Cloak-only recolor**: a custom shader matches pixels to reference cloak colors (defaults ship **16** cloak refs + **10** avoid refs in `cloak_palette.json`) and recolors only cloak-like pixels. Edit `cloak_palette.json` next to the DLL to tune masking when an animation/atlas still looks wrong.
- **Scene-wide coverage**: a separate scanner tints orphan `tk2dSprite`s that match substring lists in `cloak_palette.json` (collection / texture name / transform path), including poses outside the player hierarchy.
- If the shader bundle isn't embedded, the mod falls back to tinting the whole character.
- Zero-friction configuration through the BepInEx configuration manager (F1 in game).
- **Works without SSMP** — runs in single-player and recolors your own cloak. SSMP is a soft dependency: install it to also synchronize cloak colors with other players.
- When SSMP is installed, late-joiners receive a replay of every existing player's color on connect so nobody ever sees the wrong color.

## Installation

### Via a mod manager (recommended)

1. Install **HornetCloakColor** through r2modman / Thunderstore Mod Manager.
2. (Optional) Install [SSMP](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/) too if you want your color to sync to other players in multiplayer.
3. Launch Silksong through the mod manager.

### Manual

1. Install [BepInEx](https://thunderstore.io/c/hollow-knight-silksong/p/BepInEx/BepInExPack_Silksong/) for Silksong.
2. Copy `HornetCloakColor.dll` and `cloak_palette.json` into `BepInEx/plugins/HornetCloakColor/` (Thunderstore builds include both).
3. (Optional) Install [SSMP](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/) into `BepInEx/plugins/SSMP/` for multiplayer sync.
4. Launch the game.

## Usage

1. Launch Silksong and reach the main menu (this ensures BepInEx finishes loading the config).
2. Open the BepInEx configuration manager (default keybind: **F1**).
3. Under **HornetCloakColor → Appearance**:
   - Set **Cloak Color Preset** to **White** for the stock cloak look (no tint): you should see the game's normal reddish cloak, not a pale sheet. **White** means the tint multiply is `#FFFFFF`, not that the cloak is dyed white.
   - Pick another preset for a tinted cloak, **or** set it to `Custom` and provide a color in the **Custom Cloak Color** field.
     Accepted formats: `#AA3344`, `AA3344`, or `170,51,68` (decimal 0–255).
   - The `cloakColors` / `avoidColors` entries in `cloak_palette.json` only define which texels the shader treats as cloak vs protected — they are not the same as this tint preset.
4. Load a save or join a multiplayer server — your cloak will be tinted immediately, and every
   other SSMP player with this mod installed will see the same color on your Hornet.

Players **without** this mod will still see vanilla Hornet — they simply won't receive the color
updates. You'll still see their vanilla cloaks correctly.

### `cloak_palette.json`

Shipped next to `HornetCloakColor.dll`. It tells the cloak shader which **source** colors in the
sprite count as the cloak (so only those pixels are recolored to your preset). You normally do
not need to edit it.

```json
{
  "cloakColors": ["#79404b", "..."],
  "avoidColors": ["#ffffff", "..."],
  "matchRadius": 0.135,
  "avoidMatchRadius": 0.12,
  "debugLogging": false,
  "collectionNameContains": [],
  "sceneScanIntervalFrames": 3
}
```

(Full default lists are in [`Config/cloak_palette.json`](./Config/cloak_palette.json) in the repo.)

- `cloakColors`: reference hex colors from the vanilla atlases. Up to **16** entries.
  Texels close to **any** of them count as cloak. Add more hexes if a specific animation
  (recoil from a steam vent, dive, dash, etc.) still shows the original red — sample the
  cloak pixels from the matching atlas in an image editor and append them here.
- `avoidColors`: optional; up to **16** hex colors. Texels close to **any** of these in RGB get
  their recolor **mask reduced** (same smoothstep idea as matching, using `avoidMatchRadius`),
  so skin, metal trim, etc. can stay vanilla even if they sit near a cloak reference. Leave
  empty `[]` if you do not need masking.
- `matchRadius`: how far a pixel may deviate in RGB space and still count as cloak (roughly 0.05–0.35).
  Raise slightly if some cloak pixels are missed after a game update.
- `avoidMatchRadius`: same scale as `matchRadius`, used only for the avoid list. If omitted, it
  defaults to whatever `matchRadius` is after loading the file.
- `debugLogging`: when `true`, logs extra lines (e.g. each cloak color change, how many
  reference colors loaded, and which scene textures the scanner is matching/ignoring).
  Default `false`.
- `collectionNameContains`: the **only** way the scene-wide orphan scanner matches sprites
  (OR between substrings against `tk2dSprite.Collection.name`). Tune using `debugLogging`
  log lines (`collection=`). There is no texture registry or other fuzzy mode for the scanner.
- `dumpDiscoveredTextures`: when `true`, each distinct runtime texture (once per
  `InstanceID`) is written under `BepInEx/plugins/HornetCloakColor/TextureDumps/` as
  `Texture2D/<Texture.name>.png` (same naming as CustomizerT2D), copies under each
  `tk2d` collection subfolder (e.g. `Hornet Cln/`) when known, plus
  `texture_dump_manifest.json` in `TextureDumps/`. No layout options. Default `false` — turn
  on temporarily, then turn off again.
- `sceneScanIntervalFrames`: how often (in frames) the scanner runs `FindObjectsByType`
  to refresh its cache of orphan renderers. Tint is still applied **every** `LateUpdate` to
  that cache (so FX/HUD do not flicker when tk2d resets materials). `1` = rescan every frame;
  `3` is a good default. Higher = cheaper; new orphans may take up to N−1 frames to join the cache.

If the file is missing or invalid, the built-in defaults are used.

## Compatibility

- **Game**: Hollow Knight: Silksong
- **BepInEx**: 5.4.21 or later
- **SSMP**: *Optional.* If installed, the mod registers as both an SSMP client addon and a
  server addon so it works on dedicated SSMP servers and on peer-hosted games. If absent,
  the mod still recolors your own cloak — it just doesn't sync to other players.
- **Silksong.GameLibs**: Built against the `*-*` floating version (whichever is published).

## Building from source

```powershell
# Generate the Silksong path props (once) so the output copies into your install
dotnet new -i Silksong.Templates   # only if you don't already have the template
dotnet new silksongpath

# Build
dotnet build HornetCloakColor.csproj -c Release
```

A `thunderstore/dist/*.zip` is produced automatically alongside the compiled DLL if `thunderstore/thunderstore.toml` is configured.

## How it works

- A small `CloakRecolor` MonoBehaviour is attached to each player's root GameObject (local hero
  and SSMP-spawned remote players). Every `LateUpdate` it walks **all** `MeshRenderer`s under
  that object (`GetComponentsInChildren`, including **inactive** children). Some animations use
  extra meshes or toggled child objects; only updating the root renderer missed those frames.
- A scene-wide `CloakSceneScanner` runs on its own GameObject (DontDestroyOnLoad) at a high
  script-execution order. Every few frames it iterates `tk2dSprite`s and applies the local
  cloak pass when a sprite matches the **allowlist** in `cloak_palette.json` (case-insensitive
  substrings on tk2d collection name, `Texture.name`, and/or the full transform path; OR
  between those lists). It covers renderers **outside** the player hierarchy (steam-vent
  recoil, item-get pose, etc.). `CloakRecolor` ancestors are skipped so remote players keep
  their own colors. Orphans are **not** matched by the hero texture-ID registry (that exists
  only to tag atlases for optional PNG dumps). To discover substrings, enable `debugLogging` or
  use [Silksong AssetHelper](https://github.com/silksong-modding/Silksong.AssetHelper)
  `DebugTools.DumpAllAssetNames` and search the output — bundle **asset** paths are not the same
  as in-game `atlas0` names, and the AssetHelper in-repo test JSONs are not a player-atlas list.
- **Sprite sheets / atlases:** Hornet’s art can live in multiple atlases (e.g. idle vs sprint).
  Recolor weights live in **`CloakMasks/<collection>/<atlas>.png`** (R channel). If a sheet still
  looks wrong, edit that PNG or tune **`cloak_palette.json`** (`cloakColors`, `avoidColors`,
  `matchRadius`) and **re-bake** missing masks (GPU `CloakMaskBake` uses those fields; see
  `Shaders/README.md`).
- **Cloak shader path** swaps the renderer's shader for `HornetCloakColor/CloakHueShift` and
  pushes the chosen tint in HSV. The fragment shader reads **only** the R mask texture (no
  per-pixel RGB matching at draw time). The shader is shipped as an `AssetBundle` embedded in the DLL.
- **Fallback** (when the shader bundle isn't present) tints the whole character via the
  `tk2dSprite` vertex color and the `MeshRenderer` material color.
- Color updates are serialized as a 5-byte packet (player ID + RGB) and sent through the
  standard `IClientAddonNetworkSender` / `IServerAddonNetworkSender` channels exposed by SSMP.
- The server keeps a simple in-memory table of `playerId -> CloakColor` and replays it to any
  new joiner so late arrivals see correct colors immediately.

## Shipped `CloakMasks/`

The repo includes **`CloakMasks/<tk2d collection>/<atlas>.png`** next to the project root (same paths the mod uses under `BepInEx/plugins/HornetCloakColor/`). These files are **version-controlled**, copied into the build output with the DLL, and included in Thunderstore zips. Update them in the repo when you tune masks; the game can still write new PNGs next to the DLL when an atlas has no mask yet (GPU bake from `cloak_palette.json`).

## Baking the shader bundle

The cloak-only path requires `Resources/cloakshader.bundle` (embeds **CloakHueShift** and **CloakMaskBake**).
It's gitignored, so contributors need to bake it in Unity once. See [Shaders/README.md](./Shaders/README.md)
for step-by-step instructions (TL;DR: open Unity 6000.0.50, drop both `.shader` files and the editor script in,
**HornetCloakColor → Build Shader Bundle**, copy the output to `Resources/`).

If the bundle isn't present the build still succeeds and the mod gracefully falls back to
the legacy whole-character tint at runtime.

## License

MIT — see [LICENSE.txt](./LICENSE.txt).
