# Resources

The mod embeds `cloakshader.bundle` (built from `Shaders/CloakHueShift.shader`)
into the DLL so end users only need to copy a single file.

This bundle is **gitignored** because it's a binary asset rebuilt from source.
See `../Shaders/README.md` for instructions on baking it in Unity.

When this folder doesn't contain `cloakshader.bundle`, the mod still builds and
runs — it simply falls back to tinting the entire character (the original
behavior) instead of cloak-only recolor.
