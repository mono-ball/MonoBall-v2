// Frozen.fx
// Ice/frost effect applied TO the sprite
// Shader ID: PerEntityFrozen

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
float3 IceColor = float3(0.6, 0.85, 1.0);
float3 FrostColor = float3(0.95, 0.98, 1.0);
float FrostAmount = 0.6;
float ShimmerSpeed = 3.0;

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

    // Only affect opaque pixels
    if (pixelColor.a < 0.1)
    {
        return float4(0, 0, 0, 0);
    }

    // Frost noise pattern
    float frost = noise(uv * 15.0);
    frost += noise(uv * 30.0) * 0.5;
    frost += noise(uv * 60.0) * 0.25;
    frost = frost / 1.75;

    // Desaturate the original color
    float luminance = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    float3 desaturated = float3(luminance, luminance, luminance);

    // Blend toward ice blue
    float3 frozen = lerp(desaturated, IceColor, 0.5);

    // Add frost highlights based on noise
    float frostHighlight = smoothstep(0.4, 0.7, frost);
    frozen = lerp(frozen, FrostColor, frostHighlight * FrostAmount);

    // Shimmer effect
    float shimmer = sin(Time * ShimmerSpeed + uv.x * 30.0) * sin(Time * ShimmerSpeed * 1.3 + uv.y * 25.0);
    shimmer = shimmer * 0.5 + 0.5;
    shimmer = shimmer * shimmer * shimmer; // sharpen
    frozen += FrostColor * shimmer * 0.2;

    // Random sparkles
    float sparkle = hash(uv * 200.0 + floor(Time * 8.0) * 0.1);
    if (sparkle > 0.97)
    {
        frozen += float3(0.4, 0.4, 0.4);
    }

    // Blend with original based on frost amount
    pixelColor.rgb = lerp(pixelColor.rgb, frozen, FrostAmount);
    pixelColor *= input.Color;

    return pixelColor;
}

technique Frozen
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
