Shader "SandBlocks/SelectionOutline"
{
    Properties
    {
        [Header(Outline Appearance)]
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width", Range(0.005, 0.2)) = 0.045
        _ZOffset ("Z Depth Offset", Range(-0.02, 0.02)) = -0.002

        [Header(Optional Subtle Pulse)]
        _PulseSpeed ("Pulse Speed (0 to disable)", Float) = 0.0
        _PulseAmount ("Pulse Amount", Range(0.0, 0.5)) = 0.0

        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4 // LEqual
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0 // Off
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry+10" 
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
                float _ZOffset;
                float _PulseSpeed;
                float _PulseAmount;
                float _ZTest;
                float _ZWrite;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = SafeNormalize(TransformObjectToWorldNormal(input.normalOS));

                float pulse = 0.0;
                if (_PulseAmount > 0.001 && _PulseSpeed > 0.001)
                {
                    pulse = sin(_Time.y * _PulseSpeed) * _PulseAmount;
                }

                float width = _OutlineWidth * (1.0 + pulse);
                positionWS += normalWS * width;

                float4 positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    positionCS.z -= _ZOffset * positionCS.w;
                #else
                    positionCS.z += _ZOffset * positionCS.w;
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(_OutlineColor);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
