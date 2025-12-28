// Noir.fx
// Film noir effect - high contrast B&W with dramatic shadows
// Shader ID: CombinedLayerNoir

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
float Contrast = 1.8;
float Brightness = -0.1;
float VignetteIntensity = 0.7;
float GrainAmount = 0.08;
float3 ShadowTint = float3(0.1, 0.1, 0.15);   // Slight blue in shadows
float3 HighlightTint = float3(1.0, 0.98, 0.95); // Warm highlights
float2 ScreenSize = float2(1280.0, 720.0);

float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 pixelColor = tex2D(SpriteTextureSampler, uv);

    // Convert to luminance (B&W)
    float luminance = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));

    // Apply contrast curve (S-curve for dramatic look)
    luminance = luminance + Brightness;
    luminance = (luminance - 0.5) * Contrast + 0.5;
    luminance = saturate(luminance);

    // Crush blacks and blow highlights slightly
    luminance = smoothstep(0.05, 0.95, luminance);

    // Apply tinting based on luminance
    float3 finalColor;
    if (luminance < 0.5)
    {
        // Dark areas get shadow tint
        float t = luminance * 2.0;
        finalColor = lerp(ShadowTint * 0.1, float3(0.5, 0.5, 0.5), t);
    }
    else
    {
        // Bright areas get highlight tint
        float t = (luminance - 0.5) * 2.0;
        finalColor = lerp(float3(0.5, 0.5, 0.5), HighlightTint, t);
    }

    // Vignette
    float2 vignetteUV = uv - 0.5;
    float vignette = 1.0 - dot(vignetteUV, vignetteUV) * VignetteIntensity * 2.0;
    vignette = saturate(vignette);
    vignette = vignette * vignette; // Squared for smoother falloff
    finalColor *= vignette;

    // Film grain - use hash based on position and time
    float grain = hash(uv * ScreenSize + frac(Time * 0.1) * 1000.0);
    grain = (grain - 0.5) * GrainAmount;
    finalColor += grain;

    // Occasional film scratches (vertical lines)
    float scratchX = hash(float2(floor(Time * 3.0), 0.0));
    float scratchPos = scratchX;
    float scratchWidth = 0.002;
    if (abs(uv.x - scratchPos) < scratchWidth)
    {
        float scratchIntensity = hash(float2(floor(Time * 3.0), 1.0));
        if (scratchIntensity > 0.7)
        {
            finalColor += 0.3 * (1.0 - abs(uv.x - scratchPos) / scratchWidth);
        }
    }

    // Dust specks
    float dustCheck = hash(uv * 500.0 + floor(Time * 2.0));
    if (dustCheck > 0.998)
    {
        finalColor += 0.4;
    }

    pixelColor.rgb = saturate(finalColor);
    pixelColor *= input.Color;

    return pixelColor;
}

technique Noir
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
