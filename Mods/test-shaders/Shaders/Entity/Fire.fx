// Fire.fx
// Burning/on-fire effect applied TO the sprite
// Shader ID: PerEntityFire

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
float3 FlameColor1 = float3(1.0, 0.9, 0.2);   // Yellow
float3 FlameColor2 = float3(1.0, 0.4, 0.0);   // Orange
float3 FlameColor3 = float3(0.8, 0.1, 0.0);   // Red
float FlameSpeed = 4.0;
float FlameIntensity = 0.7;
float2 SpriteSize = float2(32.0, 32.0);

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

    // Animated fire noise
    float2 fireUV = uv * 8.0;
    fireUV.y -= Time * FlameSpeed; // Rising fire

    float fire = noise(fireUV);
    fire += noise(fireUV * 2.0 + Time * 2.0) * 0.5;
    fire += noise(fireUV * 4.0 - Time * 1.5) * 0.25;
    fire = fire / 1.75;

    // Fire is stronger at edges and top
    float heightFactor = 1.0 - uv.y; // Stronger at top
    float fireStrength = (edgeAmount + heightFactor * 0.3) * fire * FlameIntensity;

    // Fire color gradient based on intensity
    float3 fireColor;
    if (fireStrength > 0.5)
        fireColor = lerp(FlameColor2, FlameColor1, (fireStrength - 0.5) * 2.0);
    else
        fireColor = lerp(FlameColor3, FlameColor2, fireStrength * 2.0);

    // Flickering
    float flicker = noise(float2(Time * 15.0, uv.x * 5.0));
    fireStrength *= 0.7 + flicker * 0.6;

    // Blend fire with original sprite
    float3 finalColor = lerp(pixelColor.rgb, fireColor, saturate(fireStrength));

    // Add glow/brightness
    finalColor += fireColor * fireStrength * 0.3;

    pixelColor.rgb = finalColor;
    pixelColor *= input.Color;

    return pixelColor;
}

technique Fire
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
