Shader "GameJam/Fire Truck Tire Tracks"
{
    Properties { _BaseColor ("Tread tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Name "SoilImpression"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR; half fog:TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.uv=input.uv;output.color=input.color*_BaseColor;output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float across=input.uv.x;
                float edge=smoothstep(0,.08,across)*smoothstep(0,.08,1-across);
                float row=frac(input.uv.y+abs(across-.5)*.8);
                float block=1-smoothstep(.64,.82,row);
                float centreGroove=smoothstep(.025,.055,abs(across-.5));
                float outerGrooves=smoothstep(.012,.04,abs(abs(across-.5)-.245));
                float grain=lerp(.8,1,frac(sin(dot(floor(input.uv*float2(46,31)),float2(12.9898,78.233)))*43758.5453));
                float alpha=input.color.a*edge*grain*(.28+.72*block*centreGroove*outerGrooves);
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light=.38+SampleSH(normalize(input.normalWS))*.35+sun.color*saturate(dot(normalize(input.normalWS),sun.direction))*.45*sun.shadowAttenuation;
                return half4(MixFog(input.color.rgb*light,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
