// MySharedFunctions.hlsl
#ifndef SHARED_FUNCTIONS_INCLUDED
#define SHARED_FUNCTIONS_INCLUDED

//Each keyword must start with the property name followed by _<Enum Value>. All in uppercase.
#pragma shader_feature _DIFF_CIELAB _DIFF_SRGB _DIFF_LENGTH _DIFF_LUMINANCE _DIFF_HSV

//--------------------------------------------------------------------------------
// ColorSpace Conversion
//  https://github.com/fuqunaga/ColorSpace
//
// Refs:
//  https://en.wikipedia.org/wiki/SRGB
//  https://en.wikipedia.org/wiki/CIELAB_color_space
//  https://github.com/mattharrison/colorspace.py/blob/master/colorspace.py
//--------------------------------------------------------------------------------
//------------------------------------------------------------
// CIEXYZ
//------------------------------------------------------------
float3 RGBLinearToXYZ(float3 rgb)
{
    float3x3 m = float3x3(
        0.41239080, 0.35758434, 0.18048079,
        0.21263901, 0.71516868, 0.07219232,
        0.01933082, 0.11919478, 0.95053215
    );

    return mul(m, rgb);
}
float3 XYZToRGBLinear(float3 xyz)
{
    float3x3 m = float3x3(
        +3.24096994, -1.53738318, -0.49861076,
        -0.96924364, +1.8759675,  +0.04155506,
        +0.05563008, -0.20397696, +1.05697151
    );

    return mul(m, xyz);
}
//------------------------------------------------------------
// CIELAB
// Note: the L* coordinate ranges from 0 to 100.
//------------------------------------------------------------
static const float LAB_Xn = 0.950489;
static const float LAB_Yn = 1.0;
static const float LAB_Zn = 1.088840;
float _LABFunc(float t)
{
    const float T = 0.00885645168; //pow(6/29,3);
    return t > T
    ? pow(t, 1.0/3.0)
    : 7.78703704 * t + 4.0/29.0;
}
float _LABFuncInv(float t)
{
    const float T = 6/29.0;
    return t > T
    ? t*t*t
    : 3*T*T*(t - 4/29.0);
}
float3 XYZToLAB(float3 xyz)
{
    float fx = _LABFunc(xyz.x / LAB_Xn);
    float fy = _LABFunc(xyz.y / LAB_Yn);
    float fz = _LABFunc(xyz.z / LAB_Zn);

    return float3(
        116*fy - 16,
        500*(fx-fy),
        200*(fy-fz)
    );
}
float3 LABToXYZ(float3 lab)
{
    float ltmp = (lab.x + 16)/116;
    return float3(
        LAB_Xn * _LABFuncInv(ltmp + lab.y / 500),
        LAB_Yn * _LABFuncInv(ltmp),
        LAB_Zn * _LABFuncInv(ltmp - lab.z / 200)
    );
}
//------------------------------------------------------------
// sRGB(D65)
//------------------------------------------------------------
float3 SRGBToRGBLinear(float3 rgb)
{
    const float t = 0.04045;
    float3 a = rgb / 12.92;
    float3 b = pow((rgb+0.055)/1.055, 2.4);
    return float3(
        rgb.r<=t ? a.r : b.r,
        rgb.g<=t ? a.g : b.g,
        rgb.b<=t ? a.b : b.b
    );
}
float3 RGBLinearToSRGB(float3 rgb)
{
    const float t = 0.031308;
    float3 a = rgb * 12.92;
    float3 b = 1.055*pow(rgb, 1/2.4) - 0.055;
    float3 srgb = float3(
        rgb.r<=t ? a.r : b.r,
        rgb.g<=t ? a.g : b.g,
        rgb.b<=t ? a.b : b.b
    );

    return saturate(srgb);
}
float3 RGBToXYZ(float3 rgb)
{
    return RGBLinearToXYZ(SRGBToRGBLinear(rgb));
}
float3 XYZToRGB(float3 xyz)
{
    float3 rgbl = XYZToRGBLinear(xyz);
    return RGBLinearToSRGB(rgbl);
}
// Note: the L* coordinate ranges from 0 to 100.
float3 RGBToLAB(float3 rgb)
{
    return XYZToLAB(RGBToXYZ(rgb));
}
float3 LABToRGB(float3 lab)
{
    return XYZToRGB(LABToXYZ(lab));
}
float3 RGBtoHSV(in float3 RGB)
{
    float3 HSV = 0;
    // Get the maximum and minimum color components. The max component is the 'Value' (V).
    float maxComponent = max(RGB.r, max(RGB.g, RGB.b));
    float minComponent = min(RGB.r, min(RGB.g, RGB.b));
    float diff = maxComponent - minComponent;

    // Value (V)
    HSV.z = maxComponent;

    // Saturation (S)
    if (maxComponent != 0)
    {
        HSV.y = diff / maxComponent;
    }
    else
    {
        HSV.y = 0; // Saturation is 0 if there is no value
        return HSV; // Early exit for black color to avoid division by zero later
    }

    // Hue (H)
    if (diff != 0)
    {
        if (RGB.r == maxComponent)
        {
            HSV.x = (RGB.g - RGB.b) / diff;
        }
        else if (RGB.g == maxComponent)
        {
            HSV.x = 2.0 + (RGB.b - RGB.r) / diff;
        }
        else
        {
            HSV.x = 4.0 + (RGB.r - RGB.g) / diff;
        }
    }
    else
    {
        HSV.x = 0; // Hue is 0 if there is no difference (gray color)
    }

    // Wrap hue around the color wheel and normalize to [0, 1]
    HSV.x = frac(HSV.x / 6.0);

    return HSV;
}

float3 LookupPaletteColor(float3 baseColor, TEXTURE2D_PARAM(paletteTex, sampler_paletteTex), float4 paletteTexelSize)
{
    float3 bestColor = SAMPLE_TEXTURE2D(paletteTex, sampler_paletteTex, float2(0,0)).rgb;
    float bestDiff = 10000.0; 

    // Loop through each color in the palette texture
    for (int p = 0; p < (int)paletteTexelSize.z; p++)
    {
        float2 paletteUV = float2(float(p) * paletteTexelSize.x, 0.0);
        float3 palCol = SAMPLE_TEXTURE2D(paletteTex, sampler_paletteTex, paletteUV).rgb;
        float diff = 0;
        
        #ifdef _DIFF_CIELAB
            diff = distance(RGBToLAB(baseColor), RGBToLAB(palCol));
        #elif _DIFF_SRGB
            diff = distance(RGBLinearToSRGB(baseColor), RGBLinearToSRGB(palCol));
        #elif _DIFF_LENGTH
            diff = distance(baseColor, palCol);
        #elif _DIFF_LUMINANCE
            const float3 W = float3(0.2126, 0.7152, 0.0722);
            diff = abs(dot(baseColor.rgb, W) - dot(palCol.rgb, W));
        #elif _DIFF_HSV
            diff = distance(RGBtoHSV(baseColor), RGBtoHSV(palCol));
        #endif

        bestColor = lerp(bestColor, palCol, step(diff, bestDiff));
        bestDiff = min(bestDiff, diff);
    }
    
    return bestColor;
}

#endif