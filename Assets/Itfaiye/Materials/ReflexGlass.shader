Shader "Itfaiye/Reflex Glass"
{
    Properties
    {
        _Tint("Lens tint", Color) = (.08,.26,.29,.045)
        [HDR] _DotColor("Reticle", Color) = (4,.055,.012,1)
        _Reticle("Projected reticle UV / radius / visible", Vector) = (.5,.5,.012,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint, _DotColor;
            float4 _Reticle;
            CBUFFER_END
            struct Attributes { float4 positionOS: POSITION; float2 uv: TEXCOORD0; };
            struct Varyings { float4 positionCS: SV_POSITION; float2 uv: TEXCOORD0; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 Frag(Varyings i): SV_Target
            {
                float2 edge = abs(i.uv-.5);
                // The clipped corners match the optic's machined housing.
                clip(.91-edge.x-edge.y);
                float2 delta = (i.uv-_Reticle.xy)*float2(1,.78);
                float radius=max(_Reticle.z,.001);
                float d=length(delta), aa=max(fwidth(d),.002);
                float dot=(1-smoothstep(radius-aa,radius+aa,d))*_Reticle.w;
                float halo=exp(-d*d/(radius*radius*5))*.11*_Reticle.w;
                float rim=pow(saturate(max(edge.x,edge.y)*2),14);
                return half4(lerp(_Tint.rgb,_DotColor.rgb,saturate(dot+halo)),
                    saturate(_Tint.a+rim*.07+dot*.94+halo));
            }
            ENDHLSL
        }
    }
}
