// CRT.fx
// Applies CRT monitor effect with scanlines, curvature, and color bleeding
// Shader ID: CombinedLayerCRT

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

float Curvature = 0.1;
float ScanlineIntensity = 0.3;
float ScanlineCount = 400.0;
float ChromaticAberration = 0.003;
float2 ScreenSize = float2(800.0, 600.0);

float4 MainPS(PixelShaderInput input) : COLOR
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
    
    float r = tex2D(SpriteTextureSampler, clamp(offsetR, 0.0, 1.0)).r;
    float g = tex2D(SpriteTextureSampler, uv).g;
    float b = tex2D(SpriteTextureSampler, clamp(offsetB, 0.0, 1.0)).b;
    float a = tex2D(SpriteTextureSampler, uv).a;
    
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


