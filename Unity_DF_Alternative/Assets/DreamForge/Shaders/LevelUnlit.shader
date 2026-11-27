// SPDX-License-Identifier: GPL-3.0-only
// Unlit URP shader for level geometry: texture x vertex colour, no lighting.
// Debug colours use the default white texture; textured walls use white vertex colours.
// With _ALPHATEST_ON (masked walls: column shapes used as faces) pixels with alpha < 0.5 are cut out.
Shader "DreamForge/LevelUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip (masked walls)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.uv = input.uv;
            o.color = input.color;
            return o;
        }

        half4 Sample(Varyings input)
        {
            half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            #if defined(_ALPHATEST_ON)
            clip(t.a - 0.5h);
            #endif
            return t;
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON
            half4 Frag(Varyings input) : SV_Target
            {
                half4 t = Sample(input);
                return half4(t.rgb * input.color.rgb, 1);
            }
            ENDHLSL
        }

        // Needed when the renderer uses a depth prepass or depth priming; cuts out like the colour pass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON
            half Frag(Varyings input) : SV_Target { Sample(input); return 0; }
            ENDHLSL
        }
    }
}
