Shader "Custom/ToonCandy"
{
    Properties
    {
        [Header(Base)]
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 1, 1)
        _BaseMap ("Texture (optional)", 2D) = "white" {}

        [Header(Soft Toon Shading)]
        _ShadowColor ("Shadow Tint", Color) = (0.45, 0.4, 0.8, 1)
        _ShadeThreshold ("Shade Threshold", Range(0,1)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.01,0.5)) = 0.2

        [Header(Gloss Highlight)]
        _SpecColor ("Highlight Color", Color) = (1,1,1,1)
        _SpecThreshold ("Highlight Size (higher = smaller)", Range(0.5,1)) = 0.93
        _SpecSoftness ("Highlight Softness", Range(0.001,0.2)) = 0.04
        _SpecIntensity ("Highlight Intensity", Range(0,1)) = 0.7

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimThreshold ("Rim Threshold", Range(0,1)) = 0.65
        _RimSoftness ("Rim Softness", Range(0.01,0.5)) = 0.25
        _RimIntensity ("Rim Intensity", Range(0,1)) = 0.35

        [Header(Vertical Gradient)]
        _BottomTint ("Bottom Tint", Color) = (0.8, 0.8, 0.9, 1)
        _GradientScale ("Gradient Scale (1 / object height)", Float) = 1
        _GradientOffset ("Gradient Offset", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        // Same CBUFFER in every pass = SRP Batcher compatible
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _SpecColor, _RimColor, _BottomTint;
            half _ShadeThreshold, _ShadeSoftness;
            half _SpecThreshold, _SpecSoftness, _SpecIntensity;
            half _RimThreshold, _RimSoftness, _RimIntensity;
            half _GradientScale, _GradientOffset;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half3  viewDirWS  : TEXCOORD2;
                half   grad       : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS  = GetWorldSpaceNormalizeViewDir(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.grad = saturate(IN.positionOS.y * _GradientScale + _GradientOffset);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewDirWS);
                Light mainLight = GetMainLight();
                half3 L = mainLight.direction;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                albedo *= lerp(_BottomTint.rgb, 1.0h, IN.grad);

                // Soft toon band (half-lambert + smoothstep)
                half ndl = dot(N, L) * 0.5h + 0.5h;
                half toon = smoothstep(_ShadeThreshold - _ShadeSoftness,
                                       _ShadeThreshold + _ShadeSoftness, ndl);
                half3 col = lerp(albedo * _ShadowColor.rgb, albedo, toon);
                col *= lerp(1.0h, mainLight.color, 0.6h);

                // Glossy highlight (Blinn-Phong style, thresholded)
                half3 H = normalize(L + V);
                half nh = saturate(dot(N, H));
                half spec = smoothstep(_SpecThreshold, _SpecThreshold + _SpecSoftness, nh);
                col += _SpecColor.rgb * spec * _SpecIntensity;

                // Soft rim
                half fres = 1.0h - saturate(dot(N, V));
                half rim = smoothstep(_RimThreshold, _RimThreshold + _RimSoftness, fres);
                col += _RimColor.rgb * rim * _RimIntensity;

                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}