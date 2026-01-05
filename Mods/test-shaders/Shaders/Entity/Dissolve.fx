// Dissolve.fx
// Noise-based dissolve effect with glowing embers
// Shader ID: PerEntityDissolve

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
    MinFilter = Point;
    MagFilter = Point;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Time = 0.0;
float EdgeWidth = 0.15;
float3 EdgeColor = float3(1.0, 0.5, 0.0);
float3 EdgeColor2 = float3(1.0, 0.2, 0.0);
float NoiseScale = 6.0;
float CycleSpeed = 0.3;

// Simple hash
float hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// Value noise
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 pixelColor = tex2D(SpriteTextureSampler, uv);

    // Skip transparent pixels
    if (pixelColor.a < 0.1)
    {
        return float4(0, 0, 0, 0);
    }

    // Generate dissolve noise - simple 2-octave noise
    float2 noiseCoord = uv * NoiseScale;
    float dissolveNoise = noise(noiseCoord) * 0.6;
    dissolveNoise += noise(noiseCoord * 2.0 + 0.5) * 0.4;

    // Auto-animate threshold with ping-pong (0 -> 1 -> 0)
    float cycle = frac(Time * CycleSpeed);
    float threshold;
    if (cycle < 0.5)
    {
        threshold = cycle * 2.0;  // 0 to 1
    }
    else
    {
        threshold = (1.0 - cycle) * 2.0;  // 1 to 0
    }

    // Scale threshold to not fully dissolve
    threshold = threshold * 0.9;

    // Dissolve logic: pixel disappears when noise < threshold
    if (dissolveNoise < threshold)
    {
        return float4(0, 0, 0, 0);
    }

    // Edge glow effect
    float edgeDist = dissolveNoise - threshold;

    if (edgeDist < EdgeWidth)
    {
        float edgeIntensity = 1.0 - (edgeDist / EdgeWidth);
        edgeIntensity = edgeIntensity * edgeIntensity; // Quadratic falloff

        // Animate ember colors
        float colorMix = sin(Time * 5.0 + uv.x * 10.0) * 0.5 + 0.5;
        float3 emberColor = lerp(EdgeColor, EdgeColor2, colorMix);

        // Flickering
        float flicker = hash(uv * 50.0 + frac(Time * 8.0)) * 0.3 + 0.7;
        emberColor *= flicker;

        // Blend ember with original
        pixelColor.rgb = lerp(pixelColor.rgb, emberColor, edgeIntensity * 0.8);
        pixelColor.rgb += emberColor * edgeIntensity * 0.3;
    }

    pixelColor *= input.Color;
    return pixelColor;
}

technique Dissolve
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
