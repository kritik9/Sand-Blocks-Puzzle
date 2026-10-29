Shader "Custom/ToonCandy"
{
    Properties
    {
        [Header(Base)]
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 1, 1)
        _BaseMap ("Texture (optional)", 2D) = "white" {}
        _Saturation ("Saturation Boost", Range(0.5,2)) = 1.25
        _Brightness ("Brightness", Range(0.5,1.5)) = 1.05

        [Header(Soft Toon Shading)]
        _ShadowColor ("Shadow Tint", Color) = (0.7, 0.65, 0.95, 1)
        _ShadeThreshold ("Shade Threshold", Range(0,1)) = 0.45
        _ShadeSoftness ("Shade Softness", Range(0.01,0.5)) = 0.3

        [Header(Gloss Highlight)]
        _SpecColor ("Highlight Color", Color) = (1,1,1,1)
        _SpecThreshold ("Highlight Size (higher = smaller)", Range(0.5,1)) = 0.95
        _SpecSoftness ("Highlight Softness", Range(0.001,0.2)) = 0.04
        _SpecIntensity ("Highlight Intensity", Range(0,1)) = 0.5

        [Header(Same Hue Edge Line  in the same pass, free)]
        _OutlineDarkness ("Outline Darkness", Range(0.2,1)) = 0.55
        _EdgeStart ("Edge Start", Range(0,1)) = 0.6
        _EdgeSoft ("Edge Softness", Range(0.01,0.5)) = 0.12
        _EdgeStrength ("Edge Strength", Range(0,1)) = 0.9

        [Header(Hull Outline  extra pass, delete the pass for crowds)]
        _OutlineWidth ("Outline Width (object units)", Range(0,0.1)) = 0.015
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _SpecColor;
            half _Saturation, _Brightness;
            half _ShadeThreshold, _ShadeSoftness;
            half _SpecThreshold, _SpecSoftness, _SpecIntensity;
            half _OutlineDarkness, _EdgeStart, _EdgeSoft, _EdgeStrength;
            half _OutlineWidth;
        CBUFFER_END

        half3 Boost(half3 c)
        {
            half l = dot(c, half3(0.299h, 0.587h, 0.114h));
            return saturate(lerp(l.xxx, c, _Saturation) * _Brightness);
        }

        // Darker, more saturated version of the same color
        half3 OutlineColor(half3 c)
        {
            half l = dot(c, half3(0.299h, 0.587h, 0.114h));
            return saturate(lerp(l.xxx, c, 1.3h) * _OutlineDarkness);
        }
        ENDHLSL

        // ---------------- Main colored pass ----------------
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS  = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewDirWS);
                Light mainLight = GetMainLight();
                half3 L = mainLight.direction;

                half3 albedo = Boost(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb);

                half ndl = dot(N, L) * 0.5h + 0.5h;
                half toon = smoothstep(_ShadeThreshold - _ShadeSoftness,
                                       _ShadeThreshold + _ShadeSoftness, ndl);
                half3 col = lerp(albedo * _ShadowColor.rgb, albedo, toon);

                // soft glossy highlight
                half3 H = normalize(L + V);
                half spec = smoothstep(_SpecThreshold, _SpecThreshold + _SpecSoftness, saturate(dot(N, H)));
                col += _SpecColor.rgb * spec * _SpecIntensity;

                // same-hue edge line (darker, more saturated version of its own color)
                half fres = 1.0h - saturate(dot(N, V));
                half edge = smoothstep(_EdgeStart, _EdgeStart + _EdgeSoft, fres) * _EdgeStrength;
                col = lerp(col, OutlineColor(albedo), edge);

                return half4(col, 1);
            }
            ENDHLSL
        }

        // ---------------- Inverted hull outline (optional) ----------------
        Pass
        {
            Name "HullOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 c = Boost(_BaseColor.rgb);
                return half4(OutlineColor(c), 1);
            }
            ENDHLSL
        }

        // ---------------- Shadow caster ----------------
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

        // ---------------- Depth ----------------
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