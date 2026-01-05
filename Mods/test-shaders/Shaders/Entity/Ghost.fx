// Ghost.fx
// Ethereal ghost effect applied TO the sprite
// Shader ID: PerEntityGhost

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
    MinFilter = Point;
    MagFilter = Point;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Time = 0.0;
float3 GhostTint = float3(0.7, 0.85, 1.0);
float Transparency = 0.5;
float WaveSpeed = 2.0;
float FlickerSpeed = 8.0;

float hash(float n)
{
    return frac(sin(n) * 43758.5453);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float n = i.x + i.y * 57.0;
    float a = hash(n);
    float b = hash(n + 1.0);
    float c = hash(n + 57.0);
    float d = hash(n + 58.0);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Wavy UV distortion for ghostly movement
    float wave = sin(uv.y * 8.0 + Time * WaveSpeed) * 0.01;
    wave += sin(uv.y * 12.0 - Time * WaveSpeed * 0.7) * 0.005;
    float2 distortedUV = uv + float2(wave, 0.0);

    float4 pixelColor = tex2D(SpriteTextureSampler, distortedUV);

    // Only affect opaque pixels
    if (pixelColor.a < 0.1)
    {
        return float4(0, 0, 0, 0);
    }

    // Desaturate
    float luminance = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    float3 ghostColor = lerp(pixelColor.rgb, float3(luminance, luminance, luminance), 0.6);

    // Tint with ghost color
    ghostColor = lerp(ghostColor, GhostTint, 0.4);

    // Brighten slightly for ethereal look
    ghostColor = ghostColor * 1.2 + 0.1;

    // Pulsing transparency
    float pulse = sin(Time * 2.0) * 0.1;

    // Flicker effect
    float flicker = 1.0;
    float flickerRand = hash(floor(Time * FlickerSpeed));
    if (flickerRand > 0.85)
    {
        flicker = 0.6 + flickerRand * 0.4;
    }

    // Internal wispy movement
    float wisp = noise(uv * 5.0 + float2(0, Time * 1.5));
    ghostColor = lerp(ghostColor, GhostTint * 1.3, wisp * 0.2);

    // Vertical fade for floating effect
    float verticalFade = 0.85 + uv.y * 0.15;

    float finalAlpha = pixelColor.a * (Transparency + pulse) * flicker * verticalFade;

    pixelColor = float4(ghostColor, finalAlpha);
    pixelColor *= input.Color;

    return pixelColor;
}

technique Ghost
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
