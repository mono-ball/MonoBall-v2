// WaveDistortion.fx - Enhanced with multi-layer turbulence
// Organic fluid distortion with multiple wave layers
// Shader ID: CombinedLayerWaveDistortion

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
float WaveAmplitude = 0.025;
float WaveFrequency = 8.0;
float TurbulenceStrength = 0.5;
float TurbulenceScale = 4.0;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

// Simple noise function
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

    // Layer 1: Primary horizontal wave
    float wave1 = sin(uv.y * WaveFrequency * PI + Time * 2.0) * WaveAmplitude;

    // Layer 2: Secondary vertical wave (different frequency)
    float wave2 = sin(uv.x * WaveFrequency * 0.7 * PI + Time * 1.5) * WaveAmplitude * 0.7;

    // Layer 3: Diagonal wave for complexity
    float diag = (uv.x + uv.y) * WaveFrequency * 0.5;
    float wave3 = sin(diag * PI + Time * 2.5) * WaveAmplitude * 0.5;

    // Layer 4: Turbulence noise layer
    float2 noiseCoord = uv * TurbulenceScale + Time * 0.3;
    float turbulence = (noise(noiseCoord) - 0.5) * 2.0 * TurbulenceStrength * WaveAmplitude;

    // Secondary turbulence at different scale
    float2 noiseCoord2 = uv * TurbulenceScale * 2.0 - Time * 0.2;
    float turbulence2 = (noise(noiseCoord2) - 0.5) * TurbulenceStrength * WaveAmplitude * 0.5;

    // Combine all distortions
    float2 distortion;
    distortion.x = wave1 + wave3 * 0.5 + turbulence;
    distortion.y = wave2 + wave3 * 0.5 + turbulence2;

    // Apply distortion with edge falloff to prevent artifacts
    float2 center = float2(0.5, 0.5);
    float edgeFalloff = 1.0 - smoothstep(0.3, 0.5, length(uv - center));
    distortion *= (0.5 + edgeFalloff * 0.5);

    float2 distortedUV = uv + distortion;

    // Clamp to prevent sampling outside
    distortedUV = clamp(distortedUV, 0.001, 0.999);

    float4 pixelColor = tex2D(SpriteTextureSampler, distortedUV);
    pixelColor *= input.Color;

    return pixelColor;
}

technique WaveDistortion
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
