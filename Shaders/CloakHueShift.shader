// Shader: HornetCloakColor/CloakHueShift
//
// Recolors texels whose RGB is close to ANY of up to 16 reference cloak colors.
// Optionally, texels close to ANY of up to 16 "avoid" colors get their mask reduced
// (skin, metal, etc.) so they are not recolored even if they sit near cloak colors in RGB.
// Reference lists come from cloak_palette.json.
// User chooses a target tint via _TargetHue/_TargetSat/_TargetVal; matched
// texels get that hue/sat with original value preserved for shading.
Shader "HornetCloakColor/CloakHueShift"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Material Tint", Color) = (1,1,1,1)

        _TargetHue ("Target Hue (0-1)", Range(0,1)) = 0.0
        _TargetSat ("Target Saturation Multiplier", Range(0,2)) = 1.0
        _TargetVal ("Target Value Multiplier", Range(0,2)) = 1.0

        _MatchRadius ("RGB Match Radius", Range(0.02,0.6)) = 0.18
        _AvoidMatchRadius ("Avoid RGB Radius", Range(0.02,0.6)) = 0.18
        _Strength ("Recolor Strength", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_CLOAK_COLORS 16
            #define MAX_AVOID_COLORS 16

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            float _TargetHue;
            float _TargetSat;
            float _TargetVal;
            float _MatchRadius;
            float _AvoidMatchRadius;
            float _Strength;

            // Filled from C# every frame. Unused slots are pushed far away (rgb = 10) so
            // distance() stays huge and they never contribute to the mask.
            float4 _SrcColors[MAX_CLOAK_COLORS];
            float4 _AvoidColors[MAX_AVOID_COLORS];

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color    = IN.color * _Color;
                return OUT;
            }

            float3 RGBtoHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
                float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                float3 t = tex.rgb;

                float minD = 999.0;
                [unroll]
                for (int i = 0; i < MAX_CLOAK_COLORS; i++)
                {
                    float di = distance(t, _SrcColors[i].rgb);
                    minD = min(minD, di);
                }

                float inner = _MatchRadius * 0.35;
                float mask = (1.0 - smoothstep(inner, _MatchRadius, minD)) * _Strength;

                // Suppress recolor where texel is close to any avoid color (skin, trim, etc.)
                if (_AvoidMatchRadius > 1e-5)
                {
                    float minAvoid = 999.0;
                    [unroll]
                    for (int j = 0; j < MAX_AVOID_COLORS; j++)
                    {
                        float dj = distance(t, _AvoidColors[j].rgb);
                        minAvoid = min(minAvoid, dj);
                    }
                    float aInner = _AvoidMatchRadius * 0.35;
                    float avoidFactor = smoothstep(aInner, _AvoidMatchRadius, minAvoid);
                    mask *= avoidFactor;
                }

                float3 hsv = RGBtoHSV(t);
                float3 hsvOut = float3(_TargetHue,
                                       saturate(hsv.y * _TargetSat),
                                       saturate(hsv.z * _TargetVal));
                float3 recolored = HSVtoRGB(hsvOut);

                float3 finalRgb = lerp(t, recolored, mask);

                fixed4 outCol;
                outCol.rgb = finalRgb * IN.color.rgb;
                outCol.a   = tex.a * IN.color.a;
                return outCol;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
