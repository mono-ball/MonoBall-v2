// PrismGrade.fx
// Layer 1: Vaporwave chromatic color grading with noise-driven iridescence and radial prism rays
// DESIGNED FOR STACKING - uses noise and radial effects instead of sin/cos waves

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

// Color grading - vaporwave palette
float3 ShadowColor = float3(0.15, 0.0, 0.25);     // Deep purple shadows
float3 MidColor = float3(1.0, 0.4, 0.8);          // Hot pink midtones
float3 HighlightColor = float3(0.4, 1.0, 1.0);    // Cyan highlights

// Effect intensity
float GradeIntensity = 0.65;
float PrismStrength = 0.008;
float ChromaShift = 0.003;
float Saturation = 1.4;
float GlowAmount = 0.25;

#define PI 3.14159265359

// High quality 2D noise
float hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise with smooth interpolation
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f); // Smoothstep

    float a = hash21(i);
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Fractal brownian motion - layered noise
float fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        value += amplitude * noise(p * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value;
}

// HSV to RGB conversion
float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);

    // Distance and angle from center for radial effects
    float2 toCenter = uv - center;
    float dist = length(toCenter);
    float angle = atan2(toCenter.y, toCenter.x);

    // Noise-driven chromatic aberration instead of sin wave
    float2 noiseCoord = uv * 3.0 + Time * 0.1;
    float noiseVal = fbm(noiseCoord);
    float prismAmount = (noiseVal - 0.5) * PrismStrength * 2.0;

    // Radial prism rays - light beams from center
    float rayCount = 8.0;
    float rayAngle = frac(angle / (2.0 * PI) * rayCount + Time * 0.05);
    float ray = smoothstep(0.4, 0.5, rayAngle) * smoothstep(0.6, 0.5, rayAngle);
    ray *= smoothstep(0.0, 0.3, dist); // Fade near center

    // RGB separation based on noise and radial position
    float2 rOffset = toCenter * (prismAmount + ray * PrismStrength);
    float2 bOffset = -toCenter * (prismAmount + ray * PrismStrength);

    // Noise-based chromatic shift direction (not just angular)
    float2 chromaDir = float2(
        noise(uv * 5.0 + Time * 0.2) - 0.5,
        noise(uv * 5.0 + Time * 0.2 + 100.0) - 0.5
    ) * ChromaShift;

    float r = tex2D(SpriteTextureSampler, uv + rOffset + chromaDir).r;
    float g = tex2D(SpriteTextureSampler, uv).g;
    float b = tex2D(SpriteTextureSampler, uv + bOffset - chromaDir).b;
    float a = tex2D(SpriteTextureSampler, uv).a;

    float3 color = float3(r, g, b);

    // Calculate luminance for grading
    float lum = dot(color, float3(0.299, 0.587, 0.114));

    // Three-way color grade with smooth transitions
    float3 graded;
    float lowThresh = 0.33;
    float highThresh = 0.66;

    if (lum < lowThresh)
    {
        float t = lum / lowThresh;
        t = t * t;
        graded = lerp(ShadowColor, MidColor, t);
    }
    else if (lum < highThresh)
    {
        float t = (lum - lowThresh) / (highThresh - lowThresh);
        t = t * (2.0 - t);
        graded = lerp(MidColor, HighlightColor, t);
    }
    else
    {
        float t = (lum - highThresh) / (1.0 - highThresh);
        t = 1.0 - (1.0 - t) * (1.0 - t);
        graded = lerp(HighlightColor, float3(1.0, 1.0, 1.0), t);
    }

    // Blend graded with original
    color = lerp(color, graded * (color + 0.15), GradeIntensity);

    // Noise-driven iridescence instead of shimmer waves
    float iridescentNoise = fbm(uv * 8.0 + Time * 0.15);
    float hue = iridescentNoise + dist * 0.5 + Time * 0.02;
    float3 iridescent = hsv2rgb(float3(hue, 0.6, 1.0));
    color += iridescent * ray * 0.12;

    // Radial rainbow rays from center
    float rainbowHue = angle / (2.0 * PI) + Time * 0.03;
    float3 rainbow = hsv2rgb(float3(rainbowHue, 0.7, 1.0));
    float rayGlow = ray * smoothstep(0.5, 0.2, dist);
    color += rainbow * rayGlow * 0.08;

    // Boost saturation
    float gray = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(gray, gray, gray), color, Saturation);

    // Noise-based glow on bright areas
    float glowNoise = noise(uv * 4.0 - Time * 0.1);
    float glowMask = smoothstep(0.5, 0.9, lum) * (0.7 + glowNoise * 0.3);
    float3 glowColor = lerp(MidColor, HighlightColor, glowNoise);
    color += glowColor * glowMask * GlowAmount;

    // Subtle vignette with color
    float vignette = 1.0 - dist * 0.4;
    float3 vignetteColor = float3(1.0, 0.7, 0.9);
    color = lerp(color * vignetteColor * 0.8, color, vignette);

    return float4(saturate(color), a) * input.Color;
}

technique PrismGrade
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
