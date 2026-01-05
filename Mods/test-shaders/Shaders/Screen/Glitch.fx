// Glitch.fx - VHS/Digital Glitch Effect
// Retro VHS distortion with RGB splitting, scanline jitter, and static
// Shader ID: CombinedLayerGlitch

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

// Parameters
float Time = 0.0;
float GlitchIntensity = 0.8;
float ScanlineJitter = 0.02;
float ColorDrift = 0.01;
float StaticIntensity = 0.1;
float RGBSplitAmount = 0.005;
float2 ScreenSize = float2(800.0, 600.0);

// Random hash functions
float hash(float n)
{
    return frac(sin(n) * 43758.5453123);
}

float hash2(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Time-based glitch trigger (random intense glitches)
    float glitchTime = floor(Time * 10.0);
    float glitchRandom = hash(glitchTime);
    float glitchActive = step(0.85, glitchRandom) * GlitchIntensity;

    // Scanline-based horizontal jitter
    float scanline = floor(uv.y * ScreenSize.y);
    float lineRandom = hash(scanline + glitchTime * 100.0);

    float jitter = 0.0;
    if (lineRandom > 0.98 && glitchActive > 0.0)
    {
        jitter = (hash(scanline + Time) - 0.5) * ScanlineJitter * 4.0;
    }
    else if (lineRandom > 0.95)
    {
        jitter = (hash(scanline + Time) - 0.5) * ScanlineJitter;
    }

    // Block glitch - shift entire horizontal blocks
    float blockY = floor(uv.y * 20.0);
    float blockRandom = hash(blockY + glitchTime);
    float blockShift = 0.0;
    if (blockRandom > 0.9 && glitchActive > 0.0)
    {
        blockShift = (hash(blockY + Time * 50.0) - 0.5) * 0.1 * glitchActive;
    }

    // Apply jitter and block shift
    float2 distortedUV = uv;
    distortedUV.x += jitter + blockShift;

    // Vertical hold issue (VHS tracking)
    float verticalHold = sin(Time * 0.5) * 0.002 * GlitchIntensity;
    distortedUV.y += verticalHold;

    // Color drift over time (VHS color bleed)
    float drift = sin(Time * 3.0 + uv.y * 10.0) * ColorDrift * GlitchIntensity;

    // RGB Split with glitch intensification
    float splitAmount = RGBSplitAmount * (1.0 + glitchActive * 3.0);

    // Sample with RGB separation
    float r = tex2D(SpriteTextureSampler, clamp(distortedUV + float2(splitAmount + drift, 0.0), 0.0, 1.0)).r;
    float g = tex2D(SpriteTextureSampler, clamp(distortedUV, 0.0, 1.0)).g;
    float b = tex2D(SpriteTextureSampler, clamp(distortedUV - float2(splitAmount + drift, 0.0), 0.0, 1.0)).b;
    float a = tex2D(SpriteTextureSampler, clamp(distortedUV, 0.0, 1.0)).a;

    float3 color = float3(r, g, b);

    // VHS Scanlines
    float scanlineEffect = sin(uv.y * ScreenSize.y * 2.0) * 0.04;
    color -= scanlineEffect;

    // Static noise
    float staticNoise = hash2(uv * ScreenSize + frac(Time * 100.0));
    staticNoise = (staticNoise - 0.5) * 2.0 * StaticIntensity;

    // Intensify static during glitch
    if (glitchActive > 0.0)
    {
        staticNoise *= (1.0 + glitchActive * 2.0);
    }
    color += staticNoise;

    // VHS color degradation (slight desaturation + warm tint)
    float luma = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(color, float3(luma, luma, luma), 0.1 * GlitchIntensity);
    color *= float3(1.05, 1.0, 0.95); // Slight warm/magenta tint

    // Tape noise bars (horizontal interference)
    float noiseBar = step(0.99, hash(floor(uv.y * 50.0) + floor(Time * 20.0)));
    if (noiseBar > 0.0 && glitchActive > 0.0)
    {
        color = lerp(color, float3(1.0, 1.0, 1.0), 0.3);
    }

    color = saturate(color);
    return float4(color, a) * input.Color;
}

technique Glitch
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
