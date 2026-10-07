Shader "Hidden/CompositeShader"
{
    Properties
    {
        _VignetteCenter ("Vignette Center", Vector) = (0.5, 0.5, 0, 0)
        _VignetteRadius ("Vignette Radius", Range(0, 1)) = 0.15
        _VignetteFeather ("Vignette Feather", Range(0, 1)) = 0.08
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0
        _VignetteDarkness ("Vignette Darkness", Range(0, 1)) = 0.65
        _VignetteDesaturation ("Vignette Desaturation", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // URP native declaration for textures and samplers.
            TEXTURE2D(_OutlineTexture);
            SAMPLER(sampler_OutlineTexture);
            TEXTURE2D(_LiquidOutlineTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _VignetteCenter;
                float _VignetteRadius;
                float _VignetteFeather;
                float _VignetteStrength;
                float _VignetteDarkness;
                float _VignetteDesaturation;
            CBUFFER_END

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                // Sample the low-resolution scene color.
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                
                // Sample the low-resolution outline mask.
                half4 outline = SAMPLE_TEXTURE2D(_OutlineTexture, sampler_OutlineTexture, uv);

                // Replace color with the outline where alpha is greater than zero.
                float3 result = lerp(color.rgb, outline.rgb, outline.a);
                half4 liquidOutline = SAMPLE_TEXTURE2D(_LiquidOutlineTexture, sampler_PointClamp, uv);
                result = lerp(result, liquidOutline.rgb, liquidOutline.a);

                UNITY_BRANCH
                if (_VignetteStrength > 0.0)
                {
                    float2 vignetteUV = uv;

                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float2 offset = vignetteUV - _VignetteCenter.xy;
                    offset.x *= aspect;

                    float radius = max(_VignetteRadius, 0.0);
                    float feather = max(_VignetteFeather, 0.00001);
                    float distanceFromCenter = length(offset);
                    float vignetteMask = smoothstep(radius, radius + feather, distanceFromCenter);
                    float strength = saturate(_VignetteStrength);
                    float effectAmount = strength * vignetteMask;
                    float desaturation = saturate(_VignetteDesaturation) * effectAmount;
                    float darkness = saturate(_VignetteDarkness) * effectAmount;
                    float luminance = Luminance(result);
                    result = lerp(result, luminance.xxx, desaturation);
                    result *= 1.0 - darkness;
                }
                
                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
