Shader "GameJam/Volcano Lake Water"
{
    Properties
    {
        _ShallowColor("Shallow Water", Color) = (0.2, 0.6, 0.55, 0.35)
        _DeepColor("Deep Water", Color) = (0.08, 0.35, 0.46, 0.82)
        _DepthDistance("Depth Color Distance", Range(0.1, 30)) = 9
        _ShoreFade("Shoreline Fade Distance", Range(0.05, 3)) = 0.65
        [Normal] _NormalMap("Ripple Normal", 2D) = "bump" {}
        _WaveTiling("Wave Tiling Per World Unit", Range(0.01, 1)) = 0.12
        _WaveSpeed("Wave Speed", Range(0, 0.2)) = 0.03
        _NormalStrength("Ripple Strength", Range(0, 1)) = 0.5
        _Smoothness("Smoothness", Range(0, 1)) = 0.92
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _SPECULAR_SETUP 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                float _ShoreFade;
                float _WaveTiling;
                float _WaveSpeed;
                half _NormalStrength;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 fogAndVertexLight : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogAndVertexLight = half4(
                    ComputeFogFactor(positions.positionCS.z),
                    VertexLighting(positions.positionWS, output.normalWS));
                output.shadowCoord = GetShadowCoord(positions);
                output.eyeDepth = -positions.positionVS.z;
                return output;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // World-space waves keep their size when the lake plane is resized.
                float2 waveUV = input.positionWS.xz * _WaveTiling;
                float waveTime = _Time.y * _WaveSpeed;
                half3 waveA = UnpackNormalScale(SAMPLE_TEXTURE2D(
                    _NormalMap, sampler_NormalMap,
                    waveUV + waveTime * float2(0.37, 0.19)), _NormalStrength);
                half3 waveB = UnpackNormalScale(SAMPLE_TEXTURE2D(
                    _NormalMap, sampler_NormalMap,
                    waveUV * 1.63 + waveTime * float2(-0.23, 0.31)), _NormalStrength * 0.65h);
                half2 ripple = waveA.xy + waveB.xy;
                half3 normalWS = normalize(input.normalWS + half3(ripple.x, 0, ripple.y));
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                // Scene view can also use an orthographic camera.
                float orthoDepth01 = rawDepth;
                #if UNITY_REVERSED_Z
                    orthoDepth01 = 1.0 - orthoDepth01;
                #endif
                float orthoEyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, orthoDepth01);
                sceneEyeDepth = lerp(sceneEyeDepth, orthoEyeDepth, unity_OrthoParams.w);
                float waterDepth = max(0.0, sceneEyeDepth - input.eyeDepth);
                half depthBlend = saturate(waterDepth / max(_DepthDistance, 0.001));
                half4 waterColor = lerp(_ShallowColor, _DeepColor, depthBlend);
                half shoreline = saturate(waterDepth / max(_ShoreFade, 0.001));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirectionWS)), 5.0h);

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.positionCS = input.positionCS;
                lightingInput.normalWS = normalWS;
                lightingInput.viewDirectionWS = viewDirectionWS;
                lightingInput.shadowCoord = input.shadowCoord;
                lightingInput.fogCoord = input.fogAndVertexLight.x;
                lightingInput.vertexLighting = input.fogAndVertexLight.yzw;
                lightingInput.bakedGI = SampleSH(normalWS);
                lightingInput.normalizedScreenSpaceUV = screenUV;
                lightingInput.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = waterColor.rgb;
                surface.specular = half3(0.035h, 0.035h, 0.035h);
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = lerp(waterColor.a, 1.0h, fresnel * 0.45h) * shoreline;

                // PBR supplies sun highlights and Fresnel-weighted environment reflections.
                half4 color = UniversalFragmentPBR(lightingInput, surface);
                color.rgb = MixFog(color.rgb, lightingInput.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
