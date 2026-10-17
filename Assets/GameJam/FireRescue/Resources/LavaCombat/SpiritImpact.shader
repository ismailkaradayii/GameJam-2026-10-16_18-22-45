Shader "GameJam/Spirit Impact"
{
    Properties { _Shape ("0 Corona / 1 Ribbon", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 position:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 position:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float _Shape;
            CBUFFER_END
            V Vert(A i) { V o; o.position=TransformObjectToHClip(i.position.xyz); o.uv=i.uv; o.color=i.color; return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float a;
                if (_Shape>.5) a=pow(saturate(1-abs(p.y)),1.5);
                else
                {
                    float r=length(p);
                    float core=pow(saturate(1-r),4);
                    float rays=pow(saturate(1-abs(p.x*p.y)*38),7)*pow(saturate(1-r),1.5);
                    a=saturate(core+rays*.7);
                }
                return half4(i.color.rgb,i.color.a*a);
            }
            ENDHLSL
        }
    }
}
