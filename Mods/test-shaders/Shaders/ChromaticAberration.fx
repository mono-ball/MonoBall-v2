// ChromaticAberration.fx
// Applies chromatic aberration effect (color separation at edges)
// Shader ID: CombinedLayerChromaticAberration

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

float AberrationAmount = 0.005;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 center = float2(0.5, 0.5);
    float2 offset = (input.TextureCoordinates - center) * AberrationAmount;
    
    // Sample RGB channels with different offsets for color separation
    float r = tex2D(SpriteTextureSampler, input.TextureCoordinates + offset).r;
    float g = tex2D(SpriteTextureSampler, input.TextureCoordinates).g;
    float b = tex2D(SpriteTextureSampler, input.TextureCoordinates - offset).b;
    float a = tex2D(SpriteTextureSampler, input.TextureCoordinates).a;
    
    float4 pixelColor = float4(r, g, b, a);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique ChromaticAberration
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

