// Hologram.fx
// Sci-fi hologram effect with scanlines, flicker, and glitch
// Shader ID: PerEntityHologram

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
    MinFilter = Point;
    MagFilter = Point;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Time = 0.0;
float3 HoloColor = float3(0.3, 0.7, 1.0);  // Cyan hologram tint
float ScanlineIntensity = 0.3;
float ScanlineSpeed = 2.0;
float ScanlineCount = 30.0;
float FlickerSpeed = 15.0;
float FlickerIntensity = 0.15;
float GlitchIntensity = 0.3;
float Transparency = 0.7;
float2 SpriteSize = float2(32.0, 32.0);

float hash(float n)
{
    return frac(sin(n) * 43758.5453);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Glitch offset - occasional horizontal displacement
    float glitchTime = floor(Time * 10.0);
    float glitchRand = hash(glitchTime);

    if (glitchRand > 0.92)
    {
        float glitchY = hash(glitchTime + 1.0);
        float glitchHeight = 0.1;

        if (abs(uv.y - glitchY) < glitchHeight)
        {
            float offset = (hash(glitchTime + 2.0) - 0.5) * GlitchIntensity;
            uv.x += offset;
        }
    }

    // RGB split during glitch
    float rgbSplit = 0.0;
    if (glitchRand > 0.95)
    {
        rgbSplit = 0.02;
    }

    float4 pixelColor;
    if (rgbSplit > 0.0)
    {
        float r = tex2D(SpriteTextureSampler, uv + float2(rgbSplit, 0)).r;
        float g = tex2D(SpriteTextureSampler, uv).g;
        float b = tex2D(SpriteTextureSampler, uv - float2(rgbSplit, 0)).b;
        float a = tex2D(SpriteTextureSampler, uv).a;
        pixelColor = float4(r, g, b, a);
    }
    else
    {
        pixelColor = tex2D(SpriteTextureSampler, uv);
    }

    if (pixelColor.a < 0.01)
    {
        return float4(0, 0, 0, 0);
    }

    // Convert to hologram color (desaturate and tint)
    float luminance = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    float3 holoBase = lerp(float3(luminance, luminance, luminance), HoloColor, 0.7);
    holoBase = holoBase * 0.5 + HoloColor * luminance * 0.8;

    // Scrolling scanlines
    float scanline = sin((input.TextureCoordinates.y * ScanlineCount + Time * ScanlineSpeed) * 6.28318);
    scanline = scanline * 0.5 + 0.5;
    scanline = pow(scanline, 2.0);
    holoBase *= 1.0 - (scanline * ScanlineIntensity);

    // Horizontal scan line (single bright line scrolling down)
    float scanY = frac(Time * 0.5);
    float scanDist = abs(input.TextureCoordinates.y - scanY);
    float brightScan = exp(-scanDist * 30.0) * 0.5;
    holoBase += HoloColor * brightScan;

    // Flicker effect
    float flicker = sin(Time * FlickerSpeed) * sin(Time * FlickerSpeed * 1.3) * sin(Time * FlickerSpeed * 1.7);
    flicker = flicker * FlickerIntensity + (1.0 - FlickerIntensity);
    holoBase *= flicker;

    // Edge glow
    float2 texelSize = 1.0 / SpriteSize;
    float edgeGlow = 0.0;

    for (int i = 0; i < 8; i++)
    {
        float angle = float(i) * 0.785398; // 45 degree increments
        float2 offset = float2(cos(angle), sin(angle)) * texelSize * 2.0;
        float neighbor = tex2D(SpriteTextureSampler, input.TextureCoordinates + offset).a;
        if (neighbor < 0.5)
        {
            edgeGlow += 0.125;
        }
    }
    holoBase += HoloColor * edgeGlow * 0.5;

    // Apply transparency
    float alpha = pixelColor.a * Transparency * flicker;

    // Add subtle noise
    float noise = hash(input.TextureCoordinates.x * 1000.0 + input.TextureCoordinates.y * 1000.0 + Time * 100.0);
    holoBase += (noise - 0.5) * 0.1;

    return float4(holoBase, alpha) * input.Color;
}

technique Hologram
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
