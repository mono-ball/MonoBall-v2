// Glow.fx
// Applies glow effect to individual entities
// Shader ID: PerEntityGlow

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

float4 GlowColor = float4(1.0, 1.0, 1.0, 1.0);
float GlowIntensity = 0.5;

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Early exit for transparent pixels
    if (pixelColor.a < 0.01)
    {
        return pixelColor;
    }
    
    pixelColor *= input.Color;
    
    // Calculate glow based on alpha
    float glow = pixelColor.a * GlowIntensity;
    
    // Blend glow color with original color
    pixelColor.rgb = lerp(pixelColor.rgb, GlowColor.rgb, glow);
    pixelColor.a = max(pixelColor.a, glow * GlowColor.a);
    
    return pixelColor;
}

technique Glow
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
