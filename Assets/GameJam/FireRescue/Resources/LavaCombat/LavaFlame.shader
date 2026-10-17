Shader "GameJam/Lava Combat Flame"
{
    Properties { _Glow ("Hot Core", Float) = .6 _DstBlend ("Destination Blend", Float) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float _Glow;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv; o.color = input.color; return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float wave = sin(p.y * 9 - _Time.y * 14 + i.color.g * 7) * .1;
                p.x += wave * saturate(p.y + .4);
                float taper = lerp(1.05, .38, saturate(i.uv.y));
                float r = length(float2(p.x / taper, p.y * .92));
                float alpha = pow(saturate(1 - r), 1.35);
                half3 heat = lerp(i.color.rgb, half3(2.8, 2.1, .8), saturate(1 - r * 3) * _Glow);
                return half4(heat, i.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
