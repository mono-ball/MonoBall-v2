// Grayscale.fx
// Converts entire screen to grayscale
// Shader ID: CombinedLayerGrayscale

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

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Convert to grayscale using standard luminance weights
    float gray = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    pixelColor.rgb = float3(gray, gray, gray);
    
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Grayscale
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}


