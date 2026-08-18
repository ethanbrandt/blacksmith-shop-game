Shader "Custom/ToonLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        
        // Palette System.
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.3, 0.3, 0.4, 1)
        [Toggle] _UsePalette ("Use Palette", Float) = 0

        _Cuts ("Cuts", Range(1, 8)) = 3
        _Steepness ("Steepness", Range(1, 8)) = 1.0
        _Wrap ("Wrap", Range(-1.0, 0.0)) = 0.0
       
        _ThresholdGradientSize ("Threshold Gradient Size", Range(0.0, 1.0)) = 0.2
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry" 
       
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            float4 _GlobalAmbientColor;

            // Unified CBUFFER for SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                // Palette system variables.
                half4 _HighlightColor;
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
            CBUFFER_END

            float GLSLMod(float x, float y)
            {
                return x - y * floor(x / y);
            }

            float2 RotateVec2(float2 v, float angleDeg)
            {
                float rad = radians(angleDeg);
                float c = cos(rad);
                float s = sin(rad);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float EvaluateLight(float3 _normalWS, Light _light)
            {
                float diffuseAmount = dot(_normalWS, _light.direction) + _Wrap;
                diffuseAmount *= _Steepness;

                float cutsInv = 1.0 / float(_Cuts);
                float cut = cutsInv;

                float originalIndex = ceil(diffuseAmount * float(_Cuts));
                float originalStepped = saturate(originalIndex * cut);
                float diffuseStepped = saturate(diffuseAmount + GLSLMod(1.0 - diffuseAmount, cutsInv));

                if (_ThresholdGradientSize > 0.0)
                {
                    float nearestK = floor(diffuseAmount / cut + 0.5);
                    float threshold = nearestK * cut;

                    if (nearestK >= 0.0 && nearestK <= float(_Cuts))
                    {
                        float halfWidth = 0.5 * cut * _ThresholdGradientSize;
                        float low = max(0.0, threshold - halfWidth);
                        float high = min(1.0, threshold + halfWidth);

                        float blend = 0.0;
                        if (high > low)
                            blend = smoothstep(low, high, diffuseAmount);
                        else
                            blend = step(threshold, diffuseAmount);
                        float leftValue = threshold;
                        float rightValue = min(threshold + cut, 1.0);
                        diffuseStepped = lerp(leftValue, rightValue, blend);
                        diffuseStepped = saturate(diffuseStepped);
                    }
                    else
                    {
                        diffuseStepped = originalStepped;
                    }
                }
            }

            half4 frag(Varyings input) : SV_Target
            {

                half3 albedo = _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float diffuseAmount = dot(normalWS, mainLight.direction) + _Wrap;
                diffuseAmount *= _Steepness;

                float cutsInv = 1.0 / float(_Cuts);
                float cut = cutsInv;

                float originalIndex = ceil(diffuseAmount * float(_Cuts));
                float originalStepped = saturate(originalIndex * cut);
                float diffuseStepped = saturate(diffuseAmount + GLSLMod(1.0 - diffuseAmount, cutsInv));
                
                if (_ThresholdGradientSize > 0.0)
                {
                    float nearestK = floor(diffuseAmount / cut + 0.5);
                    float threshold = nearestK * cut;

                    if (nearestK >= 0.0 && nearestK <= float(_Cuts))
                    {
                        float halfWidth = 0.5 * cut * _ThresholdGradientSize;
                        float low = max(0.0, threshold - halfWidth);
                        float high = min(1.0, threshold + halfWidth);

                        float blend = 0.0;
                        if (high > low)
                            blend = smoothstep(low, high, diffuseAmount);
                        else
                            blend = step(threshold, diffuseAmount);
                        float leftValue = threshold;
                        float rightValue = min(threshold + cut, 1.0);
                        diffuseStepped = lerp(leftValue, rightValue, blend);
                        diffuseStepped = saturate(diffuseStepped);
                    }
                    else
                    {
                        diffuseStepped = originalStepped;
                    }
                }

                float shadow = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float lit = diffuseStepped * shadow;
                half3 finalColor;

                if (_UsePalette > 0.5)
                {
                    half3 paletteColor = albedo * lerp(_ShadowColor.rgb, _HighlightColor.rgb, lit);
                    finalColor = paletteColor;
                }
                else
                {
                    float3 finalLighting = diffuseStepped * mainLight.color * shadow;
                    float3 ambient = _GlobalAmbientColor.rgb;
                    finalColor = albedo * (finalLighting + ambient);
                }

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
        
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _MidtoneColor;
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
                
                float _UseDither;
                float _DitherStrength;
                half4 _Color2;
                float _Noise2Scale;
                float _Noise2Threshold;
                
                half4 _Color3;
                float _Noise3Scale;
                float _Noise3Threshold;
            CBUFFER_END

            float3 _LightDirection;
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionHCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            HLSLPROGRAM
            #pragma vertex vert
        
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _MidtoneColor;
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
                
                float _UseDither;
                float _DitherStrength;
                half4 _Color2;
                float _Noise2Scale;
                float _Noise2Threshold;
                
                half4 _Color3;
                float _Noise3Scale;
                float _Noise3Threshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
        
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _MidtoneColor;
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
                
                float _UseDither;
                float _DitherStrength;
                half4 _Color2;
                float _Noise2Scale;
                float _Noise2Threshold;
                
                half4 _Color3;
                float _Noise3Scale;
                float _Noise3Threshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                return half4(normal * 0.5 + 0.5, 0.0);
            }
            ENDHLSL
        }
    }
}