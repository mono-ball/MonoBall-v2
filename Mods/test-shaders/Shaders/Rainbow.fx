// Rainbow.fx
// Applies rainbow color cycling effect to individual entities
// Shader ID: PerEntityRainbow

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

float Time = 0.0;
float Intensity = 0.5; // How much rainbow effect (0.0 = original, 1.0 = full rainbow)
float Speed = 1.0; // Speed of color cycling

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Early exit for transparent pixels
    if (pixelColor.a < 0.01)
    {
        return pixelColor;
    }
    
    // Calculate rainbow hue based on position and time
    float hue = (input.TextureCoordinates.x + input.TextureCoordinates.y) * 2.0 + Time * Speed;
    hue = fmod(hue, 1.0); // Wrap around
    
    // Convert hue to RGB (HSV to RGB conversion)
    float3 rainbowColor;
    float h = hue * 6.0;
    float c = 1.0;
    float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
    
    if (h < 1.0)
        rainbowColor = float3(c, x, 0.0);
    else if (h < 2.0)
        rainbowColor = float3(x, c, 0.0);
    else if (h < 3.0)
        rainbowColor = float3(0.0, c, x);
    else if (h < 4.0)
        rainbowColor = float3(0.0, x, c);
    else if (h < 5.0)
        rainbowColor = float3(x, 0.0, c);
    else
        rainbowColor = float3(c, 0.0, x);
    
    // Blend original color with rainbow color
    pixelColor.rgb = lerp(pixelColor.rgb, rainbowColor, Intensity);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Rainbow
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

