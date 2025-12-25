// ColorGrading.fx
// Applies color grading to tile layers
// Shader ID: TileLayerColorGrading

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

float Brightness = 0.0;
float Contrast = 1.0;
float Saturation = 1.0;
float3 ColorTint = float3(1.0, 1.0, 1.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    pixelColor *= input.Color;
    
    // Apply brightness
    pixelColor.rgb += Brightness;
    
    // Apply contrast
    pixelColor.rgb = (pixelColor.rgb - 0.5) * Contrast + 0.5;
    
    // Apply saturation
    float gray = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    pixelColor.rgb = lerp(gray, pixelColor.rgb, Saturation);
    
    // Apply color tint
    pixelColor.rgb *= ColorTint;
    
    return pixelColor;
}

technique ColorGrading
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

