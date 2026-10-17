Shader "GameJam/Building Fire Tongues"
{
    Properties { _Intensity ("Heat brightness", Range(.1,3)) = 1.2 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float fog:TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }
            V Vert(A i)
            {
                V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color;
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float y=i.uv.y, x=i.uv.x*2-1;
                float phase=i.color.b*43;
                x+=sin(y*5-_Time.y*6+phase)*y*.07;
                float2 p=float2(x*3+phase,y*5-_Time.y*3.3);
                float n=Noise(p)*.6+Noise(p*2.1+7)*.28+Noise(p*4.3)*.12;
                float fuel=(1-y)*.9-pow(abs(x),1.4)*.65;
                float tongues=smoothstep(.05,.5,fuel+(n-.45)*.95);
                float edge=1-smoothstep(.8,1,abs(x));
                float body=smoothstep(0,.05,y)*pow(saturate(1-y),.35);
                float alpha=tongues*edge*body*i.color.a;
                float hot=saturate(fuel*.9+n*.38);
                half3 rgb=lerp(half3(.8,.025,.002),half3(3.4,.76,.045),smoothstep(.15,.9,hot));
                float core=pow(saturate(1-abs(x)*3),3)*pow(1-y,2);
                rgb=lerp(rgb,half3(4,2.8,1.05),core*.6)*_Intensity*i.color.g;
                rgb=MixFogColor(rgb,half3(0,0,0),i.fog);
                return half4(rgb,alpha);
            }
            ENDHLSL
        }
    }
}
