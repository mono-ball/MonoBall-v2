// Kaleidoscope.fx - Enhanced with rotation animation
// Hypnotic kaleidoscope mirror effect with smooth rotation
// Shader ID: CombinedLayerKaleidoscope

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

// Parameters
float Time = 0.0;
float SegmentCount = 6.0;
float RotationSpeed = 0.3;
float Zoom = 1.0;
float2 ScreenSize = float2(800.0, 600.0);

#define PI 3.14159265359

float4 MainPS(PixelShaderInput input) : SV_Target
{
    float2 uv = input.TextureCoordinates;
    float2 center = float2(0.5, 0.5);

    // Convert to polar coordinates centered on screen
    float2 coord = uv - center;

    // Apply zoom (pulling towards/away from center)
    coord *= Zoom;

    float angle = atan2(coord.y, coord.x);
    float radius = length(coord);

    // Add rotation over time
    angle += Time * RotationSpeed;

    // Calculate segment angle
    float segmentAngle = 2.0 * PI / SegmentCount;

    // Mirror within each segment for kaleidoscope effect
    float segmentIndex = floor(angle / segmentAngle);
    float localAngle = angle - segmentIndex * segmentAngle;

    // Mirror odd segments
    if (fmod(segmentIndex, 2.0) >= 1.0)
    {
        localAngle = segmentAngle - localAngle;
    }

    // Convert back to cartesian with mirrored angle
    float2 mirroredCoord = float2(cos(localAngle), sin(localAngle)) * radius;
    float2 mirroredUV = mirroredCoord + center;

    // Subtle pulsing zoom effect
    float pulseZoom = 1.0 + sin(Time * 0.5) * 0.05;
    mirroredUV = center + (mirroredUV - center) * pulseZoom;

    // Wrap coordinates for seamless tiling
    mirroredUV = frac(mirroredUV);

    float4 pixelColor = SAMPLE_TEXTURE(SpriteTexture, SpriteTextureSampler, mirroredUV);

    // Add subtle color shifting based on angle
    float hueShift = sin(Time * 0.2 + radius * 3.0) * 0.1;
    pixelColor.rgb = pixelColor.rgb * (1.0 + hueShift);

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
