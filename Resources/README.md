# Resources

The mod embeds `cloakshader.bundle` (built from `Shaders/CloakHueShift.shader`)
into the DLL so end users only need to copy a single file.

This bundle is **gitignored** because it's a binary asset rebuilt from source.
See `../Shaders/README.md` for instructions on baking it in Unity.

When this folder doesn't contain `cloakshader.bundle`, the mod still builds and
runs — it simply falls back to tinting the entire character (the original
behavior) instead of cloak-only recolor.

To activate the cloak-only path you'll need to bake the bundle once:

Install Unity 6000.0.50, create an empty 3D project.
Drop Shaders/CloakHueShift.shader into Assets/Shaders/ and Shaders/Editor/BuildCloakShaderBundle.cs into Assets/Editor/.
Menu: HornetCloakColor → Build Shader Bundle (Windows).
Copy <unity-proj>/Build/cloakshader.bundle → HornetCloakColor/Resources/cloakshader.bundle.
dotnet build -c Release again — it'll embed the bundle into the DLL.
Without the bundle, the mod still runs (it just falls back to whole-character tint).