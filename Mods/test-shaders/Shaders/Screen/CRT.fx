// CRT.fx
// Applies CRT monitor effect with scanlines, curvature, and color bleeding
// Shader ID: CombinedLayerCRT

#if OPENGL
    #define SAMPLE_TEXTURE(tex, samp, uv) tex2D(samp, uv)
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define SAMPLE_TEXTURE(tex, samp, uv) tex.Sample(samp, uv)
    #define VS_SHADERMODEL vs_6_0
    #define PS_SHADERMODEL ps_6_0
#endif

Texture2D SpriteTexture : register(t0);

SamplerState SpriteTextureSampler : register(s0);

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Curvature = 0.1;
float ScanlineIntensity = 0.3;
float ScanlineCount = 400.0;
float ChromaticAberration = 0.003;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : SV_Target
{
    // Normalize coordinates to center
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);
    float2 coord = uv - center;
    
    // Apply barrel distortion (CRT curvature)
    float2 offset = coord.yx / float2(6.0, 4.0);
    coord = coord + coord * offset * offset * Curvature;
    uv = coord + center;
    
    // Clamp to prevent sampling outside texture
    uv = clamp(uv, float2(0.0, 0.0), float2(1.0, 1.0));
    
    // Chromatic aberration (color separation)
    float2 offsetR = (uv - center) * (1.0 + ChromaticAberration) + center;
    float2 offsetB = (uv - center) * (1.0 - ChromaticAberration) + center;
    
    float r = SAMPLE_TEXTURE(SpriteTexture, SpriteTextureSampler, clamp(offsetR, 0.0, 1.0)).r;
    float g = SAMPLE_TEXTURE(SpriteTexture, SpriteTextureSampler, uv).g;
    float b = SAMPLE_TEXTURE(SpriteTexture, SpriteTextureSampler, clamp(offsetB, 0.0, 1.0)).b;
    float a = SAMPLE_TEXTURE(SpriteTexture, SpriteTextureSampler, uv).a;
    
    float4 pixelColor = float4(r, g, b, a);
    
    // Scanlines (horizontal lines)
    float scanline = sin(uv.y * ScanlineCount * 3.14159) * 0.5 + 0.5;
    scanline = lerp(1.0, scanline, ScanlineIntensity);
    pixelColor.rgb *= scanline;
    
    // Vignette (darkened edges)
    float vignette = 1.0 - length(coord) * 0.5;
    vignette = smoothstep(0.0, 1.0, vignette);
    pixelColor.rgb *= vignette;
    
    pixelColor *= input.Color;
    
    return pixelColor;
}

technique CRT
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}


