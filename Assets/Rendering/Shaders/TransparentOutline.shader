Shader "Hidden/TransparentOutline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "LiquidEdges"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "LiquidOutlineTextures.hlsl"
            float _KernelRadius, _ZThresh, _NormalSmoothLow, _NormalSmoothHigh;
            float _LineAlpha, _CreaseAlpha, _CreaseBrighten;
            float3 _HighlightColor;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 center = SAMPLE_TEXTURE2D_X(_LiquidNormalDepth, sampler_PointClamp, uv);
                float4 centerMeta = SAMPLE_TEXTURE2D_X(_LiquidMetadata, sampler_PointClamp, uv);
                float opaqueDepth = LiquidEyeDepth(SampleSceneDepth(uv));
                float closest = 1e20;
                float2 edgeUV = uv;
                bool edge = false;
                float crease = 0;
                [unroll] for (int k = 0; k < 9; k++)
                {
                    if (k == 4) continue;
                    float2 p = uv + float2(k % 3 - 1, k / 3 - 1) * _BlitTexture_TexelSize.xy * _KernelRadius;
                    float4 neighbor = SAMPLE_TEXTURE2D_X(_LiquidNormalDepth, sampler_PointClamp, p);
                    float4 meta = SAMPLE_TEXTURE2D_X(_LiquidMetadata, sampler_PointClamp, p);
                    bool validCenter = center.a > 0;
                    bool validNeighbor = neighbor.a > 0;
                    bool boundary = validCenter != validNeighbor;
                    if (validCenter && validNeighbor)
                    {
                        boundary = meta.x != centerMeta.x || abs(neighbor.a - center.a) > _ZThresh;
                        if (!boundary)
                            crease = max(crease, 1.0 - saturate(dot(center.xyz, neighbor.xyz)));
                    }
                    if (!boundary) continue;
                    // Put silhouettes on the farther side, matching the opaque outline.
                    if (validNeighbor && (!validCenter || neighbor.a < center.a))
                    {
                        if (neighbor.a <= opaqueDepth + 0.001 && neighbor.a < closest)
                        {
                            closest = neighbor.a;
                            edgeUV = p;
                            edge = true;
                        }
                    }
                }
                if (edge)
                {
                    float4 meta = SAMPLE_TEXTURE2D_X(_LiquidMetadata, sampler_PointClamp, edgeUV);
                    float3 color = SAMPLE_TEXTURE2D_X(_LiquidOutlineColor, sampler_PointClamp, edgeUV).rgb;
                    if (meta.w > 0.5) color = _HighlightColor * 0.6;
                    return half4(color, meta.z * (meta.w > 0.5 ? 1.0 : _LineAlpha));
                }
                if (center.a <= 0 || center.a > opaqueDepth + 0.001) return 0;
                float amount = smoothstep(_NormalSmoothLow, max(_NormalSmoothLow + 0.0001, _NormalSmoothHigh), crease);
                float3 color = SAMPLE_TEXTURE2D_X(_LiquidOutlineColor, sampler_PointClamp, uv).rgb * (1 + _CreaseBrighten);
                return half4(color, amount * _CreaseAlpha * centerMeta.z);
            }
            ENDHLSL
        }
    }
}
