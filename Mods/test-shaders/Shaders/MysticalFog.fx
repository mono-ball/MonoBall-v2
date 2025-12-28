// MysticalFog.fx - Mystical Ground Fog / Mist Effect
// Ethereal fog that hugs the bottom of the screen with swirling wisps
// Shader ID: CombinedLayerMysticalFog

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

// Parameters
float Time = 0.0;
float2 ScreenSize = float2(1280.0, 720.0);

// Fog coverage and density
float FogHeight = 0.4;           // How high the fog reaches (0-1, from bottom)
float FogDensity = 0.6;          // Overall fog opacity
float FogFalloff = 2.5;          // How quickly fog fades at the top edge

// Movement parameters
float FlowSpeed = 0.15;          // Horizontal drift speed
float SwirleSpeed = 0.8;         // Swirl/turbulence animation speed
float WispSpeed = 0.25;          // Speed of individual wisp movement

// Detail parameters
float NoiseScale = 3.0;          // Scale of primary noise pattern
float WispScale = 8.0;           // Scale of wisp detail
float WispIntensity = 0.4;       // How visible individual wisps are

// Color parameters (ethereal blue-purple by default)
float3 FogColorPrimary = float3(0.75, 0.82, 0.95);   // Cool blue-white
float3 FogColorSecondary = float3(0.6, 0.55, 0.85); // Purple tint
float ColorVariation = 0.3;      // How much color shifts across fog

// Depth variation
float DepthVariation = 0.35;     // Creates denser pockets in the fog

// Blend mode: 0 = Screen blend, 1 = Soft light, 2 = Multiply overlay
float BlendMode = 0.0;

#define PI 3.14159265359

// Hash function for pseudo-random values
float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 2D hash for gradient noise
float2 hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// Smooth noise function
float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    // Smooth interpolation
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Fractal Brownian Motion for organic, cloud-like patterns
float fbm(float2 p, int octaves)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    float totalAmplitude = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += amplitude * noise(p * frequency);
        totalAmplitude += amplitude;
        amplitude *= 0.5;
        frequency *= 2.0;
    }

    return value / totalAmplitude;
}

// Swirling distortion for organic movement
float2 swirl(float2 uv, float time)
{
    float2 offset;
    offset.x = sin(uv.y * 4.0 + time * SwirleSpeed) * 0.02;
    offset.x += sin(uv.y * 7.0 - time * SwirleSpeed * 0.7) * 0.015;
    offset.y = cos(uv.x * 5.0 + time * SwirleSpeed * 0.8) * 0.01;
    offset.y += sin(uv.x * 3.0 + uv.y * 2.0 + time * SwirleSpeed * 0.5) * 0.008;
    return offset;
}

// Generate wispy tendrils
float wisps(float2 uv, float time)
{
    // Multiple layers of flowing wisps at different scales
    float2 wispUV1 = uv * WispScale + float2(time * WispSpeed, 0.0);
    float2 wispUV2 = uv * WispScale * 1.7 + float2(-time * WispSpeed * 0.6, time * 0.1);
    float2 wispUV3 = uv * WispScale * 0.5 + float2(time * WispSpeed * 0.3, -time * 0.05);

    // Create elongated wisp shapes using stretched noise
    float2 stretch = float2(2.0, 0.5); // Stretch horizontally
    float w1 = noise(wispUV1 * stretch);
    float w2 = noise(wispUV2 * stretch * 1.2);
    float w3 = noise(wispUV3 * stretch * 0.8);

    // Combine with varying weights
    float wispsValue = w1 * 0.5 + w2 * 0.35 + w3 * 0.15;

    // Create wispy threshold effect
    wispsValue = smoothstep(0.3, 0.7, wispsValue);

    return wispsValue;
}

// Depth pockets - creates denser areas in the fog
float depthPockets(float2 uv, float time)
{
    float2 pocketUV = uv * 2.0 + float2(time * 0.05, 0.0);
    float pocket = fbm(pocketUV, 3);
    pocket = smoothstep(0.3, 0.6, pocket);
    return pocket;
}

// Screen blend mode
float3 blendScreen(float3 base, float3 blend, float opacity)
{
    float3 result = 1.0 - (1.0 - base) * (1.0 - blend);
    return lerp(base, result, opacity);
}

// Soft light blend mode
float3 blendSoftLight(float3 base, float3 blend, float opacity)
{
    float3 result;
    result = (blend < 0.5) ?
        (2.0 * base * blend + base * base * (1.0 - 2.0 * blend)) :
        (sqrt(base) * (2.0 * blend - 1.0) + 2.0 * base * (1.0 - blend));
    return lerp(base, result, opacity);
}

// Multiply overlay blend
float3 blendMultiplyOverlay(float3 base, float3 blend, float opacity)
{
    float3 result = base * (1.0 - opacity * 0.5) + blend * opacity * 0.3;
    return result;
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Sample the base scene
    float4 sceneColor = tex2D(SpriteTextureSampler, uv);
    float3 color = sceneColor.rgb;
    float alpha = sceneColor.a;

    // Calculate fog mask based on vertical position
    // Fog is densest at bottom (uv.y = 1.0 in screen space, but we use inverted)
    float fogY = 1.0 - uv.y; // Invert so bottom of screen = 1.0

    // Create height-based fog falloff
    float heightMask = saturate(fogY / FogHeight);
    heightMask = pow(heightMask, 1.0 / FogFalloff); // Smooth falloff at top

    // Skip processing if above fog layer
    if (heightMask < 0.01)
    {
        return float4(color, alpha) * input.Color;
    }

    // Apply swirling distortion to fog sampling coordinates
    float2 swirlOffset = swirl(uv, Time);
    float2 fogUV = uv + swirlOffset;

    // Flowing horizontal movement
    float2 flowOffset = float2(Time * FlowSpeed, 0.0);

    // Generate base fog pattern using FBM
    float2 noiseUV = fogUV * NoiseScale + flowOffset;
    float baseFog = fbm(noiseUV, 4);

    // Add second layer moving in different direction
    float2 noiseUV2 = fogUV * NoiseScale * 0.7 - flowOffset * 0.5 + float2(0.0, Time * 0.03);
    float baseFog2 = fbm(noiseUV2, 3);

    // Combine fog layers
    float fogPattern = baseFog * 0.6 + baseFog2 * 0.4;

    // Add wispy details
    float wispPattern = wisps(fogUV, Time);
    fogPattern = lerp(fogPattern, wispPattern, WispIntensity);

    // Add depth variation (denser pockets)
    float depth = depthPockets(fogUV, Time);
    fogPattern = lerp(fogPattern, fogPattern * (1.0 + depth * 0.5), DepthVariation);

    // Apply density curve for more natural look
    fogPattern = smoothstep(0.2, 0.8, fogPattern);

    // Calculate final fog density
    float finalFogDensity = fogPattern * heightMask * FogDensity;

    // Color variation across the fog
    float colorMix = noise(fogUV * 2.0 + float2(Time * 0.1, 0.0));
    colorMix = colorMix * 0.5 + 0.25; // Keep in 0.25-0.75 range

    // Blend between primary and secondary fog colors
    float3 fogColor = lerp(FogColorPrimary, FogColorSecondary, colorMix * ColorVariation);

    // Add subtle brightness variation for ethereal glow
    float glow = sin(Time * 0.5) * 0.5 + 0.5;
    glow = 0.9 + glow * 0.1;
    fogColor *= glow;

    // Edge wisps - brighter tendrils at fog boundary
    float edgeWisp = smoothstep(0.3, 0.6, heightMask) * (1.0 - smoothstep(0.6, 0.9, heightMask));
    float wispBrightness = wisps(fogUV * 1.5, Time * 1.2);
    fogColor += edgeWisp * wispBrightness * 0.15 * FogColorPrimary;

    // Apply blend mode
    float3 blendedColor;

    if (BlendMode < 0.5)
    {
        // Screen blend - brightens, good for glowing fog
        blendedColor = blendScreen(color, fogColor, finalFogDensity);
    }
    else if (BlendMode < 1.5)
    {
        // Soft light - subtle, maintains contrast
        blendedColor = blendSoftLight(color, fogColor, finalFogDensity);
    }
    else
    {
        // Multiply overlay - darker, more mysterious
        blendedColor = blendMultiplyOverlay(color, fogColor, finalFogDensity);
    }

    // Final color with subtle desaturation in foggy areas for atmosphere
    float desatAmount = finalFogDensity * 0.15;
    float luminance = dot(blendedColor, float3(0.299, 0.587, 0.114));
    blendedColor = lerp(blendedColor, float3(luminance, luminance, luminance), desatAmount);

    // Ensure we don't over-brighten
    blendedColor = saturate(blendedColor);

    return float4(blendedColor, alpha) * input.Color;
}

technique MysticalFog
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
