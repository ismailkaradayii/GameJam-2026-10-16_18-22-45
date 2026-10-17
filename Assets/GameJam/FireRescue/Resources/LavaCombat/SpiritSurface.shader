Shader "GameJam/Living Spirit Surface"
{
    Properties
    {
        _Kind ("0 Obsidian / 1 Molten / 2 Eye", Float) = 1
        [HDR] _EmissionColor ("Heat tint", Color) = (1, .35, .025, 1)
        _Hit ("Impact", Range(0,1)) = 0
        _Heat ("Heat", Float) = 1
        _Seed ("Variation", Float) = 0
        _Damage ("Damage", Range(0,1)) = 0
        _ImpactPoint ("Impact position", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float3 local:TEXCOORD2; float2 uv:TEXCOORD3; float fog:TEXCOORD4; };
            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor, _ImpactPoint;
                float _Kind, _Hit, _Heat, _Seed, _Damage;
            CBUFFER_END
            float Hash(float3 p) { p=frac(p*.3183099+.1); p*=17; return frac(p.x*p.y*p.z*(p.x+p.y+p.z)); }
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            V Vert(A i)
            {
                V o; o.local=i.vertex.xyz; o.world=TransformObjectToWorld(i.vertex.xyz);
                o.position=TransformWorldToHClip(o.world); o.normal=TransformObjectToWorldNormal(i.normal);
                o.uv=i.uv; o.fog=ComputeFogFactor(o.position.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float3 n=normalize(i.normal), view=normalize(GetWorldSpaceViewDir(i.world));
                float rim=pow(1-saturate(abs(dot(n,view))),2.3);
                float t=_Time.y, grain=Noise(i.local*32);
                float flow=Noise(i.local*float3(7,4,7)-float3(0,t*.9+_Seed,0));
                float vein=abs(Noise(i.local*8.5+Noise(i.local*3)*1.8)-.5);
                float cracks=1-smoothstep(.007,.017+_Damage*.009,vein);
                float3 col;
                if (_Kind<.5)
                {
                    Light key=GetMainLight();
                    float lit=saturate(dot(n,key.direction))*.6+.3;
                    col=float3(.026,.021,.028)*(lit+grain*.9);
                    col+=float3(1.2,.17,.012)*cracks*.24*_Heat;
                    col+=float3(.5,.065,.008)*rim;
                }
                else if (_Kind<1.5)
                {
                    float heat=saturate(flow*.8+(1-i.uv.y)*.55);
                    col=lerp(float3(.48,.018,.004),float3(2.6,.62,.028),smoothstep(.12,.75,heat));
                    col=lerp(col,float3(3.1,1.55,.25),pow(heat,5)*.65);
                    col*=.72+grain*.22;
                    col+=float3(1,.23,.008)*cracks*.4;
                    // A deep ember edge keeps the silhouette visible against the lava river.
                    col=lerp(col,float3(.28,.015,.003),rim*.68);
                    col*=_Heat;
                    // The face is cooled crust on the same rounded mesh, not a floating plate.
                    float face=smoothstep(.08,.23,i.local.z)*smoothstep(.36,.51,i.local.y)*(1-smoothstep(.87,1.06,i.local.y));
                    float3 crust=float3(.026,.021,.029)*(.65+grain*.7)+float3(1.5,.19,.01)*cracks*.38*_Heat;
                    col=lerp(col,crust,face);
                }
                else col=_EmissionColor.rgb*(1.4+_Heat*.4);
                float focal=exp(-distance(i.world,_ImpactPoint.xyz)*5);
                col=lerp(col,float3(5.5,4.5,2.7),_Hit*(.72+focal*.28));
                return half4(MixFog(col,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor, _ImpactPoint;
                float _Kind, _Hit, _Heat, _Seed, _Damage;
            CBUFFER_END
            float3 _LightDirection, _LightPosition;
            struct ShadowA { float4 vertex:POSITION; float3 normal:NORMAL; };
            float4 ShadowVert(ShadowA i):SV_POSITION
            {
                float3 world=TransformObjectToWorld(i.vertex.xyz);
                float3 normal=TransformObjectToWorldNormal(i.normal);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-world);
                #else
                    float3 direction=_LightDirection;
                #endif
                float4 position=TransformWorldToHClip(ApplyShadowBias(world,normal,direction));
                #if UNITY_REVERSED_Z
                    position.z=min(position.z,UNITY_NEAR_CLIP_VALUE*position.w);
                #else
                    position.z=max(position.z,UNITY_NEAR_CLIP_VALUE*position.w);
                #endif
                return position;
            }
            half4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
