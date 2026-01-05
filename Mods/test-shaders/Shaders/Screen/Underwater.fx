// Underwater.fx - Submerged Ocean Effect
// Realistic underwater visuals with caustics, color absorption, and wave distortion
// Shader ID: CombinedLayerUnderwater

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
float WaveStrength = 0.02;
float WaveFrequency = 10.0;
float CausticIntensity = 0.3;
float CausticScale = 8.0;
float3 TintColor = float3(0.3, 0.5, 0.8);
float TintStrength = 0.3;
float FogDensity = 0.2;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

// Noise for caustics
float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

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

// Caustic pattern generation
float caustic(float2 uv)
{
    float2 p = uv * CausticScale;

    // Multiple layers of sine waves at different angles
    float c = 0.0;
    float t = Time * 0.5;

    c += sin(p.x * 1.0 + t) * sin(p.y * 1.2 - t * 0.7);
    c += sin(p.x * 0.8 - t * 0.5 + p.y * 0.9 + t) * 0.7;
    c += sin((p.x + p.y) * 0.7 + t * 0.3) * sin((p.x - p.y) * 0.8 - t * 0.4) * 0.5;

    // Add noise for organic variation
    c += noise(p + t * 0.2) * 0.3;

    // Normalize and create bright spots
    c = c * 0.25 + 0.5;
    c = pow(max(c, 0.0), 2.0);

    return c;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);

    // Wave distortion - simulates looking through water surface
    float2 waveOffset;
    waveOffset.x = sin(uv.y * WaveFrequency + Time * 2.0) * WaveStrength;
    waveOffset.x += sin(uv.y * WaveFrequency * 2.3 + Time * 1.5) * WaveStrength * 0.5;
    waveOffset.y = cos(uv.x * WaveFrequency * 0.8 + Time * 1.8) * WaveStrength * 0.7;
    waveOffset.y += cos(uv.x * WaveFrequency * 1.7 + Time * 2.2) * WaveStrength * 0.3;

    float2 distortedUV = uv + waveOffset;
    distortedUV = clamp(distortedUV, 0.001, 0.999);

    // Sample the texture
    float4 pixelColor = tex2D(SpriteTextureSampler, distortedUV);
    float3 color = pixelColor.rgb;

    // Caustic light patterns
    float causticLight = caustic(uv);
    causticLight *= CausticIntensity;

    // Apply caustics as additive light
    color += causticLight * TintColor * 0.5;

    // Underwater color absorption (red fades first, then green)
    float depth = 1.0; // Could be based on distance or position
    color.r *= (1.0 - FogDensity * 0.5);
    color.g *= (1.0 - FogDensity * 0.2);

    // Apply water tint
    float3 waterColor = TintColor;
    color = lerp(color, color * waterColor, TintStrength);

    // Distance fog (darker towards edges simulating depth)
    float distFromCenter = length(uv - center);
    float fog = smoothstep(0.2, 0.7, distFromCenter) * FogDensity;
    color = lerp(color, waterColor * 0.3, fog);

    // Light rays from above (brighter at top)
    float lightRay = smoothstep(0.8, 0.0, uv.y);
    lightRay *= (1.0 + sin(uv.x * 20.0 + Time) * 0.1);
    color += lightRay * TintColor * 0.15;

    // Subtle bubbles (moving upward)
    float2 bubbleUV = uv;
    bubbleUV.y = frac(bubbleUV.y - Time * 0.1);
    float bubbles = step(0.98, hash(floor(bubbleUV * 30.0)));
    color += bubbles * 0.2;

    // Vignette for depth
    float vignette = 1.0 - smoothstep(0.3, 0.8, distFromCenter);
    color *= (0.7 + vignette * 0.3);

    color = saturate(color);
    return float4(color, pixelColor.a) * input.Color;
}

technique Underwater
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
