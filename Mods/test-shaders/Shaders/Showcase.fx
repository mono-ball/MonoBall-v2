// Showcase.fx
// Ultimate combined effect: CRT + Spooky Fog + Glitch + Dream glow
// The "wow factor" shader that shows off everything at once
// Shader ID: CombinedLayerShowcase

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
float2 ScreenSize = float2(1280.0, 720.0);

// Effect intensities (all controllable)
float CRTAmount = 0.7;
float GlitchAmount = 0.4;
float DreamGlow = 0.5;
float VignetteStrength = 0.8;
float ChromaShift = 0.006;
float ScanlineIntensity = 0.15;
float3 TintColor = float3(0.8, 0.6, 1.0); // Purple/pink tint

float hash(float n)
{
    return frac(sin(n) * 43758.5453);
}

float hash2(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 originalUV = uv;

    // === GLITCH DISPLACEMENT ===
    float glitchTime = floor(Time * 15.0);
    float glitchRand = hash(glitchTime);

    // Horizontal slice displacement
    if (GlitchAmount > 0.0 && glitchRand > 0.85)
    {
        float sliceY = hash(glitchTime + 1.0);
        float sliceHeight = 0.05 + hash(glitchTime + 2.0) * 0.1;

        if (abs(uv.y - sliceY) < sliceHeight)
        {
            float displacement = (hash(glitchTime + 3.0) - 0.5) * 0.08 * GlitchAmount;
            uv.x += displacement;
        }
    }

    // === CRT CURVATURE ===
    float2 crtUV = uv;
    if (CRTAmount > 0.0)
    {
        float2 curved = crtUV - 0.5;
        float curvature = 0.08 * CRTAmount;
        curved *= 1.0 + dot(curved, curved) * curvature;
        crtUV = curved + 0.5;
    }

    // === CHROMATIC ABERRATION (RGB Split) ===
    float2 dir = crtUV - 0.5;
    float dist = length(dir);
    float2 offset = dir * dist * ChromaShift;

    // Add glitch-based extra split
    if (GlitchAmount > 0.0 && hash(glitchTime + 10.0) > 0.7)
    {
        offset += float2(hash(glitchTime + 11.0) - 0.5, 0.0) * 0.015 * GlitchAmount;
    }

    float r = tex2D(SpriteTextureSampler, crtUV + offset).r;
    float g = tex2D(SpriteTextureSampler, crtUV).g;
    float b = tex2D(SpriteTextureSampler, crtUV - offset).b;
    float a = tex2D(SpriteTextureSampler, crtUV).a;

    float3 color = float3(r, g, b);

    // === DREAM GLOW (Bloom approximation) ===
    if (DreamGlow > 0.0)
    {
        float3 glow = float3(0, 0, 0);
        float2 blurSize = 3.0 / ScreenSize;

        // Simple 5-tap blur
        glow += tex2D(SpriteTextureSampler, crtUV + float2(-blurSize.x, 0)).rgb;
        glow += tex2D(SpriteTextureSampler, crtUV + float2(blurSize.x, 0)).rgb;
        glow += tex2D(SpriteTextureSampler, crtUV + float2(0, -blurSize.y)).rgb;
        glow += tex2D(SpriteTextureSampler, crtUV + float2(0, blurSize.y)).rgb;
        glow /= 4.0;

        // Add glow for bright areas
        float brightness = dot(glow, float3(0.299, 0.587, 0.114));
        color += glow * brightness * DreamGlow * 0.5;
    }

    // === SCANLINES ===
    if (ScanlineIntensity > 0.0)
    {
        float scanline = sin(originalUV.y * ScreenSize.y * 1.5) * 0.5 + 0.5;
        scanline = pow(scanline, 1.5);
        color *= 1.0 - scanline * ScanlineIntensity;
    }

    // === COLOR TINTING ===
    color = lerp(color, color * TintColor, 0.3);

    // === VIGNETTE ===
    float2 vignetteUV = originalUV - 0.5;
    float vignette = 1.0 - dot(vignetteUV, vignetteUV) * VignetteStrength * 2.0;
    vignette = saturate(vignette);
    vignette = smoothstep(0.0, 1.0, vignette);
    color *= vignette;

    // === FILM GRAIN ===
    float grain = hash2(originalUV * ScreenSize + frac(Time) * 1000.0);
    grain = (grain - 0.5) * 0.06;
    color += grain;

    // === SUBTLE PULSING ===
    float pulse = sin(Time * 2.0) * 0.03 + 1.0;
    color *= pulse;

    // === SPARKLES (Dream effect) ===
    if (DreamGlow > 0.0)
    {
        float sparkle = hash2(floor(originalUV * 200.0) + floor(Time * 4.0));
        if (sparkle > 0.995)
        {
            float twinkle = sin(Time * 20.0 + sparkle * 100.0) * 0.5 + 0.5;
            color += float3(1.0, 0.9, 1.0) * twinkle * DreamGlow;
        }
    }

    // === STATIC NOISE (Glitch) ===
    if (GlitchAmount > 0.0 && hash(glitchTime + 20.0) > 0.9)
    {
        float staticNoise = hash2(originalUV * ScreenSize + glitchTime);
        color = lerp(color, float3(staticNoise, staticNoise, staticNoise), 0.15 * GlitchAmount);
    }

    return float4(saturate(color), a) * input.Color;
}

technique Showcase
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
