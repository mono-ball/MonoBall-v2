// Kaleidoscope.fx
// Applies kaleidoscope mirror/repeat effect
// Shader ID: CombinedLayerKaleidoscope

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

float SegmentCount = 6.0;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);
    
    // Convert to polar coordinates
    float2 coord = uv - center;
    float angle = atan2(coord.y, coord.x);
    float radius = length(coord);
    
    // Mirror the angle to create segments
    angle = abs(fmod(angle, 2.0 * 3.14159 / SegmentCount));
    
    // Convert back to cartesian
    float2 mirroredCoord = float2(cos(angle), sin(angle)) * radius;
    float2 mirroredUV = mirroredCoord + center;
    
    // Also mirror horizontally for more complex pattern
    if (mirroredUV.x > 0.5)
    {
        mirroredUV.x = 1.0 - mirroredUV.x;
    }
    
    // Clamp to prevent sampling outside texture
    mirroredUV = clamp(mirroredUV, float2(0.0, 0.0), float2(1.0, 1.0));
    
    float4 pixelColor = tex2D(SpriteTextureSampler, mirroredUV);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Kaleidoscope
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

