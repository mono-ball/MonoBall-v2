// Outline.fx
// Adds outline effect to sprites
// Shader ID: SpriteLayerOutline

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

float4 OutlineColor = float4(1.0, 1.0, 1.0, 1.0);
float OutlineWidth = 1.0;

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    pixelColor *= input.Color;
    
    // Sample neighboring pixels for outline detection
    float2 texelSize = 1.0 / 256.0; // Adjust based on texture size
    float alpha = pixelColor.a;
    
    // Check if pixel is on edge
    float edge = 0.0;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            if (x == 0 && y == 0) continue;
            float neighborAlpha = tex2D(SpriteTextureSampler, 
                input.TextureCoordinates + float2(x, y) * texelSize * OutlineWidth).a;
            if (neighborAlpha < alpha)
                edge = max(edge, neighborAlpha);
        }
    }
    
    // Blend outline color
    pixelColor.rgb = lerp(pixelColor.rgb, OutlineColor.rgb, edge);
    pixelColor.a = max(pixelColor.a, edge * OutlineColor.a);
    
    return pixelColor;
}

technique Outline
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}


