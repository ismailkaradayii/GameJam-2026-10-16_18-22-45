Shader "Itfaiye/Pistol Particle"
{
    Properties
    {
        _Glow ("Hot Core", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
    }
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
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float edge = 1 - .13 * sin(a * 5 + r * 9) - .06 * cos(a * 9);
                float alpha = pow(saturate(1 - r / edge), 1.5);
                half3 color = lerp(input.color.rgb, half3(3, 2.6, 1.7), saturate(1 - r * 4) * _Glow);
                return half4(color, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
