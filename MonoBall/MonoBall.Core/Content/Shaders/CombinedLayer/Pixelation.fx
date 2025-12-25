// Pixelation.fx
// Applies pixelation effect (retro/low-res look)
// Shader ID: CombinedLayerPixelation

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

float PixelSize = 8.0;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    // Calculate pixelated UV coordinates
    float2 texelSize = 1.0 / ScreenSize;
    float2 pixelatedUV = floor(input.TextureCoordinates / (PixelSize * texelSize)) * (PixelSize * texelSize);
    
    // Sample the pixelated coordinate
    float4 pixelColor = tex2D(SpriteTextureSampler, pixelatedUV);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Pixelation
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

