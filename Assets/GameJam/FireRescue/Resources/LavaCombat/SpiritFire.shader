Shader "GameJam/Living Spirit Fire"
{
    Properties
    {
        _Hit ("Impact", Range(0,1)) = 0
        _Heat ("Heat", Float) = 1
        _Seed ("Variation", Float) = 0
        _Damage ("Damage", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float3 local:TEXCOORD2; float2 uv:TEXCOORD3; float fog:TEXCOORD4; };
            CBUFFER_START(UnityPerMaterial)
                float _Hit, _Heat, _Seed, _Damage;
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
                V o; float t=_Time.y*3.4+_Seed;
                float3 p=i.vertex.xyz;
                p.x+=sin(p.y*6-t+p.z*4)*p.y*p.y*.095;
                p.z+=cos(p.y*7-t*1.2+p.x*5)*p.y*.06;
                o.local=p; o.world=TransformObjectToWorld(p); o.position=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(i.normal); o.uv=i.uv; o.fog=ComputeFogFactor(o.position.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float t=_Time.y*2.2+_Seed;
                float3 q=i.local*float3(8,3.7,8)-float3(0,t,0);
                float noise=Noise(q)*.68+Noise(q*2.17)*.32;
                float facing=saturate(abs(dot(normalize(i.normal),normalize(GetWorldSpaceViewDir(i.world)))));
                float heat=saturate((1-i.uv.y)*.85+noise*.6-(1-facing)*.18);
                float3 col=lerp(float3(.65,.018,.002),float3(1.9,.35,.012),smoothstep(.15,.65,heat));
                col=lerp(col,float3(3.3,1.5,.24),smoothstep(.74,1.2,heat));
                float alpha=saturate((noise*.7+(1-i.uv.y)*.85-.12)*1.65);
                alpha*=smoothstep(0,.09,i.uv.y)*smoothstep(1,.8,i.uv.y)*(.35+.65*sqrt(facing));
                col*=_Heat; col=lerp(col,float3(5,3.8,1.8),_Hit*.85);
                return half4(MixFog(col,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
