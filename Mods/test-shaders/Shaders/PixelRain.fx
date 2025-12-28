// PixelRain.fx
// Layer 3: Digital pixel rain with wind turbulence and spiral drift
// DESIGNED FOR STACKING - uses noise-based wind, not just vertical falling

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

// Rain parameters
float PixelDensity = 45.0;
float FallSpeed = 1.8;
float TrailLength = 0.35;

// Color parameters
float3 PrimaryColor = float3(1.0, 0.4, 0.9);
float3 SecondaryColor = float3(0.4, 1.0, 1.0);
float3 AccentColor = float3(0.9, 0.9, 1.0);

// Effect parameters
float RainOpacity = 0.18;
float GlowIntensity = 0.6;
float Sparkle = 0.4;

#define PI 3.14159265359

// Hash functions
float hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Smooth noise
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash21(i);
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Curl noise for swirling wind effect
float2 curlNoise(float2 p)
{
    float eps = 0.01;
    float n = noise(p);
    float nx = noise(p + float2(eps, 0.0));
    float ny = noise(p + float2(0.0, eps));

    // Curl is perpendicular to gradient
    return float2(ny - n, -(nx - n)) / eps;
}

// Calculate rain particle with wind turbulence
float3 calculateRainParticle(float2 uv, float columnIndex, float columnSeed, float2 windOffset)
{
    float columnWidth = 1.0 / PixelDensity;
    float columnCenter = (columnIndex + 0.5) * columnWidth;

    // Apply wind to column position
    float windedCenter = columnCenter + windOffset.x * 0.02;

    // Distance from column center
    float xDist = abs(uv.x - windedCenter);
    float inColumn = smoothstep(columnWidth * 0.5, columnWidth * 0.1, xDist);

    if (inColumn < 0.01) return float3(0, 0, 0);

    // Random properties
    float speed = 0.5 + columnSeed * 0.8;
    float phase = columnSeed * 100.0;
    float brightness = 0.6 + hash11(columnIndex * 7.3) * 0.4;

    // Falling position with wind-influenced path
    float baseY = frac(Time * FallSpeed * speed + phase);
    float windY = windOffset.y * 0.01;
    float fallPos = frac(baseY + windY);

    // Distance from falling head
    float yDist = uv.y - fallPos;
    if (yDist < 0.0) yDist += 1.0;

    // Pixelated effect
    float pixelY = frac(uv.y * PixelDensity * 0.5);
    float pixelMask = step(0.3, pixelY) * step(pixelY, 0.7);

    // Head of the rain drop
    float head = smoothstep(0.025, 0.0, yDist);
    head = head * head * 1.5;

    // Trail with curve influenced by wind
    float trail = 1.0 - yDist / TrailLength;
    trail = saturate(trail);
    trail = trail * trail * trail;

    float intensity = max(head, trail * 0.4);
    intensity *= inColumn * brightness;
    intensity *= pixelMask * 0.3 + 0.7;

    // Color based on column and time
    float colorMix = hash11(columnIndex * 3.7 + floor(Time * 1.5) * 0.1);
    float3 rainColor;

    if (colorMix < 0.5)
    {
        rainColor = lerp(PrimaryColor, SecondaryColor, colorMix * 2.0);
    }
    else
    {
        rainColor = lerp(SecondaryColor, AccentColor, (colorMix - 0.5) * 2.0);
    }

    // Sparkle at head
    float sparklePhase = hash11(columnIndex + floor(Time * 8.0));
    if (head > 0.3 && sparklePhase > 0.7)
    {
        rainColor = lerp(rainColor, AccentColor, Sparkle);
        intensity *= 1.4;
    }

    return rainColor * intensity;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Sample base texture
    float4 tex = tex2D(SpriteTextureSampler, uv);
    float3 color = tex.rgb;

    // Calculate wind field using curl noise (creates swirling patterns)
    float2 windCoord = uv * 2.0 + Time * 0.1;
    float2 wind = curlNoise(windCoord) * 1.5;

    // Additional turbulence layer
    float2 turbCoord = uv * 5.0 + Time * 0.2;
    float2 turb = curlNoise(turbCoord) * 0.5;
    wind += turb;

    // Calculate which column cell we're in
    float columnWidth = 1.0 / PixelDensity;
    float columnIndex = floor(uv.x / columnWidth);

    // Accumulate rain from nearby columns
    float3 rainAccum = float3(0, 0, 0);

    [unroll]
    for (int dx = -1; dx <= 1; dx++)
    {
        float neighborColumn = columnIndex + float(dx);

        // Wrap column index
        if (neighborColumn < 0.0) neighborColumn += PixelDensity;
        if (neighborColumn >= PixelDensity) neighborColumn -= PixelDensity;

        float columnSeed = hash11(neighborColumn * 1.7);

        // 60% of columns active
        if (columnSeed < 0.6)
        {
            // Each column gets slightly different wind
            float2 colWind = wind * (0.8 + columnSeed * 0.4);
            rainAccum += calculateRainParticle(uv, neighborColumn, columnSeed, colWind);
        }
    }

    // Spiral rain clusters - particles that swirl around points
    float2 spiralCenter = float2(
        0.5 + noise(float2(Time * 0.1, 0.0)) * 0.4 - 0.2,
        0.5 + noise(float2(0.0, Time * 0.1)) * 0.4 - 0.2
    );

    float2 toSpiral = uv - spiralCenter;
    float spiralDist = length(toSpiral);
    float spiralAngle = atan2(toSpiral.y, toSpiral.x);

    // Spiral arm pattern
    float spiralArm = frac(spiralAngle / (2.0 * PI) * 3.0 - spiralDist * 4.0 + Time * 0.5);
    float spiralIntensity = smoothstep(0.4, 0.5, spiralArm) * smoothstep(0.6, 0.5, spiralArm);
    spiralIntensity *= smoothstep(0.5, 0.1, spiralDist); // Fade at edges
    spiralIntensity *= smoothstep(0.0, 0.05, spiralDist); // Fade at center

    float3 spiralColor = lerp(PrimaryColor, SecondaryColor, frac(spiralAngle / PI));
    rainAccum += spiralColor * spiralIntensity * 0.3;

    // Diagonal streaks
    float streakAngle = 0.7; // ~40 degrees
    float streakPos = uv.x * streakAngle + uv.y;
    float streakIndex = floor(streakPos * 20.0);
    float streakSeed = hash11(streakIndex * 3.14);

    if (streakSeed > 0.85)
    {
        float streakPhase = frac(streakPos * 20.0);
        float streakFall = frac(Time * FallSpeed * 0.5 + streakSeed * 10.0);
        float streakDist = abs(streakPhase - streakFall);
        float streak = smoothstep(0.1, 0.0, streakDist);
        streak *= smoothstep(0.0, 0.05, streakFall); // Fade in

        float3 streakColor = lerp(AccentColor, PrimaryColor, streakSeed);
        rainAccum += streakColor * streak * 0.5;
    }

    // Apply glow effect to rain
    float3 glowRain = rainAccum * GlowIntensity;

    // Add rain overlay
    color += glowRain * RainOpacity;

    // Subtle static noise
    float staticNoise = hash21(uv * ScreenSize + frac(Time * 30.0));
    staticNoise = (staticNoise - 0.5) * 0.015;
    color += staticNoise * length(rainAccum);

    // Ambient color pulse based on curl noise
    float ambientPulse = noise(float2(Time * 0.5, 0.0)) * 0.03;
    float3 ambientColor = lerp(PrimaryColor, SecondaryColor, noise(float2(Time * 0.2, 1.0)));
    color += ambientColor * ambientPulse;

    // Subtle edge darkening
    float2 edgeUV = uv - 0.5;
    float edge = 1.0 - dot(edgeUV, edgeUV) * 0.15;
    color *= edge;

    return float4(saturate(color), tex.a) * input.Color;
}

technique PixelRain
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
