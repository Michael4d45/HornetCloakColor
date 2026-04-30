# HornetCloakColor

A BepInEx mod for **Hollow Knight: Silksong** that lets you change the color of Hornet's cloak.

Works **single-player out of the box**. If **[SSMP (Silksong Multiplayer)](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/)** is also installed, the chosen color is automatically synchronized to every other player on the server so everyone sees your custom cloak in real time.

**Source:** [github.com/Michael4d45/HornetCloakColor](https://github.com/Michael4d45/HornetCloakColor)

## Features

- Pick from 12 tasteful presets (Crimson, Scarlet, Amber, Gold, Emerald, Teal, Azure, Royal, Violet, Magenta, Obsidian, Ivory) or supply your own hex / RGB color.
- **Cloak-only recolor**: a custom shader uses curated R-channel mask PNGs under `CloakMasks/` and recolors only masked pixels.
- **Scene-wide coverage**: a very-late scene pass tints orphan `tk2dSprite`s whose atlas has a matching mask PNG, including poses outside the player hierarchy.
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
4. Load a save or join a multiplayer server — your cloak will be tinted immediately, and every
   other SSMP player with this mod installed will see the same color on your Hornet.

Players **without** this mod will still see vanilla Hornet — they simply won't receive the color
updates. You'll still see their vanilla cloaks correctly.

### `cloak_palette.json`

Shipped next to `HornetCloakColor.dll`. Runtime masking is driven by `CloakMasks/`; this file only
controls diagnostics and the player hierarchy rescan cadence. You normally do not need to edit it.

```json
{
  "debugLogging": false,
  "mapIconDebugLogging": false,
  "heroMeshRescanIntervalFrames": 30,
  "dumpDiscoveredTextures": false
}
```

- `debugLogging`: when `true`, logs extra lines (e.g. each cloak color change, how many
  scene textures the scanner is matching/ignoring).
  Default `false`.
- `mapIconDebugLogging`: logs SSMP map/compass icon synchronization without enabling all cloak logs.
- `heroMeshRescanIntervalFrames`: how often the player hierarchy cache is refreshed.
- `dumpDiscoveredTextures`: when `true`, writes the source atlas beside each discovered mask path
  as `<atlas>-original.png`. If a mask is missing, it also writes an empty transparent
  `<atlas>.png` template in the correct `CloakMasks/<collection>/` folder so you can paint it.

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
  and SSMP-spawned remote players). It caches the player hierarchy's `MeshRenderer`s and
  refreshes that cache every `heroMeshRescanIntervalFrames` (default 30) so newly toggled
  child objects (some animations use extra meshes or inactive children) are picked up. Per
  `LateUpdate` it just iterates the cache and calls the memoized applier (see below).
- A scene-wide `CloakSceneScanner` runs on its own GameObject (DontDestroyOnLoad) at a high
  script-execution order. **Discovery** (`FindObjectsByType<tk2dSprite>` + filtering) only runs
  on `SceneManager.activeSceneChanged` and on a slow trickle (every ~2 s) for late-spawned
  sprites — *not* per frame. Per `LateUpdate` the scanner just walks its cached eligible-
  renderer list and calls the memoized applier. It covers renderers **outside** the player
  hierarchy (steam-vent recoil, item-get pose, etc.). Renderers under a `CloakRecolor` and
  compass icons are skipped so remote players keep their own colors.
- **Memoized applier**: `CloakMaterialApplier` caches per-renderer state (last `sharedMaterial`
  instance ID + applied mode + color). When tk2d swaps a renderer's material mid-animation the
  instance ID changes and we redo the work; otherwise the call is a single dict lookup and
  early return — `SetTexture` / `SetFloat` are *not* re-pushed every frame. Scene transitions,
  palette reloads, and color changes all invalidate the cache so freshly bound materials get
  the slow path on first sight.
- **Sprite sheets / atlases:** Hornet’s art can live in multiple atlases (e.g. idle vs sprint).
  Recolor weights live in **`CloakMasks/<collection>/<atlas>.png`** (R channel). If a sheet still
  looks wrong, edit that PNG in the repo and rebuild.
- **Cloak shader path** swaps the renderer's shader for `HornetCloakColor/CloakHueShift` and
  pushes the chosen tint in HSV. The fragment shader reads **only** the R mask texture (no
  per-pixel RGB matching at draw time). The shader is shipped as an `AssetBundle` embedded in the DLL.
- **Standalone SpriteRenderer frames** are supported only when a mask exists under
  `CloakMasks/Texture2D/<texture-or-sprite>.png`. This covers non-tk2d one-off animation
  frames such as the diving-bell bench-grab sequence without widening the scanner to every UI
  SpriteRenderer.
- **Fallback** (when the shader bundle isn't present) tints the whole character via the
  `tk2dSprite` vertex color and the `MeshRenderer` material color.
- Color updates are serialized as a 5-byte packet (player ID + RGB) and sent through the
  standard `IClientAddonNetworkSender` / `IServerAddonNetworkSender` channels exposed by SSMP.
- The server keeps a simple in-memory table of `playerId -> CloakColor` and replays it to any
  new joiner so late arrivals see correct colors immediately.

## Shipped `CloakMasks/`

The repo includes **`CloakMasks/<tk2d collection>/<atlas>.png`** and a small number of
**`CloakMasks/Texture2D/<texture-or-sprite>.png`** standalone SpriteRenderer masks next to the
project root (same paths the mod uses under `BepInEx/plugins/HornetCloakColor/`). These files
are **version-controlled**, copied into the build output with the DLL, and included in
Thunderstore zips. Update them in the repo when you tune masks; the game only writes missing
templates when `dumpDiscoveredTextures` is enabled.

## Baking the shader bundle

The cloak-only path requires `Resources/cloakshader.bundle` (embeds **CloakHueShift**).
It's gitignored, so contributors need to bake it in Unity once. See [Shaders/README.md](./Shaders/README.md)
for step-by-step instructions (TL;DR: open Unity 6000.0.50, drop the shader and editor script in,
**HornetCloakColor → Build Shader Bundle**, copy the output to `Resources/`).

If the bundle isn't present the build still succeeds and the mod gracefully falls back to
the legacy whole-character tint at runtime.

## License

MIT — see [LICENSE.txt](./LICENSE.txt).
