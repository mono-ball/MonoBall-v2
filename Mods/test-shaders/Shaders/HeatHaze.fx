// HeatHaze.fx - Rising Heat Distortion Effect
// Realistic heat shimmer like hot asphalt or desert mirage
// Shader ID: CombinedLayerHeatHaze

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
float HazeStrength = 0.015;
float RiseSpeed = 2.0;
float WaveFrequency = 20.0;
float DistortionScale = 3.0;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

// Noise function for organic variation
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

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Heat rises from bottom - stronger effect at bottom of screen
    float heightFactor = 1.0 - uv.y;
    heightFactor = pow(heightFactor, 1.5); // Exponential falloff

    // Rising time offset - heat columns rise upward
    float risingOffset = Time * RiseSpeed;

    // Multiple wave layers for complex heat shimmer
    float2 distortion = float2(0.0, 0.0);

    // Primary wave - large slow undulations
    float wave1 = sin((uv.x * WaveFrequency + uv.y * 5.0 - risingOffset) * PI);
    wave1 *= sin((uv.y * 3.0 - risingOffset * 0.7) * PI) * 0.5 + 0.5;

    // Secondary wave - faster, smaller ripples
    float wave2 = sin((uv.x * WaveFrequency * 2.0 - risingOffset * 1.3) * PI);
    wave2 *= cos((uv.y * 7.0 - risingOffset * 0.9) * PI) * 0.5 + 0.5;

    // Tertiary wave - very fine shimmer
    float wave3 = sin((uv.x * WaveFrequency * 4.0 + risingOffset * 2.0) * PI);

    // Noise for organic randomness
    float2 noiseCoord = uv * DistortionScale;
    noiseCoord.y -= risingOffset * 0.3; // Noise also rises
    float noiseVal = noise(noiseCoord) - 0.5;
    float noiseVal2 = noise(noiseCoord * 2.0 + 100.0) - 0.5;

    // Combine all distortions
    distortion.x = wave1 * 0.5 + wave2 * 0.3 + wave3 * 0.1;
    distortion.x += noiseVal * 0.4;
    distortion.y = wave2 * 0.2 + noiseVal2 * 0.2;

    // Apply height-based intensity
    distortion *= heightFactor * HazeStrength;

    // Add some horizontal variation (heat columns)
    float columnPhase = sin(uv.x * 10.0) * 0.5 + 0.5;
    distortion *= 0.7 + columnPhase * 0.3;

    // Apply distortion
    float2 distortedUV = uv + distortion;
    distortedUV = clamp(distortedUV, 0.001, 0.999);

    // Sample with slight color separation for heat shimmer look
    float chromaOffset = heightFactor * HazeStrength * 0.3;
    float r = tex2D(SpriteTextureSampler, distortedUV + float2(chromaOffset, 0.0)).r;
    float g = tex2D(SpriteTextureSampler, distortedUV).g;
    float b = tex2D(SpriteTextureSampler, distortedUV - float2(chromaOffset, 0.0)).b;
    float a = tex2D(SpriteTextureSampler, distortedUV).a;

    float3 color = float3(r, g, b);

    // Warm color tint in heat areas
    float3 heatTint = float3(1.05, 1.0, 0.95);
    color *= lerp(float3(1, 1, 1), heatTint, heightFactor * 0.3);

    // Slight brightness fluctuation (shimmering)
    float shimmer = 1.0 + sin(Time * 8.0 + uv.x * 30.0) * 0.02 * heightFactor;
    color *= shimmer;

    // Subtle contrast reduction in heavy haze areas (atmospheric scattering)
    float haze = heightFactor * 0.1;
    float3 hazeColor = float3(0.9, 0.85, 0.8);
    color = lerp(color, hazeColor, haze);

    color = saturate(color);
    return float4(color, a) * input.Color;
}

technique HeatHaze
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
