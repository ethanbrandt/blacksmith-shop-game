Shader "Custom/ToonLiquid"
{
    Properties
    {
        [Header(Body)]
        _BaseColor ("Body Color and Opacity", Color) = (0.15, 0.65, 0.8, 0.4)
        _ShadowColor ("Shadow Color", Color) = (0.3, 0.3, 0.4, 1)
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _Cuts ("Light Bands", Range(1, 8)) = 3
        _Wrap ("Light Wrap", Range(0, 1)) = 0.2
        [Header(Wet Highlights)]
        [HDR] _RimColor ("Rim Color", Color) = (0.5, 0.9, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 3)) = 0.4
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        [HDR] _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularSize ("Specular Size", Range(0.01, 0.9)) = 0.15
        _SpecularSoftness ("Specular Softness", Range(0.001, 0.5)) = 0.05
        [Header(Surface Flow)]
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.3
        _FlowSpeed ("Flow Speed XY", Vector) = (0.03, 0.02, 0, 0)
        [Header(Waves In Object Space)]
        _WaveStrength ("Wave Strength", Range(0, 0.5)) = 0
        _WaveScale ("Wave Scale", Float) = 3
        _WaveSpeed ("Wave Speed", Float) = 1
        _WaveDirection ("Wave Pattern Rotation (Degrees)", Range(0, 360)) = 0
        [Enum(Up Down Y, 0,Side To Side X, 1,Forward Back Z, 2)] _WaveMotionAxis ("Wave Motion", Float) = 0
        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.03, 0.15, 0.2, 1)
        _OutlineOpacity ("Outline Opacity", Range(0, 1)) = 1
        [HideInInspector] _ObjectId ("Object ID", Float) = 0
        [HideInInspector] _Highlighted ("Highlighted", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "DisableBatching"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "LiquidOutlineTextures.hlsl"
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _ShadowColor, _HighlightColor;
            float4 _RimColor, _SpecularColor, _OutlineColor;
            float4 _NormalMap_ST, _FlowSpeed;
            float _Cuts, _Wrap, _RimStrength, _RimPower;
            float _SpecularSize, _SpecularSoftness, _NormalStrength;
            float _WaveStrength, _WaveScale, _WaveSpeed, _WaveDirection;
            float _WaveMotionAxis;
            float _OutlineOpacity, _ObjectId, _Highlighted;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
        };
        struct LiquidVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
        };

        // One deformation path for visible color and outline data.
        LiquidVaryings LiquidVertex(Attributes input)
        {
            LiquidVaryings output;
            float3 p = input.positionOS.xyz;
            // Rotate both waves in the local XZ plane; zero preserves the original pattern.
            float angle = radians(_WaveDirection);
            float2 direction = float2(cos(angle), sin(angle));
            float2 crossDirection = float2(-direction.y, direction.x);
            float phaseX = dot(p.xz, direction) * _WaveScale + _Time.y * _WaveSpeed;
            float phaseZ = dot(p.xz, crossDirection) * _WaveScale * 0.73 + _Time.y * _WaveSpeed * 1.17;
            // Rotate the height gradient too, keeping lighting and outlines aligned.
            float2 gradient = cos(phaseX) * _WaveStrength * _WaveScale * direction
                + cos(phaseZ) * _WaveStrength * _WaveScale * 0.365 * crossDirection;
            float3 motion = _WaveMotionAxis < 0.5 ? float3(0, 1, 0)
                : (_WaveMotionAxis < 1.5 ? float3(1, 0, 0) : float3(0, 0, 1));
            float height = _WaveStrength * (sin(phaseX) + 0.5 * sin(phaseZ));
            p += motion * height;

            // For p' = p + motion * height(p), J = I + motion * gradient^T.
            // Its cofactor transforms normals without dividing by a potentially
            // zero determinant when strong sideways waves compress the surface.
            float3 heightGradient = float3(gradient.x, 0, gradient.y);
            float3 n = input.normalOS;
            float3 deformedNormal = (1.0 + dot(heightGradient, motion)) * n
                - heightGradient * dot(motion, n);
            n = dot(deformedNormal, deformedNormal) > 0.000001 ? normalize(deformedNormal) : normalize(n);
            float3 t = input.tangentOS.xyz;
            t += motion * dot(heightGradient, t);
            output.positionWS = TransformObjectToWorld(p);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(n);
            output.tangentWS = float4(TransformObjectToWorldDir(t, false), input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _NormalMap);
            return output;
        }

        float3 LiquidNormal(LiquidVaryings input)
        {
            float3 n = normalize(input.normalWS);
            float3 t = input.tangentWS.xyz - n * dot(input.tangentWS.xyz, n);
            // Meshes without UV tangents still get valid geometry normals.
            if (dot(t, t) < 0.0001) return n;
            t = normalize(t);
            float3 b = cross(n, t) * input.tangentWS.w;
            float3 detail = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,
                input.uv + _Time.y * _FlowSpeed.xy), _NormalStrength);
            return normalize(t * detail.x + b * detail.y + n * detail.z);
        }

        float LiquidBand(float diffuse)
        {
            float bands = max(1.0, round(_Cuts));
            return ceil(saturate(diffuse) * bands) / bands;
        }

        float3 LiquidSpecular(Light light, float3 n, float3 viewDir)
        {
            float3 h = SafeNormalize(light.direction + viewDir);
            float threshold = 1.0 - _SpecularSize;
            float spec = smoothstep(threshold, min(1.0, threshold + _SpecularSoftness), saturate(dot(n, h)));
            return _SpecularColor.rgb * light.color * spec * saturate(dot(n, light.direction))
                * light.distanceAttenuation * light.shadowAttenuation;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LiquidVertex
            #pragma fragment LiquidFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 LiquidFragment(LiquidVaryings input) : SV_Target
            {
                float3 n = LiquidNormal(input);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float band = LiquidBand(dot(n, main.direction) + _Wrap);
                float lit = band * main.shadowAttenuation * main.distanceAttenuation;
                float3 color = _BaseColor.rgb * lerp(_ShadowColor.rgb, _HighlightColor.rgb * main.color, lit);
                color += LiquidSpecular(main, n, v);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        color += _BaseColor.rgb * light.color * LiquidBand(dot(n, light.direction) + _Wrap)
                            * light.distanceAttenuation * light.shadowAttenuation;
                        color += LiquidSpecular(light, n, v);
                    LIGHT_LOOP_END
                #endif
                color += _RimColor.rgb * _RimStrength * pow(1.0 - saturate(dot(n, v)), _RimPower);
                return half4(color, saturate(_BaseColor.a));
            }
            ENDHLSL
        }

        // No camera-depth or shadow-caster passes: the transparent body does not occlude them.
        Pass
        {
            Name "TransparentOutlineData"
            Tags { "LightMode"="TransparentOutlineData" }
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LiquidVertex
            #pragma fragment LiquidDataFragment
            struct LiquidDataOutput
            {
                float4 normalDepth : SV_Target0;
                float4 metadata : SV_Target1;
                float4 outlineColor : SV_Target2;
            };
            LiquidDataOutput LiquidDataFragment(LiquidVaryings input)
            {
                clip(_BaseColor.a - 0.0001);
                float depth = -TransformWorldToView(input.positionWS).z;
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float opaqueDepth = LiquidEyeDepth(SampleSceneDepth(uv));
                clip(opaqueDepth + 0.001 - depth);
                LiquidDataOutput output;
                output.normalDepth = float4(LiquidNormal(input), depth);
                output.metadata = float4(_ObjectId, saturate(_BaseColor.a), saturate(_OutlineOpacity), _Highlighted);
                output.outlineColor = _OutlineColor;
                return output;
            }
            ENDHLSL
        }
    }
}
