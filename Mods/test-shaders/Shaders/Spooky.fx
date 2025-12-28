// Spooky Screen Shader
// Combines: vignette, color grading, chromatic aberration, pulsing darkness, film grain, fog wisps
// Creates an eerie, unsettling atmosphere perfect for horror/ghost scenes

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Texture samplers
Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
};

// Parameters
float Time = 0.0;                          // Elapsed time for animations
float2 ScreenSize = float2(1280, 720);     // Screen dimensions

// Vignette parameters
float VignetteIntensity = 0.8;             // How dark the edges get (0-1)
float VignetteRadius = 0.75;               // Where vignette starts (0-1, center to edge)
float VignetteSoftness = 0.45;             // How soft the vignette edge is

// Color grading parameters
float Desaturation = 0.5;                  // Amount of color removed (0-1)
float3 TintColor = float3(0.7, 0.5, 1.0);  // Purple/ghostly tint (stronger purple)
float TintStrength = 0.5;                  // How much tint to apply

// Chromatic aberration
float ChromaticAberration = 0.003;         // RGB channel offset amount
float ChromaticPulse = 0.5;                // How much aberration pulses

// Pulsing darkness
float DarknessPulseSpeed = 1.5;            // Speed of darkness pulse
float DarknessPulseAmount = 0.15;          // How much darkness pulses

// Film grain
float GrainIntensity = 0.08;               // Grain visibility (0-1)
float GrainSpeed = 15.0;                   // How fast grain animates

// Fog wisps
float FogIntensity = 0.35;                 // Fog visibility (increased)
float FogSpeed = 0.5;                      // Fog movement speed (faster)
float FogScale = 2.0;                      // Fog pattern scale (larger wisps)

// Pixel shader input
struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// Simple hash function for noise
float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise function
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    // Smooth interpolation
    float2 u = f * f * (3.0 - 2.0 * f);

    // Four corners
    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Fractal Brownian Motion for fog
float fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < 4; i++)
    {
        value += amplitude * noise(p * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }

    return value;
}

// Pixel shader - main effect
float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);

    // === CHROMATIC ABERRATION ===
    // Pulse the aberration for an unsettling effect
    float aberrationAmount = ChromaticAberration * (1.0 + sin(Time * 3.0) * ChromaticPulse);
    float2 fromCenter = uv - center;
    float dist = length(fromCenter);
    // Avoid divide by zero when at exact center
    float2 direction = dist > 0.0001 ? fromCenter / dist : float2(0.0, 0.0);

    // Sample RGB channels with offset (stronger at edges)
    float2 rOffset = direction * aberrationAmount * dist;
    float2 bOffset = -direction * aberrationAmount * dist;

    float r = tex2D(SpriteTextureSampler, uv + rOffset).r;
    float g = tex2D(SpriteTextureSampler, uv).g;
    float b = tex2D(SpriteTextureSampler, uv + bOffset).b;
    float a = tex2D(SpriteTextureSampler, uv).a;

    float3 color = float3(r, g, b);

    // === DESATURATION ===
    float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(color, float3(luminance, luminance, luminance), Desaturation);

    // === COLOR TINT ===
    // Apply ghostly purple/green tint
    color = lerp(color, color * TintColor, TintStrength);

    // === PULSING DARKNESS ===
    // Create an unsettling breathing darkness effect
    float darknessPulse = sin(Time * DarknessPulseSpeed) * 0.5 + 0.5;
    float darknessMultiplier = 1.0 - (darknessPulse * DarknessPulseAmount);
    color *= darknessMultiplier;

    // === FOG WISPS ===
    // Animated fog that drifts across the screen
    float2 fogUV = uv * FogScale;
    fogUV.x += Time * FogSpeed;
    fogUV.y += sin(Time * 0.5) * 0.2;

    float fog = fbm(fogUV);
    fog = smoothstep(0.3, 0.7, fog);

    // Secondary fog layer moving opposite direction
    float2 fogUV2 = uv * FogScale * 1.5;
    fogUV2.x -= Time * FogSpeed * 0.7;
    fogUV2.y += cos(Time * 0.3) * 0.15;

    float fog2 = fbm(fogUV2 + 100.0);
    fog2 = smoothstep(0.4, 0.8, fog2);

    float totalFog = max(fog, fog2) * FogIntensity;

    // Add fog as a ghostly white/purple overlay
    float3 fogColor = float3(0.7, 0.6, 0.9);
    color = lerp(color, fogColor, totalFog);

    // === VIGNETTE ===
    // Dark edges that pulse slightly
    float vignetteDist = length(uv - center);
    float vignettePulse = 1.0 + sin(Time * 2.0) * 0.1;
    float vignetteEffect = smoothstep(VignetteRadius * vignettePulse, VignetteRadius * vignettePulse - VignetteSoftness, vignetteDist);
    vignetteEffect = lerp(1.0, vignetteEffect, VignetteIntensity);
    color *= vignetteEffect;

    // === FILM GRAIN ===
    // Adds texture and unease
    float2 grainUV = uv * ScreenSize;
    float grain = hash(grainUV + frac(Time * GrainSpeed));
    grain = (grain - 0.5) * 2.0 * GrainIntensity;
    color += grain;

    // === OCCASIONAL FLICKER ===
    // Random subtle brightness flicker
    float flicker = 1.0;
    float flickerRandom = hash(float2(floor(Time * 10.0), 0.0));
    if (flickerRandom > 0.95)
    {
        flicker = 0.9 + flickerRandom * 0.1;
    }
    color *= flicker;

    // Clamp and return
    color = saturate(color);
    return float4(color, a) * input.Color;
}

technique Spooky
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
