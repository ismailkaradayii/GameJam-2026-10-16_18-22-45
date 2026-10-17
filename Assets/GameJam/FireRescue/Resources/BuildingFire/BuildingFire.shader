Shader "GameJam/Building Fire"
{
    Properties { _Smoke ("Smoke", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float seed:TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            float _Smoke;
            CBUFFER_END
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float fbm(float2 p) { return noise(p)*.57+noise(p*2.07+7.3)*.28+noise(p*4.13+19.1)*.15; }
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color;
                o.seed=i.color.b*31.7; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                float n=fbm(i.uv*float2(4.5,6)+float2(i.seed,-_Time.y*lerp(3.8,.32,_Smoke)));
                if (_Smoke>.5)
                {
                    float edge=saturate((1-length(p*float2(1,1.03)))*2.8);
                    float alpha=edge*edge*smoothstep(.12,.8,n)*i.color.a;
                    return half4(i.color.rgb*lerp(.65,1.25,n),alpha);
                }
                float v=i.uv.y;
                float warp=fbm(i.uv*float2(2.6,3.8)+float2(i.seed+9,-_Time.y*2.7))-.5;
                float x=p.x+warp*(.3+v*.95);
                float width=lerp(.72,.22,v);
                float body=exp(-x*x/(width*width)*2.3)*pow(saturate(1-v),.65);
                float turbulent=body*(.4+n*1.5)-(1-n)*(.08+v*.2);
                float alpha=smoothstep(.025,.32,turbulent)*smoothstep(0,.12,v)*smoothstep(1,.78,v)*i.color.a;
                float heat=saturate(turbulent*1.45+(1-v)*.12);
                half3 color=lerp(half3(.7,.055,.008),half3(2.1,.48,.025),smoothstep(.05,.6,heat));
                color=lerp(color,half3(3.1,1.7,.38),smoothstep(.65,1,heat));
                return half4(color*i.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
