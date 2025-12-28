// DataStream.fx
// Layer 3: Falling data/matrix rain effect overlay
// DESIGNED FOR STACKING - additive overlay of streaming data particles

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
float StreamSpeed = 1.5;
float StreamDensity = 30.0;
float3 StreamColor = float3(0.0, 1.0, 0.5);
float StreamOpacity = 0.12;
float TrailLength = 0.3;

float hash(float n)
{
    return frac(sin(n) * 43758.5453);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float4 tex = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float3 color = tex.rgb;

    float2 uv = input.TextureCoordinates;
    float streamEffect = 0.0;

    // Create multiple stream columns
    float columnWidth = 1.0 / StreamDensity;
    float column = floor(uv.x / columnWidth);
    float columnX = frac(uv.x / columnWidth);

    // Each column has its own speed and phase
    float columnSeed = hash(column);
    float columnSpeed = 0.5 + columnSeed * 1.0;
    float columnPhase = columnSeed * 100.0;

    // Calculate falling position
    float fallPos = frac(Time * StreamSpeed * columnSpeed + columnPhase);

    // Create the stream trail
    float distFromHead = uv.y - fallPos;
    if (distFromHead < 0.0) distFromHead += 1.0; // Wrap around

    // Trail intensity (brightest at head, fading behind)
    float trail = 1.0 - distFromHead / TrailLength;
    trail = saturate(trail);
    trail = trail * trail; // Quadratic falloff

    // Only show in center of column (creates vertical lines)
    float columnMask = 1.0 - abs(columnX - 0.5) * 4.0;
    columnMask = saturate(columnMask);

    // Random "character" flicker along trail
    float charFlicker = hash(floor(uv.y * 50.0) + floor(Time * 10.0) + column);
    if (trail > 0.1)
    {
        trail *= 0.7 + charFlicker * 0.3;
    }

    // Bright head
    float head = smoothstep(0.02, 0.0, distFromHead);

    streamEffect = (trail * 0.6 + head * 1.0) * columnMask;

    // Some columns are brighter
    if (columnSeed > 0.7)
    {
        streamEffect *= 1.5;
    }

    // Random column activation (not all columns active)
    float columnActive = step(0.4, hash(column + floor(Time * 0.5)));
    streamEffect *= columnActive;

    // Horizontal scan line accent
    float scanY = frac(Time * 0.3);
    float scan = smoothstep(0.01, 0.0, abs(uv.y - scanY));

    // Apply stream overlay
    float3 streamFinal = StreamColor * streamEffect;
    streamFinal += StreamColor * scan * 0.2;

    color += streamFinal * StreamOpacity;

    // Slight glow on bright areas from streams
    color += streamFinal * streamEffect * 0.1;

    return float4(saturate(color), tex.a) * input.Color;
}

technique DataStream
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
