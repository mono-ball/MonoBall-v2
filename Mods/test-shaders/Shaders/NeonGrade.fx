// NeonGrade.fx
// Layer 1: Cyberpunk neon color grading - purples, cyans, magentas
// DESIGNED FOR STACKING - subtle but impactful color transformation

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Time = 0.0;
float3 ShadowColor = float3(0.1, 0.0, 0.2);    // Deep purple shadows
float3 MidColor = float3(0.0, 0.8, 0.9);       // Cyan midtones
float3 HighlightColor = float3(1.0, 0.3, 0.8); // Hot pink highlights
float Intensity = 0.6;
float Saturation = 1.3;

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 tex = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float3 color = tex.rgb;

    // Calculate luminance
    float lum = dot(color, float3(0.299, 0.587, 0.114));

    // Three-way color grade based on luminance
    float3 graded;
    if (lum < 0.33)
    {
        float t = lum / 0.33;
        graded = lerp(ShadowColor, MidColor, t);
    }
    else if (lum < 0.66)
    {
        float t = (lum - 0.33) / 0.33;
        graded = lerp(MidColor, HighlightColor, t);
    }
    else
    {
        float t = (lum - 0.66) / 0.34;
        graded = lerp(HighlightColor, float3(1.0, 1.0, 1.0), t);
    }

    // Blend graded color with original, preserving detail
    color = lerp(color, graded * (color + 0.1), Intensity);

    // Boost saturation
    float gray = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(gray, gray, gray), color, Saturation);

    // Subtle animated shimmer on bright areas
    float shimmer = sin(Time * 3.0 + input.TextureCoordinates.x * 10.0) * 0.05;
    if (lum > 0.6)
    {
        color += shimmer * HighlightColor;
    }

    return float4(saturate(color), tex.a) * input.Color;
}

technique NeonGrade
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
