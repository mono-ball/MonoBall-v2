// PulsingGlow.fx
// Applies animated pulsing glow effect to individual entities
// Shader ID: PerEntityPulsingGlow

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

float4 GlowColor = float4(1.0, 0.5, 0.0, 1.0); // Orange glow
float BaseIntensity = 0.3;
float PulseIntensity = 0.4; // Additional intensity that pulses
float PulseSpeed = 2.0; // Pulses per second
float Time = 0.0;

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Early exit for transparent pixels
    if (pixelColor.a < 0.01)
    {
        return pixelColor;
    }
    
    pixelColor *= input.Color;
    
    // Calculate pulsing intensity using sine wave
    float pulse = sin(Time * PulseSpeed * 6.28318) * 0.5 + 0.5; // 0.0 to 1.0
    float glowIntensity = BaseIntensity + (PulseIntensity * pulse);
    
    // Calculate glow based on alpha
    float glow = pixelColor.a * glowIntensity;
    
    // Blend glow color with original color
    pixelColor.rgb = lerp(pixelColor.rgb, GlowColor.rgb, glow);
    pixelColor.a = max(pixelColor.a, glow * GlowColor.a);
    
    return pixelColor;
}

technique PulsingGlow
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

