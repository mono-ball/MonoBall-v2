// Aurora.fx - Aurora Borealis / Northern Lights Effect
// Ethereal flowing curtains of light with cyan, green, purple, and pink gradients
// Shader ID: CombinedLayerAurora

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
float AuroraIntensity = 0.35;
float WaveSpeed = 0.4;
float WaveFrequency = 3.0;
float CurtainLayers = 4.0;
float VerticalPosition = 0.25;
float VerticalSpread = 0.35;
float ColorCycleSpeed = 0.15;
float ShimmerIntensity = 0.3;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

// Hash function for pseudo-random values
float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Smooth noise function
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Fractal Brownian Motion for organic flow
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

// Generate aurora curtain shape at a given x position and layer
float auroraCurtain(float x, float t, float layer)
{
    float layerOffset = layer * 1.7;
    float speed = WaveSpeed * (1.0 + layer * 0.1);

    // Multiple sine waves for flowing curtain effect
    float wave = 0.0;
    wave += sin(x * WaveFrequency + t * speed + layerOffset) * 0.4;
    wave += sin(x * WaveFrequency * 1.7 - t * speed * 0.7 + layerOffset * 1.3) * 0.3;
    wave += sin(x * WaveFrequency * 2.3 + t * speed * 0.5 + layerOffset * 0.8) * 0.2;
    wave += sin(x * WaveFrequency * 3.1 - t * speed * 0.3 + layerOffset * 1.5) * 0.1;

    // Add noise for organic variation
    float noiseOffset = fbm(float2(x * 2.0 + t * 0.1, layer * 3.0)) * 0.3;
    wave += noiseOffset;

    return wave * 0.5;
}

// Aurora color palette - cycles through ethereal northern lights colors
float3 auroraColor(float t, float intensity, float layer)
{
    // Base colors: cyan, green, magenta, blue
    float3 cyan = float3(0.2, 0.9, 0.95);
    float3 green = float3(0.3, 0.95, 0.4);
    float3 purple = float3(0.6, 0.2, 0.8);
    float3 pink = float3(0.95, 0.3, 0.6);
    float3 blue = float3(0.3, 0.4, 0.95);

    // Cycle through colors based on time and position
    float colorPhase = t * ColorCycleSpeed + layer * 0.4;
    float cycle = frac(colorPhase);

    float3 color;
    if (cycle < 0.25)
    {
        color = lerp(green, cyan, cycle * 4.0);
    }
    else if (cycle < 0.5)
    {
        color = lerp(cyan, purple, (cycle - 0.25) * 4.0);
    }
    else if (cycle < 0.75)
    {
        color = lerp(purple, pink, (cycle - 0.5) * 4.0);
    }
    else
    {
        color = lerp(pink, green, (cycle - 0.75) * 4.0);
    }

    // Add some blue undertones
    color = lerp(color, blue, 0.15);

    // Intensity modulation
    color *= intensity;

    return color;
}

// Calculate aurora contribution for a single layer
float3 auroraLayer(float2 uv, float t, float layer, out float mask)
{
    float layerDepth = 1.0 - (layer / CurtainLayers) * 0.4;
    float layerScale = 1.0 + layer * 0.3;

    // Get curtain wave position
    float curtainWave = auroraCurtain(uv.x * layerScale, t, layer);

    // Aurora appears in upper portion of screen with wave distortion
    float baseY = VerticalPosition + curtainWave * 0.15;
    float spread = VerticalSpread * layerDepth;

    // Distance from aurora center line
    float distFromAurora = abs(uv.y - baseY);

    // Create soft, flowing curtain shape
    float curtainShape = smoothstep(spread, 0.0, distFromAurora);

    // Add vertical streaks (rays of light)
    float rayFreq = 15.0 + layer * 5.0;
    float rayPhase = t * 0.3 + layer;
    float rays = sin(uv.x * rayFreq + rayPhase) * 0.5 + 0.5;
    rays = pow(rays, 2.0);

    // Vertical gradient - brighter at top of curtain
    float vertGradient = 1.0 - smoothstep(0.0, spread * 0.7, distFromAurora);
    vertGradient = pow(vertGradient, 1.5);

    // Combine shape and rays
    float intensity = curtainShape * (0.6 + rays * 0.4) * vertGradient;
    intensity *= layerDepth;

    // Add shimmer
    float shimmer = noise(float2(uv.x * 50.0 + t * 2.0, uv.y * 30.0 - t));
    shimmer = shimmer * ShimmerIntensity * intensity;
    intensity += shimmer;

    // Edge fade for soft appearance
    float edgeFade = smoothstep(0.0, 0.15, uv.x) * smoothstep(1.0, 0.85, uv.x);
    intensity *= edgeFade;

    mask = intensity;

    // Get color for this layer
    float3 color = auroraColor(t + uv.x * 2.0, intensity, layer);

    return color;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Sample the original scene
    float4 sceneColor = tex2D(SpriteTextureSampler, uv);
    float3 color = sceneColor.rgb;

    // Accumulate aurora from multiple layers
    float3 auroraTotal = float3(0.0, 0.0, 0.0);
    float totalMask = 0.0;

    for (int i = 0; i < 4; i++)
    {
        float layerMask;
        float3 layerColor = auroraLayer(uv, Time, (float)i, layerMask);

        // Additive blending with depth-based opacity
        float layerOpacity = 1.0 - ((float)i / CurtainLayers) * 0.3;
        auroraTotal += layerColor * layerOpacity;
        totalMask = max(totalMask, layerMask * layerOpacity);
    }

    // Apply intensity control
    auroraTotal *= AuroraIntensity;
    totalMask *= AuroraIntensity;

    // Add subtle glow around aurora
    float glowRadius = VerticalSpread * 1.5;
    float glowDist = abs(uv.y - VerticalPosition);
    float glow = smoothstep(glowRadius, 0.0, glowDist) * 0.1;
    float3 glowColor = float3(0.3, 0.6, 0.5) * glow;

    // Blend aurora with scene using additive blending for light effect
    color += auroraTotal + glowColor;

    // Add very subtle color tint to the entire scene when aurora is visible
    float3 ambientTint = float3(0.95, 1.0, 1.02);
    color *= lerp(float3(1.0, 1.0, 1.0), ambientTint, totalMask * 0.3);

    // Enhance stars/bright points near aurora (optional sparkle)
    float luminance = dot(sceneColor.rgb, float3(0.299, 0.587, 0.114));
    if (luminance > 0.8 && uv.y < VerticalPosition + VerticalSpread)
    {
        float starBoost = smoothstep(0.8, 1.0, luminance) * 0.15 * totalMask;
        color += starBoost;
    }

    // Subtle vignette to frame the aurora
    float2 center = float2(0.5, 0.5);
    float vignetteDist = length(uv - center);
    float vignette = 1.0 - smoothstep(0.5, 1.0, vignetteDist) * 0.15;
    color *= vignette;

    color = saturate(color);
    return float4(color, sceneColor.a) * input.Color;
}

technique AuroraBorealis
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
