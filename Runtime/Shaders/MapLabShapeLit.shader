Shader "MapLab/Shape Lit"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _White("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                float2 surface : TEXCOORD1;
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color : COLOR;
                half flatColor : TEXCOORD4;
            };

            float4 _White;

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            Varyings LitVertex(Attributes input)
            {
                Varyings output = CommonLitVertex(input);
                output.color = input.color;
                output.flatColor = input.surface.x;
                return output;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                const half4 texel = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv), half4(1, 1, 1, 1), input.flatColor);
                const half4 main = half4(_White) * input.color * texel;
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
            };

            float4 _White;

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                return CommonNormalsVertex(input);
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                return CommonNormalsFragment(input, _White);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                float2 surface : TEXCOORD1;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
                half flatColor : TEXCOORD4;
            };

            float4 _White;

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = CommonUnlitVertex(input);
                output.color = input.color;
                output.flatColor = input.surface.x;
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                const half4 texel = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv), half4(1, 1, 1, 1), input.flatColor);
                return half4(_White) * input.color * texel;
            }
            ENDHLSL
        }
    }
}
