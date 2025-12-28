// Silhouette.fx
// Solid color fill with animated edge glow - applied TO the sprite
// Shader ID: PerEntitySilhouette

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
float3 FillColor = float3(0.05, 0.05, 0.1);
float3 EdgeColor = float3(1.0, 0.7, 0.2);
float3 EdgeColor2 = float3(1.0, 0.3, 0.1);
float PulseSpeed = 2.0;
float WaveSpeed = 3.0;
float2 SpriteSize = float2(32.0, 32.0);

float hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
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

    // Check if near edge by sampling neighbors
    float edgeAmount = 0.0;

    // Sample 8 directions
    float2 offsets[8] = {
        float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
        float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1)
    };

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 samplePos = uv + offsets[i] * texelSize * 1.5;
        float neighbor = tex2D(SpriteTextureSampler, samplePos).a;
        if (neighbor < 0.5)
        {
            edgeAmount += 0.125;
        }
    }

    // Base silhouette color
    float3 finalColor = FillColor;

    // Edge highlight
    if (edgeAmount > 0.0)
    {
        // Animated wave along edge
        float wave = sin(uv.x * 30.0 + uv.y * 30.0 + Time * WaveSpeed) * 0.5 + 0.5;

        // Pulsing intensity
        float pulse = sin(Time * PulseSpeed) * 0.3 + 0.7;

        // Color blend between edge colors
        float3 highlightColor = lerp(EdgeColor, EdgeColor2, wave);

        // Mix edge glow with fill
        finalColor = lerp(FillColor, highlightColor, edgeAmount * pulse);
    }

    // Subtle inner sparkle
    float sparkle = hash(uv * 150.0 + floor(Time * 6.0) * 0.1);
    if (sparkle > 0.98)
    {
        finalColor += EdgeColor * 0.3;
    }

    return float4(finalColor, pixelColor.a) * input.Color;
}

technique Silhouette
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
