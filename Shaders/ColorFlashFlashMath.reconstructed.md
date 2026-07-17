# Sprites/Default-ColorFlash — reconstructed flash math

Source: LZ4-decompressed `compressedBlob` from Silksong `shaders_assets_all.bundle`,
96 DXBC variants carved and decompiled with 3Dmigoto `cmd_Decompiler` (2026-07-17).

Artifacts: `CloakMasks/_analysis/colorflash_dump/subprograms/`

## Runtime contract (confirmed via SpriteFlash + GMS)

- Property names: `_FlashAmount`, `_FlashColor` (PascalCase)
- Written by `SpriteFlash.SetParams` via MaterialPropertyBlock
- GMS body original shader: `Sprites/Default-ColorFlash`

## Keyword set (from SerializedShader)

`PIXELSNAP_ON`, `RECOLOUR`, `BLACKTHREAD`, `MASKING_SPRITE`, `CAN_HUESHIFT`,
`IS_CHARACTER`, `IS_HERO`, `CAN_DESATURATE`, `CAN_LERP_AMBIENT`, `LOCAL_SPACE_X/Y`,
plus stereo variants.

## Flash blend (simplest pixel variant, e.g. dxbc_088)

After sampling `_MainTex` and optional HSV/`CAN_HUESHIFT` path, with vertex color `v1`:

```hlsl
float4 tex = tex2D(_MainTex, uv);
float3 baseRgb = /* after optional hue-shift etc */ tex.rgb;
baseRgb *= v1.rgb;                    // vertex tint
float3 flashed = lerp(baseRgb, _FlashColor.rgb, _FlashAmount);
float alpha = tex.a * v1.a;
return float4(flashed * alpha, alpha); // premultiplied alpha out
```

Decompiled form (cb0[3] ≈ `_FlashColor`, cb0[4].x ≈ `_FlashAmount`):

```hlsl
// r2 = sampled tex (possibly hue-shifted into rgb)
// v1 = vertex COLOR
r0.xyz = -r2.xyz * v1.xyz + cb0[3].xyz;       // flashColor - (tex*vert)
r1.xyzw = v1.xyzw * r2.xyzw;                  // base = tex * vert
r0.xyz = cb0[4].xxx * r0.xyz + r1.xyz;        // lerp(base, flashColor, amount)
o0.xyz = r0.xyz * r1.www;                     // premultiply
o0.w = r1.w;
```

## Implications for CloakHueShift

1. Flash is a **final-stage lerp toward `_FlashColor` by `_FlashAmount`**, after base color is computed.
2. Output uses **premultiplied alpha** (`rgb *= a`).
3. Full ColorFlash also has hue-shift / desaturate / ambient / black-thread branches (larger variants).
   For hit-flash parity, only the flash lerp + premultiply are required.
4. There is **no cloak mask texture** in ColorFlash — mask-based cloak recolor cannot be done by
   staying on ColorFlash alone without adding a new texture input (i.e. forking).

## CloakHueShift flash fragment (option D — implemented in 1.16.0)

```hlsl
// after cloak HSV recolor:
finalRgb *= IN.color.rgb;
float alpha = tex.a * IN.color.a;
finalRgb = lerp(finalRgb, _FlashColor.rgb, saturate(_FlashAmount));
outCol.rgb = finalRgb; // straight alpha (SrcAlpha OneMinusSrcAlpha)
outCol.a = alpha;
```

Vanilla ColorFlash also does `rgb *= alpha` (premultiply) for `One OneMinusSrcAlpha` blends.
CloakHueShift keeps straight alpha to avoid regressing existing cloak materials.

**Status:** source + Windows/Linux/macOS AssetBundles rebaked; embedded in 1.16.0 Release build.
