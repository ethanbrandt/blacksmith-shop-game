Shader "Custom/ToonLit"
{
    Properties
    {
        [Header(Palette Settings)]
        [Space(3)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.3, 0.3, 0.4, 1)
        [Toggle] _UsePalette ("Use Palette", Float) = 1

        [Header(Toon Light Banding Settings)]
        [Space(3)]
        _Cuts ("Cuts", Range(1, 8)) = 3
        _Steepness ("Steepness", Range(1, 8)) = 1.0
        _Wrap ("Wrap", Range(-1.0, 1.0)) = 0.0
        _ThresholdGradientSize ("Threshold Gradient Size", Range(0.0, 1.0)) = 0.1
        
        [Header(Shadow Settings)]
        [Space(3)]
        [Enum(Banding, 0, Shadow Map, 1)] _ReceiveShadowMap ("Receive Shadows", Float) = 1
        _ShadowReceiverBias ("Shadow Receiver Bias", Range(0.0, 0.5)) = 0.02
        _ShadowNormalBias ("Shadow Normal Bias", Range(0.0, 0.5)) = 0.04
       
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS

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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HighlightColor;
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
            
                float _ShadowReceiverBias;
                float _ShadowNormalBias;
                float _ReceiveShadowMap;
            CBUFFER_END

            float GLSLMod(float x, float y)
            {
                return x - y * floor(x / y);
            }

            float ToonDiffuse(float diffuseAmount)
            {
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
                        float blend = high > low
                            ? smoothstep(low, high, diffuseAmount)
                            : step(threshold, diffuseAmount);
                        float leftValue = threshold;
                        float rightValue = min(threshold + cut, 1.0);
                        diffuseStepped = saturate(lerp(leftValue, rightValue, blend));
                    }
                    else
                    {
                        diffuseStepped = originalStepped;
                    }
                }
                return diffuseStepped;
            }

            Light GetBiasedAdditionalLight(uint lightIndex, float3 positionWS, float3 normalWS, half4 shadowMask)
            {
                if (_ReceiveShadowMap > 0.5)
                {
                    Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
                    float ndotlSat = saturate(dot(normalWS, light.direction));
                    float sinNL = sqrt(max(0.0, 1.0 - ndotlSat * ndotlSat));
                    float3 biasedWS = positionWS + light.direction * _ShadowReceiverBias + normalWS * (_ShadowNormalBias * max(sinNL, 0.2));
                    return GetAdditionalLight(lightIndex, biasedWS, shadowMask);
                }
                return GetAdditionalLight(lightIndex, positionWS);
            }

            float3 ShadeToonPunctual(Light light, float3 normalWS)
            {
                #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer()))
                        return 0;
                #endif
                float ndotl = dot(normalWS, light.direction) + _Wrap;
                ndotl *= _Steepness;
                float stepped = ToonDiffuse(ndotl);
                float atten = light.distanceAttenuation;
                if (_ReceiveShadowMap > 0.5)
                    atten *= light.shadowAttenuation;
                return stepped * light.color * atten;
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

            half4 frag(Varyings input) : SV_Target
            {
                half3 albedo = _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float ndotl = dot(normalWS, lightDir);

                if (_ReceiveShadowMap > 0.5)
                {
                    float ndotlSat = saturate(ndotl);
                    float sinNL = sqrt(max(0.0, 1.0 - ndotlSat * ndotlSat));
                    float3 biasedWS = input.positionWS
                        + lightDir * _ShadowReceiverBias
                        + normalWS * (_ShadowNormalBias * max(sinNL, 0.2));
                    mainLight = GetMainLight(TransformWorldToShadowCoord(biasedWS));
                }

                float diffuseAmount = ndotl + _Wrap;
                diffuseAmount *= _Steepness;
                float diffuseStepped = ToonDiffuse(diffuseAmount);

                float shadow = 1.0;
                if (_ReceiveShadowMap > 0.5)
                    shadow = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float lit = diffuseStepped * shadow;

                float3 additionalLighting = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                    half4 shadowMask = half4(1, 1, 1, 1);
                    uint pixelLightCount = GetAdditionalLightsCount();

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetBiasedAdditionalLight(lightIndex, input.positionWS, normalWS, shadowMask);
                        additionalLighting += ShadeToonPunctual(addLight, normalWS);
                    LIGHT_LOOP_END
                #endif

                half3 finalColor;
                if (_UsePalette > 0.5)
                {
                    half3 paletteColor = albedo * lerp(_ShadowColor.rgb, _HighlightColor.rgb, lit);
                    finalColor = paletteColor + albedo * additionalLighting;
                }
                else
                {
                    float3 finalLighting = diffuseStepped * mainLight.color * shadow;
                    float3 ambient = _GlobalAmbientColor.rgb;
                    finalColor = albedo * (finalLighting + additionalLighting + ambient);
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
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
            
                float _ShadowReceiverBias;
                float _ShadowNormalBias;
                float _ReceiveShadowMap;
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
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
            
                float _ShadowReceiverBias;
                float _ShadowNormalBias;
                float _ReceiveShadowMap;
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
                half4 _ShadowColor;
                float _UsePalette;

                int _Cuts;
                float _Steepness;
                float _Wrap;
                float _ThresholdGradientSize;
            
                float _ShadowReceiverBias;
                float _ShadowNormalBias;
                float _ReceiveShadowMap;
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