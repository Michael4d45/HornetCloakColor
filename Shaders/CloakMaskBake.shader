// HornetCloakColor/CloakMaskBake — procedural cloak mask only (matches CloakHueShift mask math).
// Used to bake PNGs next to the DLL; not used on in-game materials.
Shader "HornetCloakColor/CloakMaskBake"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _MatchRadius ("RGB Match Radius", Range(0.02, 0.6)) = 0.18
        _AvoidMatchRadius ("Avoid RGB Radius", Range(0.02, 0.6)) = 0.18
        _Strength ("Strength", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            Cull Off
            Lighting Off
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_CLOAK_COLORS 16
            #define MAX_AVOID_COLORS 16

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float     _MatchRadius;
            float     _AvoidMatchRadius;
            float     _Strength;

            float4 _SrcColors[MAX_CLOAK_COLORS];
            float4 _AvoidColors[MAX_AVOID_COLORS];

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                float3 t = tex.rgb;

                float minD = 999.0;
                [unroll]
                for (int i = 0; i < MAX_CLOAK_COLORS; i++)
                    minD = min(minD, distance(t, _SrcColors[i].rgb));

                float inner = _MatchRadius * 0.35;
                float proceduralMask = (1.0 - smoothstep(inner, _MatchRadius, minD));

                if (_AvoidMatchRadius > 1e-5)
                {
                    float minAvoid = 999.0;
                    [unroll]
                    for (int j = 0; j < MAX_AVOID_COLORS; j++)
                        minAvoid = min(minAvoid, distance(t, _AvoidColors[j].rgb));
                    float aInner = _AvoidMatchRadius * 0.35;
                    proceduralMask *= smoothstep(aInner, _AvoidMatchRadius, minAvoid);
                }

                proceduralMask *= _Strength;
                return fixed4(proceduralMask, proceduralMask, proceduralMask, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
