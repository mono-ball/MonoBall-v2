// ScanPulse.fx
// Layer 2: Diagonal scan beams with circular ripple pulses from random points
// DESIGNED FOR STACKING - uses diagonal sweeps and radial ripples, not vertical waves

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

// Scanline parameters
float ScanlineCount = 180.0;
float ScanlineIntensity = 0.12;
float ScanlineSpeed = 0.0;

// Diagonal sweep parameters
float PulseSpeed = 1.2;
float PulseWidth = 0.08;
float PulseGlow = 0.5;
float3 PulseColor = float3(1.0, 0.3, 0.8);

// Ripple parameters
float WaveSpeed = 0.8;
float WaveIntensity = 0.15;
float3 WaveColor = float3(0.3, 1.0, 1.0);

// Distortion
float DistortionAmount = 0.004;

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

// Noise for organic distortion
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

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float aspectRatio = ScreenSize.x / ScreenSize.y;

    // Noise-based distortion instead of sin waves
    float2 noiseCoord = uv * 8.0 + Time * 0.3;
    float distortNoise = noise(noiseCoord) - 0.5;
    float distortNoise2 = noise(noiseCoord + 50.0) - 0.5;
    float2 distortion = float2(distortNoise, distortNoise2) * DistortionAmount;

    float2 sampleUV = uv + distortion;
    float4 tex = tex2D(SpriteTextureSampler, sampleUV);
    float3 color = tex.rgb;

    // Diagonal scanlines (45 degree angle)
    float diagonalPos = (uv.x + uv.y) * ScanlineCount * 0.5;
    diagonalPos += Time * ScanlineSpeed;
    float scanline = frac(diagonalPos);
    scanline = smoothstep(0.0, 0.3, scanline) * smoothstep(1.0, 0.7, scanline);
    color *= 1.0 - (1.0 - scanline) * ScanlineIntensity;

    // Diagonal sweep beam (corner to corner)
    float sweepTime = frac(Time * PulseSpeed * 0.3);
    float sweepPos = (uv.x + uv.y) * 0.5; // Diagonal position 0-1
    float sweepDist = abs(sweepPos - sweepTime);
    if (sweepDist > 0.5) sweepDist = 1.0 - sweepDist; // Wrap around
    float sweep = smoothstep(PulseWidth, 0.0, sweepDist);
    sweep = sweep * sweep; // Sharper falloff
    color += PulseColor * sweep * PulseGlow * 0.6;

    // Secondary diagonal sweep (opposite direction)
    float sweepTime2 = frac(Time * PulseSpeed * 0.25 + 0.5);
    float sweepPos2 = (uv.x - uv.y + 1.0) * 0.5; // Other diagonal
    float sweepDist2 = abs(sweepPos2 - sweepTime2);
    if (sweepDist2 > 0.5) sweepDist2 = 1.0 - sweepDist2;
    float sweep2 = smoothstep(PulseWidth * 0.7, 0.0, sweepDist2);
    color += WaveColor * sweep2 * sweep2 * 0.3;

    // Circular ripples from pseudo-random points
    float3 rippleAccum = float3(0, 0, 0);

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        // Generate ripple center based on time
        float ripplePhase = floor(Time * WaveSpeed * 0.5 + float(i) * 0.33);
        float2 rippleCenter = float2(
            hash11(ripplePhase * 1.7 + float(i) * 13.0),
            hash11(ripplePhase * 2.3 + float(i) * 17.0)
        );

        // Distance from ripple center
        float2 toRipple = uv - rippleCenter;
        toRipple.x *= aspectRatio;
        float rippleDist = length(toRipple);

        // Expanding ring
        float rippleTime = frac(Time * WaveSpeed * 0.5 + float(i) * 0.33);
        float ringRadius = rippleTime * 0.8;
        float ringDist = abs(rippleDist - ringRadius);
        float ring = smoothstep(0.05, 0.0, ringDist);
        ring *= 1.0 - rippleTime; // Fade as it expands
        ring *= smoothstep(0.0, 0.1, rippleTime); // Fade in at start

        // Color varies per ripple
        float3 rippleColor = lerp(WaveColor, PulseColor, hash11(ripplePhase + float(i)));
        rippleAccum += rippleColor * ring;
    }

    color += rippleAccum * WaveIntensity;

    // Corner glow pulses (radial from corners)
    float2 corners[4];
    corners[0] = float2(0.0, 0.0);
    corners[1] = float2(1.0, 0.0);
    corners[2] = float2(0.0, 1.0);
    corners[3] = float2(1.0, 1.0);

    [unroll]
    for (int c = 0; c < 4; c++)
    {
        float2 toCorner = uv - corners[c];
        toCorner.x *= aspectRatio;
        float cornerDist = length(toCorner);

        float cornerPulse = frac(Time * PulseSpeed * 0.2 + float(c) * 0.25);
        float cornerRing = abs(cornerDist - cornerPulse * 1.5);
        float cornerGlow = smoothstep(0.1, 0.0, cornerRing) * (1.0 - cornerPulse);

        float3 cornerColor = (c < 2) ? PulseColor : WaveColor;
        color += cornerColor * cornerGlow * 0.15;
    }

    // Subtle noise-based shimmer
    float shimmerNoise = noise(uv * 30.0 + Time * 2.0);
    shimmerNoise = smoothstep(0.6, 0.8, shimmerNoise);
    color += float3(1.0, 0.9, 1.0) * shimmerNoise * 0.03;

    return float4(saturate(color), tex.a) * input.Color;
}

technique ScanPulse
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
