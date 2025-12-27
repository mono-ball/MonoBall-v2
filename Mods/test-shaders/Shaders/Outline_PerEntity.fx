// Outline.fx
// Applies colored outline effect to individual entities
// Shader ID: PerEntityOutline

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

float4 OutlineColor = float4(1.0, 0.0, 1.0, 1.0); // Magenta outline
float OutlineThickness = 2.0;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // If current pixel is opaque, draw it normally
    if (pixelColor.a > 0.5)
    {
        pixelColor *= input.Color;
        return pixelColor;
    }
    
    // Current pixel is transparent - check if we should draw outline
    // Calculate texel size for outline sampling
    float2 texelSize = 1.0 / ScreenSize;
    
    // Sample surrounding pixels to detect if we're near an opaque pixel
    float outlineAlpha = 0.0;
    
    // Sample 8 surrounding pixels (cardinal and diagonal directions)
    float2 offsets[8] = {
        float2(-1.0, 0.0),  // Left
        float2(1.0, 0.0),   // Right
        float2(0.0, -1.0),  // Up
        float2(0.0, 1.0),   // Down
        float2(-1.0, -1.0), // Top-left
        float2(1.0, -1.0),  // Top-right
        float2(-1.0, 1.0),  // Bottom-left
        float2(1.0, 1.0)    // Bottom-right
    };
    
    // Check if any surrounding pixel is opaque (edge detection)
    for (int i = 0; i < 8; i++)
    {
        float2 sampleUV = input.TextureCoordinates + offsets[i] * texelSize * OutlineThickness;
        float4 sampleColor = tex2D(SpriteTextureSampler, sampleUV);
        if (sampleColor.a > 0.5)
        {
            outlineAlpha = 1.0;
            break;
        }
    }
    
    // If we're near an opaque pixel, draw outline
    if (outlineAlpha > 0.0)
    {
        pixelColor = OutlineColor;
        pixelColor.a = outlineAlpha;
        return pixelColor;
    }
    
    // No outline needed, return transparent
    return pixelColor;
}

technique Outline
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}


