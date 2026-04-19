# HornetCloakColor

A BepInEx mod for **Hollow Knight: Silksong** that lets you change the color of Hornet's cloak.

Works **single-player out of the box**. If **[SSMP (Silksong Multiplayer)](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/)** is also installed, the chosen color is automatically synchronized to every other player on the server so everyone sees your custom cloak in real time.

**Source:** [github.com/Michael4d45/HornetCloakColor](https://github.com/Michael4d45/HornetCloakColor)

## Features

- Pick from 12 tasteful presets (Crimson, Scarlet, Amber, Gold, Emerald, Teal, Azure, Royal, Violet, Magenta, Obsidian, Ivory) or supply your own hex / RGB color.
- **Cloak-only recolor**: a custom shader matches pixels to two reference cloak colors (front `#79404b`, underside `#501f3b` by default) and recolors only those pixels. Edit `cloak_palette.json` next to the DLL if you need to tune matching for a texture update.
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
   - The `#79404b` / `#501f3b` entries in `cloak_palette.json` only define cloak masking for the shader — they are not this preset.
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
  "cloakFront": "#79404b",
  "cloakUnder": "#501f3b",
  "matchRadius": 0.18,
  "debugLogging": false
}
```

- `cloakFront` / `cloakUnder`: reference hex colors from the vanilla texture (front and underside).
- `matchRadius`: how far a pixel may deviate in RGB space and still match (roughly 0.05–0.35).
  Raise slightly if some cloak pixels are missed after a game update.
- `debugLogging`: when `true`, logs extra lines (e.g. each cloak color change). Default `false`.

If the file is missing or invalid, the same defaults are used from code.

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

- A small `CloakRecolor` MonoBehaviour is attached to each player's GameObject (local hero
  and SSMP-spawned remote players). It reasserts the tint every `LateUpdate`, which keeps
  the recolor consistent across `tk2dSpriteAnimator` material swaps.
- **Cloak shader path** swaps the renderer's shader for `HornetCloakColor/CloakHueShift` and
  pushes the chosen color in HSV. The shader measures RGB distance from each texel to two
  reference colors (from `cloak_palette.json`), masks the cloak, then replaces hue/saturation
  while preserving value for shading. The shader is shipped as an `AssetBundle` embedded in the DLL.
- **Fallback** (when the shader bundle isn't present) tints the whole character via the
  `tk2dSprite` vertex color and the `MeshRenderer` material color.
- Color updates are serialized as a 5-byte packet (player ID + RGB) and sent through the
  standard `IClientAddonNetworkSender` / `IServerAddonNetworkSender` channels exposed by SSMP.
- The server keeps a simple in-memory table of `playerId -> CloakColor` and replays it to any
  new joiner so late arrivals see correct colors immediately.

## Baking the shader bundle

The cloak-only path requires `Resources/cloakshader.bundle`. It's gitignored, so contributors
need to bake it in Unity once. See [Shaders/README.md](./Shaders/README.md) for step-by-step
instructions (TL;DR: open Unity 6000.0.50, drop the shader and editor script in, click
**HornetCloakColor → Build Shader Bundle**, copy the output to `Resources/`).

If the bundle isn't present the build still succeeds and the mod gracefully falls back to
the legacy whole-character tint at runtime.

## License

MIT — see [LICENSE.txt](./LICENSE.txt).
