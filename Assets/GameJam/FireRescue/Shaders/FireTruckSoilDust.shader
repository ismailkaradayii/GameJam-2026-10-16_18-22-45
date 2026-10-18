Shader "GameJam/Fire Truck Soil Dust"
{
    Properties
    {
        _BaseMap ("Existing soil dust texture", 2D) = "white" {}
        _SoftDistance ("Soft intersection distance", Float) = 1.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "RisingSoil"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _SoftDistance;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);output.color=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z);return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 dustSample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
                float radius=length(input.uv*2-1);
                float roundEdge=smoothstep(0,.45,saturate(1-radius));
                float rawDepth=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                float sceneEye=LinearEyeDepth(rawDepth,_ZBufferParams);
                #if UNITY_REVERSED_Z
                float orthographicEye=lerp(_ProjectionParams.z,_ProjectionParams.y,rawDepth);
                #else
                float orthographicEye=lerp(_ProjectionParams.y,_ProjectionParams.z,rawDepth);
                #endif
                sceneEye=lerp(sceneEye,orthographicEye,unity_OrthoParams.w);
                float particleEye=-TransformWorldToView(input.positionWS).z;
                float soft=saturate((sceneEye-particleEye)/max(.01,_SoftDistance));
                // This source texture contains sparse ash flecks; the soft soil body must remain between them.
                float density=.65+.35*saturate(dustSample.a*3);
                float alpha=input.color.a*density*roundEdge*soft;
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting=.32+SampleSH(half3(0,1,0))*.45+sun.color*.45*sun.shadowAttenuation;
                half3 color=input.color.rgb*lerp(.75,1,dustSample.rgb)*lighting;
                return half4(MixFog(color,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
