#ifndef LIQUID_OUTLINE_TEXTURES_INCLUDED
#define LIQUID_OUTLINE_TEXTURES_INCLUDED
TEXTURE2D_X(_LiquidNormalDepth);
TEXTURE2D_X(_LiquidMetadata);
TEXTURE2D_X(_LiquidOutlineColor);

// LinearEyeDepth alone only handles perspective cameras.
float LiquidEyeDepth(float rawDepth)
{
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
            rawDepth = 1.0 - rawDepth;
        #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
    }
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}
#endif
