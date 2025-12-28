// Fireflies.fx
// Magical floating firefly/sparkle particle overlay effect
// DESIGNED FOR STACKING - semi-transparent additive overlay of glowing particles
// Perfect for mystical forest, enchanted areas, magical night scenes

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

// Configurable parameters
float Time = 0.0;
float2 ScreenSize = float2(1280.0, 720.0);

// Particle behavior
float ParticleDensity = 40.0;       // Number of particle cells across screen
float FloatSpeed = 0.15;            // Upward drift speed
float DriftAmount = 0.03;           // Horizontal sine wave drift magnitude
float DriftSpeed = 1.2;             // Horizontal drift oscillation speed

// Glow and size
float ParticleSize = 0.012;         // Base particle radius (in UV space)
float SizeVariation = 0.6;          // Random size variation (0-1)
float GlowFalloff = 2.5;            // Soft glow falloff exponent
float GlowIntensity = 0.8;          // Overall glow brightness

// Twinkling animation
float TwinkleSpeed = 3.0;           // Brightness pulse speed
float TwinkleAmount = 0.5;          // Brightness variation (0-1)

// Colors
float3 BaseColor = float3(1.0, 0.95, 0.4);      // Warm yellow
float3 AccentColor1 = float3(1.0, 0.8, 0.2);   // Golden
float3 AccentColor2 = float3(0.4, 1.0, 0.7);   // Cyan/green magic

// Overlay blend
float OverlayOpacity = 0.7;         // Overall effect opacity

#define PI 3.14159265359

// Procedural hash functions for randomness
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

float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Calculate single firefly particle contribution
float3 calculateFirefly(float2 uv, float2 cellId, float cellSeed)
{
    // Random values for this particle
    float2 randoms = hash22(cellId);
    float lifeSeed = hash21(cellId + float2(123.456, 789.012));
    float colorSeed = hash21(cellId + float2(456.789, 123.456));

    // Particle lifecycle - some appear/disappear over time
    float lifePhase = frac(lifeSeed + Time * 0.1);
    float lifeActive = smoothstep(0.0, 0.1, lifePhase) * smoothstep(1.0, 0.8, lifePhase);

    // Skip inactive particles early
    if (lifeActive < 0.01) return float3(0, 0, 0);

    // Base position within cell (0-1 range)
    float2 basePos = randoms;

    // Upward floating motion with wrap-around
    float floatOffset = frac(Time * FloatSpeed * (0.5 + randoms.y * 0.5) + lifeSeed * 10.0);

    // Horizontal sine wave drift
    float driftPhase = Time * DriftSpeed * (0.7 + randoms.x * 0.6) + lifeSeed * PI * 2.0;
    float drift = sin(driftPhase) * DriftAmount * (0.5 + randoms.x * 0.5);

    // Secondary smaller drift for more organic movement
    float drift2 = sin(driftPhase * 2.3 + 1.5) * DriftAmount * 0.3;

    // Calculate animated particle position
    float2 particlePos = basePos;
    particlePos.y = frac(particlePos.y - floatOffset); // Float upward
    particlePos.x += drift + drift2;

    // Convert cell-relative position to UV space
    float cellSize = 1.0 / ParticleDensity;
    float2 particleUV = cellId * cellSize + particlePos * cellSize;

    // Distance from current pixel to particle center
    float dist = length(uv - particleUV);

    // Particle size with variation
    float size = ParticleSize * (1.0 - SizeVariation + randoms.y * SizeVariation);

    // Twinkling brightness animation
    float twinklePhase = Time * TwinkleSpeed * (0.8 + randoms.x * 0.4) + lifeSeed * PI * 2.0;
    float twinkle = sin(twinklePhase) * 0.5 + 0.5;
    float brightness = 1.0 - TwinkleAmount + twinkle * TwinkleAmount;

    // Occasional bright "flash" for extra magic
    float flashPhase = frac(Time * 0.5 + lifeSeed * 7.0);
    float flash = pow(smoothstep(0.9, 1.0, flashPhase) * smoothstep(1.0, 0.95, flashPhase + 0.05), 2.0);
    brightness += flash * 2.0;

    // Soft glow falloff (exponential for that magical look)
    float glow = 1.0 - saturate(dist / size);
    glow = pow(glow, GlowFalloff);
    glow *= brightness * lifeActive * GlowIntensity;

    // Color selection based on random seed
    float3 particleColor;
    if (colorSeed < 0.6)
    {
        // Majority: warm yellow base
        particleColor = BaseColor;
    }
    else if (colorSeed < 0.85)
    {
        // Some: golden accent
        particleColor = AccentColor1;
    }
    else
    {
        // Rare: cyan/green magical accent
        particleColor = AccentColor2;
    }

    // Slight color shift over time for subtle variation
    float colorShift = sin(Time * 0.3 + lifeSeed * 5.0) * 0.1;
    particleColor.r += colorShift * 0.2;
    particleColor.g += colorShift * 0.1;

    return particleColor * glow;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 tex = tex2D(SpriteTextureSampler, uv);
    float3 color = tex.rgb;

    // Aspect ratio correction for consistent particle shapes
    float2 aspectUV = uv;
    aspectUV.x *= ScreenSize.x / ScreenSize.y;

    // Calculate which cell this pixel is in
    float cellSize = 1.0 / ParticleDensity;
    float2 cellId = floor(uv / cellSize);

    // Accumulate firefly contributions from nearby cells
    // Check 3x3 neighborhood for overlapping glow
    float3 fireflyAccum = float3(0, 0, 0);

    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            float2 neighborCell = cellId + float2(dx, dy);

            // Wrap cells for seamless tiling
            neighborCell = fmod(neighborCell + ParticleDensity, ParticleDensity);

            float cellSeed = hash21(neighborCell);

            // Only spawn particle in ~60% of cells for natural distribution
            if (cellSeed > 0.4)
            {
                fireflyAccum += calculateFirefly(uv, neighborCell, cellSeed);
            }
        }
    }

    // Add some extra sparse "hero" fireflies that are larger and brighter
    float heroDensity = ParticleDensity * 0.3; // Fewer hero particles
    float2 heroCellId = floor(uv / (1.0 / heroDensity));

    [unroll]
    for (int hy = -1; hy <= 1; hy++)
    {
        [unroll]
        for (int hx = -1; hx <= 1; hx++)
        {
            float2 heroCell = heroCellId + float2(hx, hy);
            heroCell = fmod(heroCell + heroDensity, heroDensity);

            float heroSeed = hash21(heroCell + float2(999.0, 777.0));

            // Only 15% of hero cells get particles
            if (heroSeed > 0.85)
            {
                // Hero particles are 2x size and brighter
                float2 heroRandoms = hash22(heroCell + float2(333.0, 444.0));
                float heroLifeSeed = hash21(heroCell + float2(555.0, 666.0));

                // Similar animation but slower and larger
                float2 heroPos = heroRandoms;
                float heroFloat = frac(Time * FloatSpeed * 0.7 + heroLifeSeed * 10.0);
                float heroDrift = sin(Time * DriftSpeed * 0.6 + heroLifeSeed * PI * 2.0) * DriftAmount * 1.5;

                heroPos.y = frac(heroPos.y - heroFloat);
                heroPos.x += heroDrift;

                float2 heroUV = heroCell * (1.0 / heroDensity) + heroPos * (1.0 / heroDensity);
                float heroDist = length(uv - heroUV);
                float heroSize = ParticleSize * 2.0;

                // Slower, more dramatic twinkle
                float heroTwinkle = sin(Time * TwinkleSpeed * 0.5 + heroLifeSeed * PI * 2.0) * 0.5 + 0.5;
                float heroBrightness = 0.7 + heroTwinkle * 0.5;

                float heroGlow = 1.0 - saturate(heroDist / heroSize);
                heroGlow = pow(heroGlow, GlowFalloff * 0.8); // Softer falloff for larger glow
                heroGlow *= heroBrightness * GlowIntensity * 1.2;

                // Hero particles are always warm golden
                float3 heroColor = lerp(BaseColor, AccentColor1, heroTwinkle * 0.3);
                fireflyAccum += heroColor * heroGlow;
            }
        }
    }

    // Subtle overall ambient glow variation
    float ambientPulse = sin(Time * 0.2) * 0.05 + 0.05;
    fireflyAccum *= (1.0 + ambientPulse);

    // Apply firefly overlay with additive blending
    color += fireflyAccum * OverlayOpacity;

    // Very subtle warm tint to the whole scene when fireflies are present
    float fireflyPresence = saturate(length(fireflyAccum) * 0.5);
    color = lerp(color, color * float3(1.02, 1.0, 0.98), fireflyPresence * 0.1);

    return float4(saturate(color), tex.a) * input.Color;
}

technique Fireflies
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
