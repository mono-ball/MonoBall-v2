// Bloom.fx
// Applies bloom effect (glowing highlights) to entire screen
// Shader ID: CombinedLayerBloom

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

float BloomIntensity = 1.0;
float BloomThreshold = 0.7;
float BloomBlurAmount = 0.005;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 pixelColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Calculate luminance to find bright areas
    float luminance = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    
    // Extract bright areas (above threshold)
    float bright = max(0.0, luminance - BloomThreshold);
    float3 bloomColor = pixelColor.rgb * bright;
    
    // Simple blur approximation using multiple samples
    float2 texelSize = 1.0 / ScreenSize;
    float3 blur = float3(0.0, 0.0, 0.0);
    float blurWeight = 0.0;
    
    // Sample surrounding pixels for blur
    for (int x = -2; x <= 2; x++)
    {
        for (int y = -2; y <= 2; y++)
        {
            float2 offset = float2(x, y) * texelSize * BloomBlurAmount * 10.0;
            float4 sample = tex2D(SpriteTextureSampler, input.TextureCoordinates + offset);
            float sampleLum = dot(sample.rgb, float3(0.299, 0.587, 0.114));
            float sampleBright = max(0.0, sampleLum - BloomThreshold);
            
            // Gaussian-like weight (closer pixels have more weight)
            float weight = 1.0 / (1.0 + (x * x + y * y));
            blur += sample.rgb * sampleBright * weight;
            blurWeight += weight;
        }
    }
    
    if (blurWeight > 0.0)
    {
        blur /= blurWeight;
    }
    
    // Combine original color with bloom
    pixelColor.rgb = pixelColor.rgb + blur * BloomIntensity;
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique Bloom
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

