// Dream.fx - Ethereal Dream Sequence Effect
// Soft, otherworldly visuals with glow, blur, color shifts, and sparkles
// Shader ID: CombinedLayerDream

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
float BlurAmount = 0.003;
float GlowIntensity = 0.4;
float VignetteStrength = 0.5;
float ColorShift = 0.1;
float SparkleIntensity = 0.15;
float PulseSpeed = 1.0;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);

    // Breathing/pulsing distortion
    float pulse = sin(Time * PulseSpeed) * 0.5 + 0.5;
    float breathe = 1.0 + sin(Time * PulseSpeed * 0.7) * 0.01;

    // Subtle swirl distortion
    float2 toCenter = uv - center;
    float dist = length(toCenter);
    float angle = atan2(toCenter.y, toCenter.x);
    angle += sin(dist * 10.0 - Time) * 0.02;
    float2 swirlUV = center + float2(cos(angle), sin(angle)) * dist * breathe;

    // Soft blur (sample multiple points)
    float3 color = float3(0, 0, 0);
    float blurPulse = BlurAmount * (1.0 + pulse * 0.5);

    // 9-tap blur for soft glow
    float2 offsets[9] = {
        float2(-1, -1), float2(0, -1), float2(1, -1),
        float2(-1,  0), float2(0,  0), float2(1,  0),
        float2(-1,  1), float2(0,  1), float2(1,  1)
    };

    float weights[9] = {
        0.0625, 0.125, 0.0625,
        0.125,  0.25,  0.125,
        0.0625, 0.125, 0.0625
    };

    for (int i = 0; i < 9; i++)
    {
        float2 sampleUV = swirlUV + offsets[i] * blurPulse;
        sampleUV = clamp(sampleUV, 0.001, 0.999);
        color += tex2D(SpriteTextureSampler, sampleUV).rgb * weights[i];
    }

    // Store original for alpha
    float alpha = tex2D(SpriteTextureSampler, clamp(swirlUV, 0.001, 0.999)).a;

    // Dreamy glow - brighten highlights
    float luminance = dot(color, float3(0.299, 0.587, 0.114));
    float glow = smoothstep(0.5, 1.0, luminance) * GlowIntensity;
    color += glow * float3(1.0, 0.95, 0.9);

    // Color shifting - cycle through subtle hues
    float hueShift = sin(Time * 0.5 + dist * 5.0) * ColorShift;
    float3 shiftedColor;
    shiftedColor.r = color.r * (1.0 + hueShift);
    shiftedColor.g = color.g;
    shiftedColor.b = color.b * (1.0 - hueShift);
    color = lerp(color, shiftedColor, 0.5);

    // Pastel tint
    float3 pastelTint = float3(0.95, 0.9, 1.0); // Slight lavender
    color *= pastelTint;

    // Sparkles - random bright points that twinkle
    float2 sparkleGrid = floor(uv * 50.0);
    float sparkleRandom = hash(sparkleGrid);
    float sparkleTime = frac(Time * 2.0 + sparkleRandom * 10.0);
    float sparkle = 0.0;

    if (sparkleRandom > 0.95)
    {
        // Twinkle animation
        sparkle = sin(sparkleTime * PI) * SparkleIntensity;
        sparkle *= step(0.3, sparkleTime) * step(sparkleTime, 0.7);

        // Make sparkle a small bright point
        float2 sparkleCenter = (sparkleGrid + 0.5) / 50.0;
        float sparkleDist = length(uv - sparkleCenter) * 50.0;
        sparkle *= smoothstep(1.0, 0.0, sparkleDist);
    }
    color += sparkle * float3(1.0, 1.0, 0.9);

    // Soft vignette with color
    float vignetteDist = length(uv - center);
    float vignette = smoothstep(0.3, 0.8, vignetteDist);
    float3 vignetteColor = float3(0.8, 0.7, 0.9); // Purple edges
    color = lerp(color, color * vignetteColor, vignette * VignetteStrength);

    // Fade edges to create floating feeling
    float edgeFade = smoothstep(0.5, 0.3, vignetteDist);
    float brightness = 0.9 + edgeFade * 0.1;
    color *= brightness;

    // Occasional light flare
    float flare = sin(Time * 0.3) * 0.5 + 0.5;
    flare = pow(flare, 4.0) * 0.1;
    color += flare * float3(1.0, 0.95, 0.85);

    color = saturate(color);
    return float4(color, alpha) * input.Color;
}

technique Dream
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
