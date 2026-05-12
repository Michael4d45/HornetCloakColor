# Resources

The mod embeds **per-OS** shader bundles built from `Shaders/CloakHueShift.shader`:

- `windows/shaders.bundle`
- `linux/shaders.bundle`
- `macos/shaders.bundle`

They are **gitignored** (binary assets rebuilt from source). See `../Shaders/README.md`
for Unity bake steps. At runtime the mod picks the bundle for the current OS.

If none of those files are present, the mod still builds and runs — it falls back to
whole-character vertex tint.

Quick path: Unity 6000.0.50 project with the shader + editor script → menu
**HornetCloakColor → Build Shader Bundle** for each platform → copy each output to the
matching `Resources/<platform>/shaders.bundle` path → `dotnet build -c Release`.