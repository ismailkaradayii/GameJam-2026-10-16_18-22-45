Shader "Itfaiye/Pistol Tracer"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color; return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float edge = saturate(1-abs(i.uv.y*2-1));
                return half4(i.color.rgb, i.color.a * edge * sin(saturate(i.uv.x)*3.14159));
            }
            ENDHLSL
        }
    }
}
