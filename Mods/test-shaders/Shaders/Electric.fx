// Electric.fx
// Electric/lightning effect applied TO the sprite
// Shader ID: PerEntityElectric

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
float3 ElectricColor = float3(0.4, 0.7, 1.0);
float3 CoreColor = float3(1.0, 1.0, 1.0);
float Intensity = 0.8;
float FlashRate = 10.0;
float2 SpriteSize = float2(32.0, 32.0);

float hash(float n)
{
    return frac(sin(n) * 43758.5453);
}

float hash2(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash2(i);
    float b = hash2(i + float2(1.0, 0.0));
    float c = hash2(i + float2(0.0, 1.0));
    float d = hash2(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 pixelColor = tex2D(SpriteTextureSampler, uv);
    float2 texelSize = 1.0 / SpriteSize;

    // Only affect opaque pixels
    if (pixelColor.a < 0.1)
    {
        return float4(0, 0, 0, 0);
    }

    // Check if near edge
    float edgeAmount = 0.0;
    float2 offsets[8] = {
        float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
        float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1)
    };

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 samplePos = uv + offsets[i] * texelSize * 2.0;
        float neighbor = tex2D(SpriteTextureSampler, samplePos).a;
        if (neighbor < 0.5)
        {
            edgeAmount += 0.125;
        }
    }

    // Electric arc noise pattern
    float arc = noise(uv * 20.0 + Time * 8.0);
    arc += noise(uv * 40.0 - Time * 12.0) * 0.5;
    arc = arc / 1.5;

    // Random flashing/pulsing
    float flash = hash(floor(Time * FlashRate));
    float flashIntensity = flash > 0.7 ? (flash - 0.7) * 3.33 : 0.0;

    // Combine edge detection with arc pattern
    float electricStrength = (edgeAmount * 2.0 + arc * 0.3) * Intensity;
    electricStrength += flashIntensity * 0.5;

    // Electric color with white core for bright areas
    float3 electricFinal = lerp(ElectricColor, CoreColor, saturate(electricStrength - 0.5) * 2.0);

    // Crawling electric veins inside sprite
    float veins = noise(uv * 15.0 + float2(Time * 5.0, Time * 3.0));
    veins = smoothstep(0.6, 0.8, veins);

    // Combine effects
    float3 finalColor = pixelColor.rgb;

    // Add electric glow at edges
    finalColor = lerp(finalColor, electricFinal, saturate(electricStrength) * 0.7);

    // Add internal electric veins
    finalColor = lerp(finalColor, ElectricColor * 1.5, veins * 0.4 * Intensity);

    // Flash effect
    if (flashIntensity > 0.3)
    {
        finalColor = lerp(finalColor, CoreColor, flashIntensity * 0.5);
    }

    // Slight overall tint
    finalColor = lerp(finalColor, ElectricColor, 0.15 * Intensity);

    pixelColor.rgb = finalColor;
    pixelColor *= input.Color;

    return pixelColor;
}

technique Electric
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
