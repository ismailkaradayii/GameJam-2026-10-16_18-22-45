Shader "Itfaiye/Pistol Impact"
{
    Properties
    {
        _Tint ("Chipped surface", Color) = (.32,.30,.27,1)
        _Opacity ("Opacity", Range(0,1)) = 1
        _Seed ("Variation", Float) = 0
        _Surface ("Surface kind", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Opacity, _Seed, _Surface;
            CBUFFER_END
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o;
            }
            float Hash(float n) { return frac(sin(n * 127.1 + _Seed * 19.7) * 43758.54); }
            float EdgeNoise(float a)
            {
                float sector = (a + 3.14159) / 6.28318 * 19;
                float k = floor(sector), f = frac(sector);
                return lerp(Hash(fmod(k,19)), Hash(fmod(k+1,19)), f*f*(3-2*f));
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float a = atan2(p.y, p.x);
                float r = length(p);
                float angular = EdgeNoise(a);
                float edge = .51 + .33 * angular;
                float grain = frac(sin(dot(floor(p * 53), float2(127.1,311.7)) + _Seed) * 43758.54);
                float metal = 1-step(.1,abs(_Surface-1));
                float chip = 1 - smoothstep(edge - .07, edge, r + (grain-.5)*.055);
                float pitEdge = .23 + .055 * angular;
                float pit = 1 - smoothstep(pitEdge-.035, pitEdge+.018, r);
                float rim = smoothstep(pitEdge-.01,pitEdge+.035,r) * (1-smoothstep(pitEdge+.04,pitEdge+.1,r));
                float crack = 0;
                [unroll] for(int k=0;k<5;k++)
                {
                    float direction=Hash(k+31)*6.28318-3.14159;
                    float d=abs(atan2(sin(a-direction),cos(a-direction)) + .025*sin(r*23+k));
                    crack=max(crack,(1-smoothstep(.003,.016,d))*(1-smoothstep(.34,edge+.04,r)));
                }
                crack *= lerp(1,.2,metal);
                half3 color = _Tint.rgb * (.68 + grain * .42);
                color += rim * lerp(.13,.28,metal) * saturate(.5 + p.y);
                color = lerp(color, half3(.018,.016,.014), saturate(pit + crack * .65));
                float alpha = max(pit, chip * (.44 + grain * .44) + crack*.3);
                return half4(color, alpha * _Opacity * _Tint.a);
            }
            ENDHLSL
        }
    }
}
