// SPDX-License-Identifier: GPL-3.0-only
// Unlit URP shader that draws the mesh's vertex colours. Used for the untextured debug level.
Shader "DreamForge/VertexColorUnlit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.color = input.color;
            return o;
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
            half4 Frag(Varyings input) : SV_Target { return half4(input.color.rgb, 1); }
            ENDHLSL
        }

        // Needed when the renderer uses a depth prepass or depth priming.
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
            half Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
