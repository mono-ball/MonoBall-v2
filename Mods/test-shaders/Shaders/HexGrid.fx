// HexGrid.fx
// Layer 2: Animated hexagonal grid overlay - cyberpunk/sci-fi pattern
// DESIGNED FOR STACKING - transparent overlay that adds texture

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
float2 ScreenSize = float2(1280.0, 720.0);
float GridScale = 40.0;
float LineThickness = 0.08;
float3 GridColor = float3(0.0, 1.0, 0.9);
float GridOpacity = 0.15;
float PulseSpeed = 2.0;

// Hexagonal distance function
float hexDist(float2 p)
{
    p = abs(p);
    return max(p.x * 0.866025 + p.y * 0.5, p.y);
}

float2 hexCoord(float2 uv)
{
    float2 r = float2(1.0, 1.732);
    float2 h = r * 0.5;
    float2 a = fmod(uv, r) - h;
    float2 b = fmod(uv - h, r) - h;
    return dot(a, a) < dot(b, b) ? a : b;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 tex = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float3 color = tex.rgb;

    // Create hex grid coordinates
    float2 screenUV = input.TextureCoordinates * ScreenSize / GridScale;
    float2 hex = hexCoord(screenUV);
    float d = hexDist(hex);

    // Calculate hex center for pulse effect
    float2 hexCenter = screenUV - hex;
    float pulse = sin(Time * PulseSpeed + hexCenter.x * 0.5 + hexCenter.y * 0.3) * 0.5 + 0.5;

    // Draw hex edges
    float edge = smoothstep(0.5 - LineThickness, 0.5, d);
    edge *= smoothstep(0.5 + LineThickness, 0.5, d);
    edge = 1.0 - edge;

    // Animated scan line moving across hex grid
    float scan = sin(hexCenter.y * 0.2 - Time * 1.5) * 0.5 + 0.5;
    scan = pow(scan, 8.0);

    // Combine effects
    float gridIntensity = edge * (0.3 + pulse * 0.7);
    gridIntensity += scan * 0.3;

    // Random hex highlights
    float hexId = frac(sin(dot(floor(hexCenter), float2(127.1, 311.7))) * 43758.5453);
    if (hexId > 0.95)
    {
        float blink = sin(Time * 5.0 + hexId * 100.0) * 0.5 + 0.5;
        gridIntensity += blink * 0.5;
    }

    // Apply grid overlay
    float3 gridEffect = GridColor * gridIntensity;
    color += gridEffect * GridOpacity;

    return float4(saturate(color), tex.a) * input.Color;
}

technique HexGrid
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
