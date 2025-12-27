// WaveDistortion.fx
// Applies animated wave distortion effect
// Shader ID: CombinedLayerWaveDistortion

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

float WaveSpeed = 2.0;
float WaveFrequency = 10.0;
float WaveAmplitude = 0.02;
float Time = 0.0;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    
    // Create wave distortion
    float wave = sin(uv.y * WaveFrequency + Time * WaveSpeed) * WaveAmplitude;
    float2 distortedUV = uv + float2(wave, 0.0);
    
    // Add vertical wave as well for more interesting effect
    float waveY = sin(uv.x * WaveFrequency * 0.7 + Time * WaveSpeed * 1.3) * WaveAmplitude * 0.5;
    distortedUV += float2(0.0, waveY);
    
    // Clamp to prevent sampling outside texture
    distortedUV = clamp(distortedUV, float2(0.0, 0.0), float2(1.0, 1.0));
    
    float4 pixelColor = tex2D(SpriteTextureSampler, distortedUV);
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique WaveDistortion
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}


