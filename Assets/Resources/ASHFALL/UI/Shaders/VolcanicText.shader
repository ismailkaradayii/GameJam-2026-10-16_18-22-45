Shader "Ashfall/UI/Volcanic Text"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
        _LavaTex ("Lava Texture", 2D) = "white" {}
        _RockColor ("Rock Color", Color) = (0.72, 0.28, 0.10, 1)
        _LavaColor ("Lava Color", Color) = (1.0, 0.36, 0.02, 1)
        _EmberColor ("Ember Color", Color) = (1.0, 0.82, 0.22, 1)
        _Brightness ("Brightness", Range(0.5, 2.5)) = 1.45
        _EdgeCutoff ("Glyph Edge Cutoff", Range(0, 0.5)) = 0.18
        _PatternScale ("Pattern Scale", Float) = 0.0035
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "VolcanicText"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 atlasUV : TEXCOORD0;
                float2 patternUV : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _LavaTex;
            fixed4 _TextureSampleAdd;
            fixed4 _RockColor;
            fixed4 _LavaColor;
            fixed4 _EmberColor;
            float _Brightness;
            float _EdgeCutoff;
            float _PatternScale;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.atlasUV = input.texcoord;
                output.patternUV = frac(input.vertex.xy * _PatternScale + float2(0.31, 0.67));
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 glyph = tex2D(_MainTex, input.atlasUV) + _TextureSampleAdd;
                float glyphAlpha = smoothstep(_EdgeCutoff, min(_EdgeCutoff + 0.42, 0.98), glyph.a);
                fixed3 textureColor = tex2D(_LavaTex, input.patternUV).rgb;
                float heat = saturate(textureColor.r * 1.65 + textureColor.g * 0.45 + 0.08);
                fixed3 volcanic = lerp(_RockColor.rgb, _LavaColor.rgb, heat);
                volcanic += _EmberColor.rgb * pow(heat, 2.4) * 0.95;
                volcanic *= _Brightness;

                fixed4 color = fixed4(volcanic * input.color.rgb, glyphAlpha * input.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
