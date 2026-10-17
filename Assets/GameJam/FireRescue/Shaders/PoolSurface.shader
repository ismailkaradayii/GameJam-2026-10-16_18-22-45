Shader "GameJam/Water/Pool Surface"
{
    Properties
    {
        _ShallowColor("Shallow water", Color) = (.12,.38,.39,1)
        _DeepColor("Deep water", Color) = (.055,.24,.28,1)
        _WaveStrength("Normal ripple strength", Range(0,.2)) = .045
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor, _DeepColor;
            half _WaveStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o; VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.positionWS.xz;
                float t=_Time.y;
                half dx=cos(p.x*2.7+p.y*1.7+t*.9)+.35*cos(p.x*7.1-p.y*4.3-t*1.8);
                half dz=sin(p.y*3.2-p.x*1.1-t*.7)+.3*sin(p.y*6.7+p.x*3.4+t*2.1);
                half3 normal=normalize(half3(dx*_WaveStrength,1,dz*_WaveStrength));
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel=.025+.85*pow(1-saturate(abs(dot(normal,view))),5);
                float2 screen=GetNormalizedScreenSpaceUV(i.positionCS);
                float depth=LinearEyeDepth(SampleSceneDepth(screen),_ZBufferParams);
                float surfaceDepth=-TransformWorldToView(i.positionWS).z;
                half thickness=saturate((depth-surfaceDepth)*.3);
                half3 water=lerp(_ShallowColor.rgb,_DeepColor.rgb,thickness);
                half3 scene=SampleSceneColor(screen+normal.xz*.008*saturate(depth-surfaceDepth));
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 reflection=GlossyEnvironmentReflection(reflect(-view,normal),i.positionWS,.09,1);
                reflection=max(reflection,SampleSH(reflect(-view,normal))*.7);
                half glint=pow(saturate(dot(normal,SafeNormalize(sun.direction+view))),180)*sun.shadowAttenuation;
                half3 rgb=lerp(scene,water*(SampleSH(normal)+sun.color*.45),.55+thickness*.3);
                rgb=lerp(rgb,reflection,fresnel)+sun.color*glint*1.3;
                return half4(MixFog(rgb,i.fog),.94);
            }
            ENDHLSL
        }
    }
}
