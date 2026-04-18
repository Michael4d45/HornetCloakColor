# HornetCloakColor

A BepInEx mod for **Hollow Knight: Silksong** that lets you change the color of Hornet's cloak.
It is a client + server add-on for **[SSMP (Silksong Multiplayer)](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/)** — the color you choose is synchronized to every other player on the server, so everyone sees your custom cloak in real time.

**Source:** [github.com/Michael4d45/HornetCloakColor](https://github.com/Michael4d45/HornetCloakColor)

## Features

- Pick from 12 tasteful presets (Crimson, Scarlet, Amber, Gold, Emerald, Teal, Azure, Royal, Violet, Magenta, Obsidian, Ivory) or supply your own hex / RGB color.
- Zero-friction configuration through the BepInEx configuration manager (F1 in game).
- Fully networked — connected players automatically see each other's cloak color the moment they enter the same scene.
- Late-joiners receive a replay of every existing player's color on connect, so nobody ever sees the wrong color.

## Installation

### Via a mod manager (recommended)

1. Install [SSMP](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/) through r2modman / Thunderstore Mod Manager.
2. Install **HornetCloakColor** alongside it.
3. Launch Silksong through the mod manager.

### Manual

1. Install [BepInEx](https://thunderstore.io/c/hollow-knight-silksong/p/BepInEx/BepInExPack_Silksong/) for Silksong.
2. Install [SSMP](https://thunderstore.io/c/hollow-knight-silksong/p/SSMP/SSMP/) into `BepInEx/plugins/SSMP/`.
3. Copy `HornetCloakColor.dll` into `BepInEx/plugins/HornetCloakColor/`.
4. Launch the game.

## Usage

1. Launch Silksong and reach the main menu (this ensures BepInEx finishes loading the config).
2. Open the BepInEx configuration manager (default keybind: **F1**).
3. Under **HornetCloakColor → Appearance**:
   - Set **Cloak Color Preset** to any of the listed presets, **or**
   - Set it to `Custom` and provide a color in the **Custom Cloak Color** field.
     Accepted formats: `#AA3344`, `AA3344`, or `170,51,68` (decimal 0–255).
4. Load a save or join a multiplayer server — your cloak will be tinted immediately, and every
   other SSMP player with this mod installed will see the same color on your Hornet.

Players **without** this mod will still see vanilla Hornet — they simply won't receive the color
updates. You'll still see their vanilla cloaks correctly.

## Compatibility

- **Game**: Hollow Knight: Silksong
- **BepInEx**: 5.4.21 or later
- **SSMP**: Requires SSMP to be installed. The mod registers as both an SSMP client addon and
  server addon so it works on dedicated SSMP servers and on peer-hosted games.
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

- The local cloak tint is applied via the `tk2dSprite` component on `HeroController.instance`
  (with a `MeshRenderer` material fallback). Both are non-destructive vertex / material color
  multipliers, so swapping cloaks is as cheap as changing a `Color` value per frame.
- Color updates are serialized as a 5-byte packet (player ID + RGB) and sent through the
  standard `IClientAddonNetworkSender` / `IServerAddonNetworkSender` channels exposed by SSMP.
- The server keeps a simple in-memory table of `playerId -> CloakColor` and replays it to any
  new joiner so late arrivals see correct colors immediately.

## License

MIT — see [LICENSE.txt](./LICENSE.txt).
