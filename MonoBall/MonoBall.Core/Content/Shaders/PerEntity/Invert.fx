// Invert.fx
// Applies color inversion effect to individual entities
// Shader ID: PerEntityInvert

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

float Intensity = 1.0; // How much inversion (0.0 = original, 1.0 = full invert)

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Early exit for transparent pixels
    if (pixelColor.a < 0.01)
    {
        return pixelColor;
    }
    
    // Invert RGB channels
    float3 inverted = float3(1.0, 1.0, 1.0) - pixelColor.rgb;
    
    // Blend original with inverted based on intensity
    pixelColor.rgb = lerp(pixelColor.rgb, inverted, Intensity);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Invert
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

