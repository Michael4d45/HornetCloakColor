// Shader: HornetCloakColor/CloakHueShift
//
// Recolor weight comes only from _CloakMaskTex (R channel, 0–1). User tint via _TargetHue/_TargetSat/_TargetVal;
// matched weight scales HSV toward that tint (value preserved for shading).
Shader "HornetCloakColor/CloakHueShift"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Material Tint", Color) = (1,1,1,1)

        _TargetHue ("Target Hue (0-1)", Range(0,1)) = 0.0
        _TargetSat ("Target Saturation Multiplier", Range(0,2)) = 1.0
        _TargetVal ("Target Value Multiplier", Range(0,2)) = 1.0

        _Strength ("Recolor Strength", Range(0,1)) = 1.0

        [NoScaleOffset] _CloakMaskTex ("Cloak Mask (R = weight)", 2D) = "white" {}
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
            sampler2D _CloakMaskTex;
            fixed4    _Color;

            float _TargetHue;
            float _TargetSat;
            float _TargetVal;
            float _Strength;

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

                float mask = tex2D(_CloakMaskTex, IN.texcoord).r * _Strength;

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
