// Made with Amplify Shader Editor v1.9.9.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "BK_Lava"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		_Dispacement( "Dispacement", Float ) = 0.1
		_Tiling( "Tiling", Float ) = 1
		_Speed( "Speed", Float ) = 0
		_DepthDistance( "Depth Distance", Float ) = 10
		_EdgeDistance1( "Edge Distance", Float ) = 10
		[Space (10)][Header (Main)][Space (10)] _MainColor( "Main Color", Color ) = ( 1, 1, 1, 1 )
		_Albedo( "Albedo", 2D ) = "white" {}
		[Normal] _Normal( "Normal", 2D ) = "bump" {}
		_Smoothness( "Smoothness", Float ) = 0.5
		[HDR] _EmissiveColor( "Emissive Color", Color ) = ( 1, 1, 1, 1 )
		_Emissive( "Emissive", 2D ) = "white" {}
		_DepthContrast( "Depth Contrast", Float ) = 0
		[Toggle] _InvertDepth( "Invert", Float ) = 1
		[Space (10)][Header (Layer Maps)][Space (10)] _LayerColor( "Layer Color", Color ) = ( 1, 1, 1, 1 )
		_LayerAlbedo( "Albedo", 2D ) = "white" {}
		[Normal] _LayerNormal( "Normal", 2D ) = "bump" {}
		_LayerNormalPower( "Normal Power", Range( 0, 1 ) ) = 1
		_LayerTiling( "Tiling", Float ) = 1
		[Space (10)][Header (Distortion)][Space (10)] _DistortionPower( "Distortion Power", Range( 0, 1 ) ) = 0
		[Normal] _Distortion( "Distortion", 2D ) = "bump" {}
		_DistorsionScale( "Distorsion Scale", Float ) = 1
		_DistortionSpeed( "Distortion Speed", Float ) = 0


		//_TransmissionShadow( "Transmission Shadow", Range( 0, 1 ) ) = 0.5
		//_TransStrength( "Trans Strength", Range( 0, 50 ) ) = 1
		//_TransNormal( "Trans Normal Distortion", Range( 0, 1 ) ) = 0.5
		//_TransScattering( "Trans Scattering", Range( 1, 50 ) ) = 2
		//_TransDirect( "Trans Direct", Range( 0, 1 ) ) = 0.9
		//_TransAmbient( "Trans Ambient", Range( 0, 1 ) ) = 0.1
		//_TransShadow( "Trans Shadow", Range( 0, 1 ) ) = 0.5

		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		//_InstancedTerrainNormals("Instanced Terrain Normals", Float) = 1.0

		[ToggleOff(_SPECULARHIGHLIGHTS_OFF)] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
		[HideInInspector][ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		//[HideInInspector] _AlphaClip("__clip", Float) = 0.0
	}

	SubShader
	{
		LOD 0

		

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Lit" }

		Cull Back
		ZWrite Off
		ZTest LEqual
		Offset 0 , 0
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 4.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#if ( SHADER_TARGET > 35 ) && defined( SHADER_API_GLES3 )
			#error For WebGL2/GLES3, please set your shader target to 3.5 via SubShader options. URP shaders in ASE use target 4.5 by default.
		#endif

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
			#pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fog
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

			
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
		

			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

			
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
           

			

			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile _ _LIGHT_LAYERS
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#pragma multi_compile _ _FORWARD_PLUS

			

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_FORWARD

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			
			#if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_FRAG_COLOR


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 lightmapUVOrVertexSH : TEXCOORD3;
				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					half4 fogFactorAndVertexLight : TEXCOORD4;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					float2 dynamicLightmapUV : TEXCOORD5;
				#endif
				float4 ase_texcoord6 : TEXCOORD6;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;
			sampler2D _Albedo;
			sampler2D _LayerAlbedo;
			sampler2D _Normal;
			sampler2D _LayerNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				
				output.ase_texcoord6.xy = input.texcoord.xy;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord6.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif
				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				#if defined(LIGHTMAP_ON)
					OUTPUT_LIGHTMAP_UV(input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy);
				#else
					OUTPUT_SH(normalInput.normalWS.xyz, output.lightmapUVOrVertexSH.xyz);
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif

				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					output.fogFactorAndVertexLight = 0;
					#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
						output.fogFactorAndVertexLight.x = ComputeFogFactor(vertexInput.positionCS.z);
					#endif
					#ifdef _ADDITIONAL_LIGHTS_VERTEX
						half3 vertexLight = VertexLighting( vertexInput.positionWS, normalInput.normalWS );
						output.fogFactorAndVertexLight.yzw = vertexLight;
					#endif
				#endif

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				float4 ase_color : COLOR;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = input.texcoord1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = input.texcoord2;
				#endif
				output.ase_color = input.ase_color;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				#endif
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						, out float4 outRenderingLayers : SV_Target1
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined( _SURFACE_TYPE_TRANSPARENT )
					const bool isTransparent = true;
				#else
					const bool isTransparent = false;
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWS );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord6.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord6.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2D( _Distortion, panner28 ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 temp_output_23_0 = ( panner3 + temp_output_25_0 );
				float2 temp_cast_2 = (_LayerTiling).xx;
				float2 texCoord93 = input.ase_texcoord6.xy * temp_cast_2 + float2( 0,0 );
				float saferPower87 = abs( ( 1.0 - DepthFade78 ) );
				float temp_output_87_0 = pow( saferPower87 , 20.0 );
				float4 lerpResult89 = lerp( ( _MainColor * ( tex2D( _Albedo, temp_output_23_0 ).r * ( 1.0 - DepthFade78 ) ) ) , ( _LayerColor * tex2D( _LayerAlbedo, texCoord93 ) ) , temp_output_87_0);
				
				float3 unpack84 = UnpackNormalScale( tex2D( _LayerNormal, texCoord93 ), _LayerNormalPower );
				unpack84.z = lerp( 1, unpack84.z, saturate(_LayerNormalPower) );
				float3 lerpResult94 = lerp( UnpackNormalScale( tex2D( _Normal, temp_output_23_0 ), 1.0f ) , unpack84 , temp_output_87_0);
				
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float2 temp_output_10_0_g2 = ddx( Input_UV145_g2 );
				float2 temp_output_12_0_g2 = ddy( Input_UV145_g2 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar59_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar60_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				float saferPower20 = abs( temp_output_58_0 );
				float saferPower15 = abs( ( DepthFade78 * (( _InvertDepth )?( input.ase_color.g ):( ( 1.0 - input.ase_color.r ) )) ) );
				float temp_output_15_0 = pow( saferPower15 , ( 1.0 - _DepthContrast ) );
				float4 color96 = IsGammaSpace() ? float4( 0, 0, 0, 0 ) : float4( 0, 0, 0, 0 );
				float3 lerpResult90 = lerp( ( _EmissiveColor.rgb * ( pow( saferPower20 , temp_output_15_0 ) * saturate( temp_output_15_0 ) ) ) , color96.rgb , ( temp_output_87_0 * temp_output_87_0 ));
				
				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float3 BaseColor = lerpResult89.rgb;
				float3 Normal = lerpResult94;
				float3 Specular = 0.5;
				float Metallic = 0;
				float Smoothness = _Smoothness;
				float Occlusion = 1;
				float3 Emission = lerpResult90;
				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif
				float3 BakedGI = 0;
				float3 RefractionColor = 1;
				float RefractionIndex = 1;
				float3 Transmission = 1;
				float3 Translucency = 1;

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _CLEARCOAT
					float CoatMask = 0;
					float CoatSmoothness = 0;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.viewDirectionWS = ViewDirWS;
				inputData.shadowCoord = ShadowCoord;

				#ifdef _NORMALMAP
						#if _NORMAL_DROPOFF_TS
							inputData.normalWS = TransformTangentToWorld(Normal, half3x3(TangentWS, BitangentWS, NormalWS));
						#elif _NORMAL_DROPOFF_OS
							inputData.normalWS = TransformObjectToWorldNormal(Normal);
						#elif _NORMAL_DROPOFF_WS
							inputData.normalWS = Normal;
						#endif
					inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
				#else
					inputData.normalWS = NormalWS;
				#endif

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.fogFactorAndVertexLight.x);
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
					inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				#endif

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float3 SH = SampleSH(inputData.normalWS.xyz);
				#else
					float3 SH = input.lightmapUVOrVertexSH.xyz;
				#endif

				#if defined(DYNAMICLIGHTMAP_ON)
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, SH, inputData.normalWS);
				#else
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, SH, inputData.normalWS);
				#endif

				#ifdef ASE_BAKEDGI
					inputData.bakedGI = BakedGI;
				#endif

				inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);

				#if defined(DEBUG_DISPLAY)
					#if defined(DYNAMICLIGHTMAP_ON)
						inputData.dynamicLightmapUV = input.dynamicLightmapUV.xy;
					#endif
					#if defined(LIGHTMAP_ON)
						inputData.staticLightmapUV = input.lightmapUVOrVertexSH.xy;
					#else
						inputData.vertexSH = SH;
					#endif
				#endif

				SurfaceData surfaceData;
				surfaceData.albedo              = BaseColor;
				surfaceData.metallic            = saturate(Metallic);
				surfaceData.specular            = Specular;
				surfaceData.smoothness          = saturate(Smoothness),
				surfaceData.occlusion           = Occlusion,
				surfaceData.emission            = Emission,
				surfaceData.alpha               = saturate(Alpha);
				surfaceData.normalTS            = Normal;
				surfaceData.clearCoatMask       = 0;
				surfaceData.clearCoatSmoothness = 1;

				#ifdef _CLEARCOAT
					surfaceData.clearCoatMask       = saturate(CoatMask);
					surfaceData.clearCoatSmoothness = saturate(CoatSmoothness);
				#endif

				#if defined(_DBUFFER)
					ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
				#endif

				#ifdef ASE_LIGHTING_SIMPLE
					half4 color = UniversalFragmentBlinnPhong( inputData, surfaceData);
				#else
					half4 color = UniversalFragmentPBR( inputData, surfaceData);
				#endif

				#ifdef ASE_TRANSMISSION
				{
					float shadow = _TransmissionShadow;

					#define SUM_LIGHT_TRANSMISSION(Light)\
						float3 atten = Light.color * Light.distanceAttenuation;\
						atten = lerp( atten, atten * Light.shadowAttenuation, shadow );\
						half3 transmission = max( 0, -dot( inputData.normalWS, Light.direction ) ) * atten * Transmission;\
						color.rgb += BaseColor * transmission;

					SUM_LIGHT_TRANSMISSION( GetMainLight( inputData.shadowCoord ) );

					#if defined(_ADDITIONAL_LIGHTS)
						uint meshRenderingLayers = GetMeshRenderingLayer();
						uint pixelLightCount = GetAdditionalLightsCount();
						#if USE_FORWARD_PLUS
							for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
							{
								FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

								Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
								#ifdef _LIGHT_LAYERS
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								#endif
								{
									SUM_LIGHT_TRANSMISSION( light );
								}
							}
						#endif
						LIGHT_LOOP_BEGIN( pixelLightCount )
							Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
							#ifdef _LIGHT_LAYERS
							if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
							#endif
							{
								SUM_LIGHT_TRANSMISSION( light );
							}
						LIGHT_LOOP_END
					#endif
				}
				#endif

				#ifdef ASE_TRANSLUCENCY
				{
					float shadow = _TransShadow;
					float normal = _TransNormal;
					float scattering = _TransScattering;
					float direct = _TransDirect;
					float ambient = _TransAmbient;
					float strength = _TransStrength;

					#define SUM_LIGHT_TRANSLUCENCY(Light)\
						float3 atten = Light.color * Light.distanceAttenuation;\
						atten = lerp( atten, atten * Light.shadowAttenuation, shadow );\
						half3 lightDir = Light.direction + inputData.normalWS * normal;\
						half VdotL = pow( saturate( dot( inputData.viewDirectionWS, -lightDir ) ), scattering );\
						half3 translucency = atten * ( VdotL * direct + inputData.bakedGI * ambient ) * Translucency;\
						color.rgb += BaseColor * translucency * strength;

					SUM_LIGHT_TRANSLUCENCY( GetMainLight( inputData.shadowCoord ) );

					#if defined(_ADDITIONAL_LIGHTS)
						uint meshRenderingLayers = GetMeshRenderingLayer();
						uint pixelLightCount = GetAdditionalLightsCount();
						#if USE_FORWARD_PLUS
							for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
							{
								FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

								Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
								#ifdef _LIGHT_LAYERS
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								#endif
								{
									SUM_LIGHT_TRANSLUCENCY( light );
								}
							}
						#endif
						LIGHT_LOOP_BEGIN( pixelLightCount )
							Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
							#ifdef _LIGHT_LAYERS
							if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
							#endif
							{
								SUM_LIGHT_TRANSLUCENCY( light );
							}
						LIGHT_LOOP_END
					#endif
				}
				#endif

				#ifdef ASE_REFRACTION
					float4 projScreenPos = ScreenPos / ScreenPos.w;
					float3 refractionOffset = ( RefractionIndex - 1.0 ) * mul( UNITY_MATRIX_V, float4( NormalWS,0 ) ).xyz * ( 1.0 - dot( NormalWS, ViewDirWS ) );
					projScreenPos.xy += refractionOffset.xy;
					float3 refraction = SHADERGRAPH_SAMPLE_SCENE_COLOR( projScreenPos.xy ) * RefractionColor;
					color.rgb = lerp( refraction, color.rgb, color.a );
					color.a = 1;
				#endif

				#ifdef ASE_FINAL_COLOR_ALPHA_MULTIPLY
					color.rgb *= color.a;
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						color.rgb = MixFogColor(color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						color.rgb = MixFog(color.rgb, inputData.fogCoord);
					#endif
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
				#endif

				#if defined( ASE_OPAQUE_KEEP_ALPHA )
					return half4( color.rgb, color.a );
				#else
					return half4( color.rgb, OutputAlpha( color.a, isTransparent ) );
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }

			ZWrite On
			ZTest LEqual
			AlphaToMask Off
			ColorMask 0

			HLSLPROGRAM

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma multi_compile_instancing
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_SHADOWCASTER

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;


			float3 _LightDirection;
			float3 _LightPosition;

			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input )
			{
				PackedVaryings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( output );

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );
				float3 normalWS = TransformObjectToWorldDir(input.normalOS);

				#if _CASTING_PUNCTUAL_LIGHT_SHADOW
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif

				float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

				#if UNITY_REVERSED_Z
					positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
					positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif

				output.positionCS = positionCS;
				output.positionWS = positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(	PackedVaryings input
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					#if defined( _ALPHATEST_SHADOW_ON )
						AlphaDiscard( Alpha, AlphaClipThresholdShadow );
					#else
						AlphaDiscard( Alpha, AlphaClipThreshold );
					#endif
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask R
			AlphaToMask Off

			HLSLPROGRAM

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma multi_compile_instancing
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_DEPTHONLY

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(	PackedVaryings input
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "Meta"
			Tags { "LightMode"="Meta" }

			Cull Off

			HLSLPROGRAM
			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1

			#pragma shader_feature EDITOR_VISUALIZATION

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_META

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_COLOR


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				#ifdef EDITOR_VISUALIZATION
					float4 VizUV : TEXCOORD1;
					float4 LightCoord : TEXCOORD2;
				#endif
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;
			sampler2D _Albedo;
			sampler2D _LayerAlbedo;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				
				output.ase_texcoord4 = screenPos;
				
				output.ase_texcoord3.xy = input.texcoord.xy;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord3.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				#ifdef EDITOR_VISUALIZATION
					float2 VizUV = 0;
					float4 LightCoord = 0;
					UnityEditorVizData(input.positionOS.xyz, input.texcoord.xy, input.texcoord1.xy, input.texcoord2.xy, VizUV, LightCoord);
					output.VizUV = float4(VizUV, 0, 0);
					output.LightCoord = LightCoord;
				#endif

				output.positionCS = MetaVertexPosition( input.positionOS, input.texcoord1.xy, input.texcoord1.xy, unity_LightmapST, unity_DynamicLightmapST );
				output.positionWS = TransformObjectToWorld( input.positionOS.xyz );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				output.texcoord1 = input.texcoord1;
				output.texcoord2 = input.texcoord2;
				output.ase_color = input.ase_color;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord3.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord3.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 screenPos = input.ase_texcoord4;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2D( _Distortion, panner28 ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 temp_output_23_0 = ( panner3 + temp_output_25_0 );
				float2 temp_cast_2 = (_LayerTiling).xx;
				float2 texCoord93 = input.ase_texcoord3.xy * temp_cast_2 + float2( 0,0 );
				float saferPower87 = abs( ( 1.0 - DepthFade78 ) );
				float temp_output_87_0 = pow( saferPower87 , 20.0 );
				float4 lerpResult89 = lerp( ( _MainColor * ( tex2D( _Albedo, temp_output_23_0 ).r * ( 1.0 - DepthFade78 ) ) ) , ( _LayerColor * tex2D( _LayerAlbedo, texCoord93 ) ) , temp_output_87_0);
				
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float2 temp_output_10_0_g2 = ddx( Input_UV145_g2 );
				float2 temp_output_12_0_g2 = ddy( Input_UV145_g2 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar59_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar60_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				float saferPower20 = abs( temp_output_58_0 );
				float saferPower15 = abs( ( DepthFade78 * (( _InvertDepth )?( input.ase_color.g ):( ( 1.0 - input.ase_color.r ) )) ) );
				float temp_output_15_0 = pow( saferPower15 , ( 1.0 - _DepthContrast ) );
				float4 color96 = IsGammaSpace() ? float4( 0, 0, 0, 0 ) : float4( 0, 0, 0, 0 );
				float3 lerpResult90 = lerp( ( _EmissiveColor.rgb * ( pow( saferPower20 , temp_output_15_0 ) * saturate( temp_output_15_0 ) ) ) , color96.rgb , ( temp_output_87_0 * temp_output_87_0 ));
				
				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float3 BaseColor = lerpResult89.rgb;
				float3 Emission = lerpResult90;
				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				MetaInput metaInput = (MetaInput)0;
				metaInput.Albedo = BaseColor;
				metaInput.Emission = Emission;
				#ifdef EDITOR_VISUALIZATION
					metaInput.VizUV = input.VizUV.xy;
					metaInput.LightCoord = input.LightCoord;
				#endif

				return UnityMetaFragment(metaInput);
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "Universal2D"
			Tags { "LightMode"="Universal2D" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			HLSLPROGRAM

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_2D

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;
			sampler2D _Albedo;
			sampler2D _LayerAlbedo;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_TRANSFER_INSTANCE_ID( input, output );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( output );

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				
				output.ase_texcoord2 = screenPos;
				
				output.ase_texcoord1.xy = input.ase_texcoord.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord1.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord1.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord1.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 screenPos = input.ase_texcoord2;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2D( _Distortion, panner28 ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 temp_output_23_0 = ( panner3 + temp_output_25_0 );
				float2 temp_cast_2 = (_LayerTiling).xx;
				float2 texCoord93 = input.ase_texcoord1.xy * temp_cast_2 + float2( 0,0 );
				float saferPower87 = abs( ( 1.0 - DepthFade78 ) );
				float temp_output_87_0 = pow( saferPower87 , 20.0 );
				float4 lerpResult89 = lerp( ( _MainColor * ( tex2D( _Albedo, temp_output_23_0 ).r * ( 1.0 - DepthFade78 ) ) ) , ( _LayerColor * tex2D( _LayerAlbedo, texCoord93 ) ) , temp_output_87_0);
				
				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float3 BaseColor = lerpResult89.rgb;
				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				half4 color = half4(BaseColor, Alpha );

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				return color;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthNormals"
			Tags { "LightMode"="DepthNormals" }

			ZWrite On
			Blend One Zero
			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

			

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma multi_compile_instancing
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY
			//#define SHADERPASS SHADERPASS_DEPTHNORMALS

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			
			#if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				half4 texcoord : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;
			sampler2D _Normal;
			sampler2D _LayerNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				
				output.ase_texcoord3.xy = input.texcoord.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord3.zw = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			void frag(	PackedVaryings input
						, out half4 outNormalWS : SV_Target0
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						, out float4 outRenderingLayers : SV_Target1
						#endif
						 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord3.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord3.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2D( _Distortion, panner28 ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 temp_output_23_0 = ( panner3 + temp_output_25_0 );
				float2 temp_cast_2 = (_LayerTiling).xx;
				float2 texCoord93 = input.ase_texcoord3.xy * temp_cast_2 + float2( 0,0 );
				float3 unpack84 = UnpackNormalScale( tex2D( _LayerNormal, texCoord93 ), _LayerNormalPower );
				unpack84.z = lerp( 1, unpack84.z, saturate(_LayerNormalPower) );
				float saferPower87 = abs( ( 1.0 - DepthFade78 ) );
				float temp_output_87_0 = pow( saferPower87 , 20.0 );
				float3 lerpResult94 = lerp( UnpackNormalScale( tex2D( _Normal, temp_output_23_0 ), 1.0f ) , unpack84 , temp_output_87_0);
				
				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float3 Normal = lerpResult94;
				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				#if defined(_GBUFFER_NORMALS_OCT)
					float2 octNormalWS = PackNormalOctQuadEncode(NormalWS);
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
					outNormalWS = half4(packedNormalWS, 0.0);
				#else
					#if defined(_NORMALMAP)
						#if _NORMAL_DROPOFF_TS
							float3 normalWS = TransformTangentToWorld(Normal, half3x3(TangentWS, BitangentWS, NormalWS));
						#elif _NORMAL_DROPOFF_OS
							float3 normalWS = TransformObjectToWorldNormal(Normal);
						#elif _NORMAL_DROPOFF_WS
							float3 normalWS = Normal;
						#endif
					#else
						float3 normalWS = NormalWS;
					#endif
					outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "GBuffer"
			Tags { "LightMode"="UniversalGBuffer" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA
			

			HLSLPROGRAM

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
			#pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fog
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			// Deferred Rendering Path does not support the OpenGL-based graphics API:
			// Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
			#pragma exclude_renderers glcore gles3 

			

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

			
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
           

			

			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

			

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_GBUFFER

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			
			#if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_FRAG_COLOR


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 lightmapUVOrVertexSH : TEXCOORD3;
				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					half4 fogFactorAndVertexLight : TEXCOORD4;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					float2 dynamicLightmapUV : TEXCOORD5;
				#endif
				float4 ase_texcoord6 : TEXCOORD6;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;
			sampler2D _Albedo;
			sampler2D _LayerAlbedo;
			sampler2D _Normal;
			sampler2D _LayerNormal;


			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				
				output.ase_texcoord6.xy = input.texcoord.xy;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord6.zw = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				#if defined(LIGHTMAP_ON)
					OUTPUT_LIGHTMAP_UV(input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy);
				#else
					OUTPUT_SH(normalInput.normalWS.xyz, output.lightmapUVOrVertexSH.xyz);
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif

				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					output.fogFactorAndVertexLight = 0;
					#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
						// @diogo: no fog applied in GBuffer
					#endif
					#ifdef _ADDITIONAL_LIGHTS_VERTEX
						half3 vertexLight = VertexLighting( vertexInput.positionWS, normalInput.normalWS );
						output.fogFactorAndVertexLight.yzw = vertexLight;
					#endif
				#endif

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				float4 ase_color : COLOR;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = input.texcoord1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = input.texcoord2;
				#endif
				output.ase_color = input.ase_color;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				#endif
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			FragmentOutput frag ( PackedVaryings input
								#if defined( ASE_DEPTH_WRITE_ON )
								,out float outputDepth : ASE_SV_DEPTH
								#endif
								 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWS );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord6.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord6.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2D( _Distortion, panner28 ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 temp_output_23_0 = ( panner3 + temp_output_25_0 );
				float2 temp_cast_2 = (_LayerTiling).xx;
				float2 texCoord93 = input.ase_texcoord6.xy * temp_cast_2 + float2( 0,0 );
				float saferPower87 = abs( ( 1.0 - DepthFade78 ) );
				float temp_output_87_0 = pow( saferPower87 , 20.0 );
				float4 lerpResult89 = lerp( ( _MainColor * ( tex2D( _Albedo, temp_output_23_0 ).r * ( 1.0 - DepthFade78 ) ) ) , ( _LayerColor * tex2D( _LayerAlbedo, texCoord93 ) ) , temp_output_87_0);
				
				float3 unpack84 = UnpackNormalScale( tex2D( _LayerNormal, texCoord93 ), _LayerNormalPower );
				unpack84.z = lerp( 1, unpack84.z, saturate(_LayerNormalPower) );
				float3 lerpResult94 = lerp( UnpackNormalScale( tex2D( _Normal, temp_output_23_0 ), 1.0f ) , unpack84 , temp_output_87_0);
				
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float2 temp_output_10_0_g2 = ddx( Input_UV145_g2 );
				float2 temp_output_12_0_g2 = ddy( Input_UV145_g2 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar59_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar60_g292 ) + ( tex2D( _Emissive, ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), temp_output_10_0_g2, temp_output_12_0_g2 ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				float saferPower20 = abs( temp_output_58_0 );
				float saferPower15 = abs( ( DepthFade78 * (( _InvertDepth )?( input.ase_color.g ):( ( 1.0 - input.ase_color.r ) )) ) );
				float temp_output_15_0 = pow( saferPower15 , ( 1.0 - _DepthContrast ) );
				float4 color96 = IsGammaSpace() ? float4( 0, 0, 0, 0 ) : float4( 0, 0, 0, 0 );
				float3 lerpResult90 = lerp( ( _EmissiveColor.rgb * ( pow( saferPower20 , temp_output_15_0 ) * saturate( temp_output_15_0 ) ) ) , color96.rgb , ( temp_output_87_0 * temp_output_87_0 ));
				
				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				float3 BaseColor = lerpResult89.rgb;
				float3 Normal = lerpResult94;
				float3 Specular = 0.5;
				float Metallic = 0;
				float Smoothness = _Smoothness;
				float Occlusion = 1;
				float3 Emission = lerpResult90;
				float Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif
				float3 BakedGI = 0;
				float3 RefractionColor = 1;
				float RefractionIndex = 1;
				float3 Transmission = 1;
				float3 Translucency = 1;

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.shadowCoord = ShadowCoord;

				#ifdef _NORMALMAP
					#if _NORMAL_DROPOFF_TS
						inputData.normalWS = TransformTangentToWorld(Normal, half3x3( TangentWS, BitangentWS, NormalWS ));
					#elif _NORMAL_DROPOFF_OS
						inputData.normalWS = TransformObjectToWorldNormal(Normal);
					#elif _NORMAL_DROPOFF_WS
						inputData.normalWS = Normal;
					#endif
				#else
					inputData.normalWS = NormalWS;
				#endif

				inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
				inputData.viewDirectionWS = SafeNormalize( ViewDirWS );

				#ifdef ASE_FOG
					// @diogo: no fog applied in GBuffer
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
					inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				#endif

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float3 SH = SampleSH(inputData.normalWS.xyz);
				#else
					float3 SH = input.lightmapUVOrVertexSH.xyz;
				#endif

				#if defined(DYNAMICLIGHTMAP_ON)
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, SH, inputData.normalWS);
				#else
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, SH, inputData.normalWS);
				#endif

				#ifdef ASE_BAKEDGI
					inputData.bakedGI = BakedGI;
				#endif

				inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);

				#if defined(DEBUG_DISPLAY)
					#if defined(DYNAMICLIGHTMAP_ON)
						inputData.dynamicLightmapUV = input.dynamicLightmapUV.xy;
						#endif
					#if defined(LIGHTMAP_ON)
						inputData.staticLightmapUV = input.lightmapUVOrVertexSH.xy;
					#else
						inputData.vertexSH = SH;
					#endif
				#endif

				#ifdef _DBUFFER
					ApplyDecal(input.positionCS,
						BaseColor,
						Specular,
						inputData.normalWS,
						Metallic,
						Occlusion,
						Smoothness);
				#endif

				BRDFData brdfData;
				InitializeBRDFData(BaseColor, Metallic, Specular, Smoothness, Alpha, brdfData);

				Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
				half4 color;
				MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
				color.rgb = GlobalIllumination(brdfData, inputData.bakedGI, Occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS);
				color.a = Alpha;

				#ifdef ASE_FINAL_COLOR_ALPHA_MULTIPLY
					color.rgb *= color.a;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				return BRDFDataToGbuffer(brdfData, inputData, Smoothness, Emission + color.rgb, Occlusion);
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "SceneSelectionPass"
			Tags { "LightMode"="SceneSelectionPass" }

			Cull Off
			AlphaToMask Off

			HLSLPROGRAM

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SCENESELECTIONPASS 1

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag( PackedVaryings input
				#if defined( ASE_DEPTH_WRITE_ON )
				,out float outputDepth : ASE_SV_DEPTH
				#endif
				 ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;

				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				surfaceDescription.Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				return half4( _ObjectId, _PassValue, 1.0, 1.0 );
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "ScenePickingPass"
			Tags { "LightMode"="Picking" }

			AlphaToMask Off

			HLSLPROGRAM

			

			#define ASE_GEOMETRY
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define _EMISSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19908
			#define ASE_SRP_VERSION 140008
			#define REQUIRE_DEPTH_TEXTURE 1


			

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

		    #define SCENEPICKINGPASS 1

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"

			

			

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"

			
            #if ASE_SRP_VERSION >=140007
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#endif
		

			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainColor;
			float4 _LayerColor;
			float4 _EmissiveColor;
			float _Speed;
			float _Tiling;
			float _DistortionSpeed;
			float _DistorsionScale;
			float _DistortionPower;
			float _DepthDistance;
			float _Dispacement;
			float _LayerTiling;
			float _LayerNormalPower;
			float _Smoothness;
			float _InvertDepth;
			float _DepthContrast;
			float _EdgeDistance1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _Emissive;
			sampler2D _Distortion;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 appendResult10 = (float2(0.0 , _Speed));
				float2 temp_cast_0 = (_Tiling).xx;
				float2 texCoord9 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				float2 panner3 = ( 1.0 * _Time.y * appendResult10 + texCoord9);
				float2 appendResult30 = (float2(0.0 , _DistortionSpeed));
				float2 temp_cast_1 = (_DistorsionScale).xx;
				float2 texCoord32 = input.ase_texcoord.xy * temp_cast_1 + float2( 0,0 );
				float2 panner28 = ( 1.0 * _Time.y * appendResult30 + texCoord32);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth43 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth43 = abs( ( screenDepth43 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _DepthDistance ) );
				float saferPower50 = abs( distanceDepth43 );
				float DepthFade78 = saturate( pow( saferPower50 , 2.5 ) );
				float3 unpack22 = UnpackNormalScale( tex2Dlod( _Distortion, float4( panner28, 0, 0.0) ), ( _DistortionPower * DepthFade78 ) );
				unpack22.z = lerp( 1, unpack22.z, saturate(( _DistortionPower * DepthFade78 )) );
				float2 temp_output_25_0 = (unpack22).xy;
				float2 Input_UV145_g2 = ( ( panner3 / float2( 2.7,2.7 ) ) + temp_output_25_0 );
				float2 UV100_g292 = Input_UV145_g2;
				float2 temp_output_51_0_g292 = mul( float2x2( 1, 0, -0.5773503, 1.154701 ), ( UV100_g292 * float2( 3.464,3.464 ) ) );
				float2 break55_g292 = frac( temp_output_51_0_g292 );
				float temp_output_56_0_g292 = ( ( 1.0 - break55_g292.x ) - break55_g292.y );
				float2 temp_output_52_0_g292 = floor( temp_output_51_0_g292 );
				float2 temp_output_125_0_g292 = ( temp_output_52_0_g292 + float2( 1,1 ) );
				float2 ifLocalVar87_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar87_g292 = temp_output_52_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar87_g292 = temp_output_125_0_g292;
				float3 temp_output_7_0_g293 = frac( ( (ifLocalVar87_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g293 = dot( temp_output_7_0_g293 , ( (temp_output_7_0_g293).yzx + 33.33 ) );
				float3 temp_output_12_0_g293 = ( temp_output_7_0_g293 + dotResult8_g293 );
				float temp_output_65_0_g292 = ( 0.0 - temp_output_56_0_g292 );
				float ifLocalVar59_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar59_g292 = temp_output_65_0_g292;
				else
				ifLocalVar59_g292 = temp_output_56_0_g292;
				float2 temp_output_90_0_g292 = ( temp_output_52_0_g292 + float2( 0,1 ) );
				float2 temp_output_123_0_g292 = ( temp_output_52_0_g292 + float2( 1,0 ) );
				float2 ifLocalVar88_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar88_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar88_g292 = temp_output_123_0_g292;
				float3 temp_output_7_0_g294 = frac( ( (ifLocalVar88_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g294 = dot( temp_output_7_0_g294 , ( (temp_output_7_0_g294).yzx + 33.33 ) );
				float3 temp_output_12_0_g294 = ( temp_output_7_0_g294 + dotResult8_g294 );
				float temp_output_66_0_g292 = ( 1.0 - break55_g292.y );
				float ifLocalVar60_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar60_g292 = temp_output_66_0_g292;
				else
				ifLocalVar60_g292 = break55_g292.y;
				float2 ifLocalVar89_g292 = 0;
				if( temp_output_56_0_g292 > 0.0 )
				ifLocalVar89_g292 = temp_output_123_0_g292;
				else if( temp_output_56_0_g292 == 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				else if( temp_output_56_0_g292 < 0.0 )
				ifLocalVar89_g292 = temp_output_90_0_g292;
				float3 temp_output_7_0_g295 = frac( ( (ifLocalVar89_g292).xyx * float3( 0.1031, 0.103, 0.0973 ) ) );
				float dotResult8_g295 = dot( temp_output_7_0_g295 , ( (temp_output_7_0_g295).yzx + 33.33 ) );
				float3 temp_output_12_0_g295 = ( temp_output_7_0_g295 + dotResult8_g295 );
				float temp_output_67_0_g292 = ( 1.0 - break55_g292.x );
				float ifLocalVar61_g292 = 0;
				if( temp_output_56_0_g292 <= 0.0 )
				ifLocalVar61_g292 = temp_output_67_0_g292;
				else
				ifLocalVar61_g292 = break55_g292.x;
				float4 Output_2D293_g2 = ( ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g293).xx + (temp_output_12_0_g293).yz ) * (temp_output_12_0_g293).zy ) ) ), 0, 0.0) ) * ifLocalVar59_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g294).xx + (temp_output_12_0_g294).yz ) * (temp_output_12_0_g294).zy ) ) ), 0, 0.0) ) * ifLocalVar60_g292 ) + ( tex2Dlod( _Emissive, float4( ( UV100_g292 + frac( ( ( (temp_output_12_0_g295).xx + (temp_output_12_0_g295).yz ) * (temp_output_12_0_g295).zy ) ) ), 0, 0.0) ) * ifLocalVar61_g292 ) );
				float4 break31_g2 = Output_2D293_g2;
				float temp_output_112_32 = break31_g2.r;
				float temp_output_58_0 = ( temp_output_112_32 * temp_output_112_32 );
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( temp_output_58_0 * _Dispacement ) * input.normalOS );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag( PackedVaryings input
				#if defined( ASE_DEPTH_WRITE_ON )
				,out float outputDepth : ASE_SV_DEPTH
				#endif
				 ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;

				float screenDepth118 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth118 = abs( ( screenDepth118 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _EdgeDistance1 ) );
				float saferPower119 = abs( distanceDepth118 );
				float EdgeFade121 = saturate( pow( saferPower119 , 2.5 ) );
				

				surfaceDescription.Alpha = EdgeFade121;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				return unity_SelectionID;
			}
			ENDHLSL
		}
		
	}
	
	CustomEditor "UnityEditor.ShaderGraphLitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}

/*ASEBEGIN
Version=19908
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;104;-2784,-2048;Inherit;False;1124;186.3334;Depth Fade;5;44;43;50;49;78;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;44;-2736,-1984;Inherit;False;Property;_DepthDistance;Depth Distance;3;0;Create;True;0;0;0;False;0;False;10;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DepthFade, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;43;-2496,-2000;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;50;-2240,-2000;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;2.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;108;-4194,126;Inherit;False;1276.667;562.6667;Distortion;9;29;31;32;30;79;27;28;65;22;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;49;-2064,-2000;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;29;-4144,352;Inherit;False;Property;_DistortionSpeed;Distortion Speed;22;0;Create;True;0;0;0;False;0;False;0;0.05;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;78;-1904,-2000;Inherit;False;DepthFade;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;31;-4144,240;Inherit;False;Property;_DistorsionScale;Distorsion Scale;21;0;Create;True;0;0;0;False;0;False;1;2.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;110;-3840,-688;Inherit;False;644;346.3333;Speed & Direction;5;4;18;10;9;3;;1,1,1,1;0;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;32;-3904,176;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;30;-3840,320;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;79;-3568,576;Inherit;False;78;DepthFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;27;-3648,464;Inherit;False;Property;_DistortionPower;Distortion Power;19;0;Create;True;0;0;0;False;3;Space (10);Header (Distortion);Space (10);False;0;0.025;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;4;-3728,-464;Inherit;False;Property;_Speed;Speed;2;0;Create;True;0;0;0;False;0;False;0;0.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;18;-3792,-592;Inherit;False;Property;_Tiling;Tiling;1;0;Create;True;0;0;0;False;0;False;1;3.87;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;28;-3664,272;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;65;-3360,496;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;10;-3584,-480;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;9;-3648,-640;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;22;-3232,256;Inherit;True;Property;_Distortion;Distortion;20;1;[Normal];Create;True;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;116;-2784,-2272;Inherit;False;1124;186.3334;Edge Fade;5;121;120;119;118;117;;1,1,1,1;0;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;3;-3408,-576;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;107;-2560,128;Inherit;False;893.1921;506.9097;Emissive;3;58;53;112;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;52;-3136,-176;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;2.7,2.7;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ComponentMaskNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;25;-2944,-112;Inherit;False;True;True;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;117;-2736,-2208;Inherit;False;Property;_EdgeDistance1;Edge Distance;4;0;Create;True;0;0;0;False;0;False;10;0.25;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;57;-2656,-176;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TexturePropertyNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;53;-2528,320;Inherit;True;Property;_Emissive;Emissive;10;0;Create;True;0;0;0;False;0;False;None;None;False;white;Auto;Texture2D;False;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.DepthFade, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;118;-2496,-2224;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;112;-2176,320;Inherit;False;Procedural Sample;-1;;2;f5379ff72769e2b4495e5ce2f004d8d4;2,157,0,315,0;7;82;SAMPLER2D;0;False;158;SAMPLER2DARRAY;0;False;74;SAMPLERSTATE;0;False;183;FLOAT;0;False;5;FLOAT2;0,0;False;80;FLOAT3;0,0,0;False;104;FLOAT2;1,1;False;5;COLOR;0;FLOAT;32;FLOAT;33;FLOAT;34;FLOAT;35
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;119;-2240,-2224;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;2.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;58;-1904,320;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;120;-2064,-2224;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;128;-800,1040;Inherit;False;Property;_Dispacement;Dispacement;0;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;109;-2656,768;Inherit;False;996;402.6666;Depth Contrast;8;40;42;82;16;33;46;15;12;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;105;-2592,-1728;Inherit;False;924.6667;826.3333;Main Maps;9;86;92;91;93;101;87;83;84;85;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;106;-2304,-768;Inherit;False;638.8645;637.531;Layer Maps;5;80;72;71;103;1;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;121;-1904,-2224;Inherit;False;EdgeFade;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;127;-608,976;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.NormalVertexDataNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;125;-640,1152;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VertexColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;12;-2592,896;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;40;-2416,896;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;82;-2240,816;Inherit;False;78;DepthFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;16;-2240,1056;Inherit;False;Property;_DepthContrast;Depth Contrast;11;0;Create;True;0;0;0;False;0;False;0;1.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ToggleSwitchNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;42;-2272,896;Inherit;False;Property;_InvertDepth;Invert;12;0;Create;False;0;0;0;False;0;False;1;True;Create;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;33;-2032,1056;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;46;-2032,848;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;-1824,944;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;23;-2656,-576;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;80;-2256,-272;Inherit;False;78;DepthFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;92;-2544,-1472;Inherit;False;Property;_LayerTiling;Tiling;18;0;Create;False;0;0;0;False;0;False;1;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;86;-2224,-1040;Inherit;False;78;DepthFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;20;-1536,896;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;19;-1536,1024;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;72;-2064,-272;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1;-2192,-528;Inherit;True;Property;_Albedo;Albedo;6;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;93;-2384,-1488;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;91;-2016,-1040;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;14;-1344,896;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;101;-2400,-1296;Inherit;False;Property;_LayerNormalPower;Normal Power;16;0;Create;False;0;0;0;False;0;False;1;0.5;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;71;-1872,-400;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;5;-1280,-768;Inherit;False;Property;_MainColor;Main Color;5;0;Create;True;0;0;0;False;3;Space (10);Header (Main);Space (10);False;1,1,1,1;1,0,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;83;-1984,-1680;Inherit;True;Property;_LayerAlbedo;Albedo;14;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;114;-1280,-1808;Inherit;False;Property;_LayerColor;Layer Color;13;0;Create;True;0;0;0;False;3;Space (10);Header (Layer Maps);Space (10);False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;6;-1408,128;Inherit;False;Property;_EmissiveColor;Emissive Color;9;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;10.40665,0.7627915,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;87;-1856,-1040;Inherit;False;True;2;0;FLOAT;50;False;1;FLOAT;20;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;115;-1632,-928;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;84;-1984,-1472;Inherit;True;Property;_LayerNormal;Normal;15;1;[Normal];Create;False;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;103;-2192,-720;Inherit;True;Property;_Normal;Normal;7;1;[Normal];Create;True;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;2;-1024,-768;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;113;-1024,-1664;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;7;-1152,128;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;96;-1152,256;Inherit;False;Constant;_Color0;Color 0;19;0;Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;85;-1984,-1264;Inherit;True;Property;_LayerMask;Mask;17;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;89;-768,-1280;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;94;-768,-1072;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;90;-896,128;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;122;0,256;Inherit;False;121;EdgeFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;126;-368,1056;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;139;-16,144;Inherit;False;Property;_Smoothness;Smoothness;8;0;Create;True;0;0;0;False;0;False;0.5;0.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;129;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;ExtraPrePass;0;0;ExtraPrePass;6;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;0;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;130;256,80;Float;False;True;-1;3;UnityEditor.ShaderGraphLitGUI;0;15;BK_Lava;94348b07e5e8bab40bd6c8a1e3df54cd;True;Forward;0;1;Forward;21;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=UniversalForward;False;False;0;;0;0;Standard;48;Category;0;0;  Instanced Terrain Normals;1;0;Lighting Model;0;0;Workflow;1;0;Surface;1;639202552631206647;  Keep Alpha;0;0;  Refraction Model;0;0;  Blend;0;0;Two Sided;1;0;Alpha Clipping;0;0;  Use Shadow Threshold;0;0;Fragment Normal Space;0;0;Forward Only;0;0;Transmission;0;0;  Transmission Shadow;0.5,False,;0;Translucency;0;0;  Translucency Strength;1,False,;0;  Normal Distortion;0.5,False,;0;  Scattering;2,False,;0;  Direct;0.9,False,;0;  Ambient;0.1,False,;0;  Shadow;0.5,False,;0;Cast Shadows;1;0;Receive Shadows;2;0;Specular Highlights;2;0;Environment Reflections;2;0;Receive SSAO;1;0;GPU Instancing;1;0;LOD CrossFade;1;0;Built-in Fog;1;0;_FinalColorxAlpha;0;0;Meta Pass;1;0;Override Baked GI;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Write Depth;0;0;  Early Z;0;0;Vertex Position;1;0;Debug Display;1;0;Clear Coat;0;0;0;10;False;True;True;True;True;True;True;True;True;True;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;131;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=ShadowCaster;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;132;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;True;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;False;True;1;LightMode=DepthOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;133;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;134;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;135;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;DepthNormals;0;6;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=DepthNormals;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;136;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;GBuffer;0;7;GBuffer;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=UniversalGBuffer;False;True;10;d3d11;gles;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;137;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;SceneSelectionPass;0;8;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;138;256,80;Float;False;False;-1;3;UnityEditor.ShaderGraphLitGUI;0;1;New Amplify Shader;94348b07e5e8bab40bd6c8a1e3df54cd;True;ScenePickingPass;0;9;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Lit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
WireConnection;43;0;44;0
WireConnection;50;0;43;0
WireConnection;49;0;50;0
WireConnection;78;0;49;0
WireConnection;32;0;31;0
WireConnection;30;1;29;0
WireConnection;28;0;32;0
WireConnection;28;2;30;0
WireConnection;65;0;27;0
WireConnection;65;1;79;0
WireConnection;10;1;4;0
WireConnection;9;0;18;0
WireConnection;22;1;28;0
WireConnection;22;5;65;0
WireConnection;3;0;9;0
WireConnection;3;2;10;0
WireConnection;52;0;3;0
WireConnection;25;0;22;0
WireConnection;57;0;52;0
WireConnection;57;1;25;0
WireConnection;118;0;117;0
WireConnection;112;82;53;0
WireConnection;112;5;57;0
WireConnection;119;0;118;0
WireConnection;58;0;112;32
WireConnection;58;1;112;32
WireConnection;120;0;119;0
WireConnection;121;0;120;0
WireConnection;127;0;58;0
WireConnection;127;1;128;0
WireConnection;40;0;12;1
WireConnection;42;0;40;0
WireConnection;42;1;12;2
WireConnection;33;0;16;0
WireConnection;46;0;82;0
WireConnection;46;1;42;0
WireConnection;15;0;46;0
WireConnection;15;1;33;0
WireConnection;23;0;3;0
WireConnection;23;1;25;0
WireConnection;20;0;58;0
WireConnection;20;1;15;0
WireConnection;19;0;15;0
WireConnection;72;0;80;0
WireConnection;1;1;23;0
WireConnection;93;0;92;0
WireConnection;91;0;86;0
WireConnection;14;0;20;0
WireConnection;14;1;19;0
WireConnection;71;0;1;1
WireConnection;71;1;72;0
WireConnection;83;1;93;0
WireConnection;87;0;91;0
WireConnection;115;0;87;0
WireConnection;115;1;87;0
WireConnection;84;1;93;0
WireConnection;84;5;101;0
WireConnection;103;1;23;0
WireConnection;2;0;5;0
WireConnection;2;1;71;0
WireConnection;113;0;114;0
WireConnection;113;1;83;0
WireConnection;7;0;6;5
WireConnection;7;1;14;0
WireConnection;85;1;93;0
WireConnection;89;0;2;0
WireConnection;89;1;113;0
WireConnection;89;2;87;0
WireConnection;94;0;103;0
WireConnection;94;1;84;0
WireConnection;94;2;87;0
WireConnection;90;0;7;0
WireConnection;90;1;96;5
WireConnection;90;2;115;0
WireConnection;126;0;127;0
WireConnection;126;1;125;0
WireConnection;130;0;89;0
WireConnection;130;1;94;0
WireConnection;130;4;139;0
WireConnection;130;2;90;0
WireConnection;130;6;122;0
WireConnection;130;8;126;0
ASEEND*/
//CHKSM=749E3722A446026772B64002D890103ACBDA4E65