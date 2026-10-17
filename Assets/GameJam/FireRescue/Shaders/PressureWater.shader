Shader "GameJam/Water/Pressure Water"
{
    Properties
    {
        _BaseColor("Water scattering", Color) = (.72,.86,.9,1)
        _Opacity("Opacity", Range(0,1)) = .82
        _Flow("Flowing core (0 for surface waves)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+8" }
        Pass
        {
            Name "WaterLit"
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Opacity;
                half _Flow;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR; half fog:TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.uv=input.uv; o.color=input.color; o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 normal=normalize(i.normalWS);
                normal *= dot(normal,view)<0 ? -1 : 1;
                float movement=i.uv.y*18-_Time.y*36;
                half streak=.65+.35*sin(movement+sin(i.uv.x*42+movement*.19)*2);
                half fine=.55+.45*sin(i.uv.y*93-_Time.y*85+i.uv.x*65);
                half turbulence=lerp(1,lerp(streak,fine,.28),saturate(_Flow));
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(normal,light.direction))*.7+.3;
                half3 illumination=SampleSH(normal)+light.color*diffuse*light.shadowAttenuation;
                half3 halfDir=SafeNormalize(view+light.direction);
                half spec=pow(saturate(dot(normal,halfDir)),96)*light.shadowAttenuation;
                half fresnel=.02+.7*pow(1-saturate(dot(normal,view)),5);
                half3 reflection=GlossyEnvironmentReflection(reflect(-view,normal),i.positionWS,.13,1);
                half3 rgb=_BaseColor.rgb*illumination*.72 + light.color*spec*1.8+reflection*fresnel;
                #ifdef _ADDITIONAL_LIGHTS
                uint count=GetAdditionalLightsCount();
                for(uint k=0;k<count;k++)
                {
                    Light extra=GetAdditionalLight(k,i.positionWS);
                    rgb+=_BaseColor.rgb*extra.color*saturate(dot(normal,extra.direction))*extra.distanceAttenuation*extra.shadowAttenuation*.7;
                }
                #endif
                rgb=MixFog(rgb,i.fog);
                half alpha=saturate(i.color.a*_Opacity*turbulence);
                clip(alpha-.002);
                return half4(rgb,alpha);
            }
            ENDHLSL
        }
    }
}
