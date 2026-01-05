// Outline_PerEntity.fx
// Rainbow outline around sprite edges
// Shader ID: PerEntityOutline

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
float BaseThickness = 2.0;
float PulseAmount = 1.0;
float PulseSpeed = 3.0;
float RainbowSpeed = 1.0;
float2 SpriteSize = float2(32.0, 32.0);

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 pixelColor = tex2D(SpriteTextureSampler, uv);

    // OPAQUE pixel - check if it's on the edge (has transparent neighbor)
    if (pixelColor.a > 0.5)
    {
        float2 texelSize = 1.0 / SpriteSize;

        // Sample 4 cardinal neighbors
        float a1 = tex2D(SpriteTextureSampler, uv + float2(texelSize.x, 0)).a;
        float a2 = tex2D(SpriteTextureSampler, uv + float2(-texelSize.x, 0)).a;
        float a3 = tex2D(SpriteTextureSampler, uv + float2(0, texelSize.y)).a;
        float a4 = tex2D(SpriteTextureSampler, uv + float2(0, -texelSize.y)).a;

        // Count transparent neighbors
        float edgeCount = 0.0;
        if (a1 < 0.5) edgeCount += 1.0;
        if (a2 < 0.5) edgeCount += 1.0;
        if (a3 < 0.5) edgeCount += 1.0;
        if (a4 < 0.5) edgeCount += 1.0;

        // If this pixel has at least one transparent neighbor, it's on the edge
        if (edgeCount > 0.0)
        {
            // Rainbow color
            float hue = frac(uv.x * 2.0 + uv.y * 2.0 + Time * RainbowSpeed);
            float3 rainbowColor = hsv2rgb(float3(hue, 1.0, 1.0));

            // Pulsing brightness
            float pulse = sin(Time * PulseSpeed) * 0.3 + 0.7;
            rainbowColor *= pulse;

            // Blend rainbow with original based on how many edges
            float blendAmount = edgeCount * 0.25; // 0.25 to 1.0
            pixelColor.rgb = lerp(pixelColor.rgb, rainbowColor, blendAmount * 0.8);
        }

        return pixelColor * input.Color;
    }

    // TRANSPARENT pixel - don't draw anything
    return float4(0, 0, 0, 0);
}

technique Outline
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
