Shader "UI/Ashfall Ending Iris"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Progress ("Closure", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float _Progress;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float radius = length((input.uv - .5) * 2);
                float opening = lerp(1.48, -.42, saturate(_Progress));
                float darkness = smoothstep(opening, opening + .4, radius);
                return half4(0, 0, 0, darkness * input.color.a);
            }
            ENDHLSL
        }
    }
}
