// Rain.fx
// Pokemon Emerald-style rain effect using rain sprite sheet
// Shader ID: CombinedLayerRain
// Based on pokeemerald-expansion rain weather implementation

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;  // Main scene texture
Texture2D RainTexture;   // Rain sprite sheet texture

sampler SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
};

sampler RainTextureSampler = sampler_state
{
    Texture = <RainTexture>;
    AddressU = Wrap;      // Wrap for tiling
    AddressV = Wrap;
    MinFilter = Point;    // Point filtering for pixel-perfect sprite frames
    MagFilter = Point;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// Rain sprite sheet layout (based on Pokemon Emerald):
// - Frame 0: Falling raindrop (16x32, loops)
// - Frame 8, 16, 24, 32, 40: Splash frames
// Sprite sheet is typically 64x64 or 128x64 pixels
float RainSpriteSheetWidth = 128.0;   // Width of rain sprite sheet in pixels
float RainSpriteSheetHeight = 64.0;   // Height of rain sprite sheet in pixels
float RainFrameWidth = 16.0;          // Width of each frame
float RainFrameHeight = 32.0;         // Height of each frame

// Animation timing (based on Pokemon Emerald: 18 frames falling, then splash)
float RainFallDuration = 18.0;         // Frames a raindrop falls before splashing
float RainAnimationSpeed = 1.0;       // Animation speed multiplier
float Time = 0.0;                     // Time value for animation

// Movement (based on Pokemon Emerald: diagonal movement left and down)
// Normal rain: (-0x68, 0xD0) per frame in Q28.4 fixed point
// Converted: approximately (-6.5, 13.0) pixels per frame at 60 FPS
float2 RainMovement = float2(-0.1083, 0.2167);  // Per-frame movement (normalized)
float RainIntensity = 0.6;            // Overall rain visibility (0-1)
float RainDarkness = 0.15;            // How much to darken the scene (0-1)

// Multiple rain layers for density (Pokemon Emerald uses 10-24 sprites)
// Note: Using fixed loop count for shader model compatibility
float RainLayerCount = 10.0;           // Number of rain layers (0-24)
float2 ScreenSize = float2(800.0, 600.0);

// Helper function to sample rain sprite sheet at specific frame
float4 SampleRainFrame(float2 uv, float frameIndex)
{
    // Calculate frame position in sprite sheet
    float framesPerRow = RainSpriteSheetWidth / RainFrameWidth;
    float frameX = fmod(frameIndex, framesPerRow);
    float frameY = floor(frameIndex / framesPerRow);
    
    // Calculate UV offset for this frame
    float2 frameOffset = float2(frameX * RainFrameWidth, frameY * RainFrameHeight);
    float2 frameUV = (uv * float2(RainFrameWidth, RainFrameHeight) + frameOffset) / float2(RainSpriteSheetWidth, RainSpriteSheetHeight);
    
    return tex2D(RainTextureSampler, frameUV);
}

// Calculate which frame to show based on animation cycle
float GetRainFrameIndex(float layerTime, bool isDownpour)
{
    // Normal rain: falls for 18 frames, then splashes
    // Downpour: falls for 12 frames, then splashes faster
    float fallDuration = isDownpour ? 12.0 : 18.0;
    float cycleDuration = fallDuration + 7.0; // Fall + splash duration
    
    float cycleTime = fmod(layerTime, cycleDuration);
    
    if (cycleTime < fallDuration)
    {
        // Falling animation: frame 0, loops every 16 frames
        return 0.0;
    }
    else
    {
        // Splash animation: frames 8, 32, 40 (normal) or 8, 16, 24 (heavy)
        float splashTime = cycleTime - fallDuration;
        if (isDownpour)
        {
            // Heavy splash: frames 8, 16, 24
            if (splashTime < 3.0) return 8.0;
            else if (splashTime < 6.0) return 16.0;
            else return 24.0;
        }
        else
        {
            // Normal splash: frames 8, 32, 40
            if (splashTime < 3.0) return 8.0;
            else if (splashTime < 5.0) return 32.0;
            else return 40.0;
        }
    }
}

float4 MainPS(PixelShaderInput input) : COLOR
{
    // Sample the main scene
    float4 sceneColor = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    
    // Accumulate rain from multiple layers
    // Use fixed loop count (24 max, matching Pokemon Emerald's downpour)
    // RainLayerCount parameter controls how many layers contribute
    float4 rainAccum = float4(0.0, 0.0, 0.0, 0.0);
    float activeLayerCount = 0.0;
    
    // Create multiple rain layers with staggered timing and positions
    // Fixed loop count for shader model compatibility (24 max, matching Pokemon Emerald's downpour)
    for (int i = 0; i < 24; i++)
    {
        // Only process layers up to RainLayerCount
        if (float(i) < RainLayerCount)
        {
            activeLayerCount += 1.0;
        
        // Stagger each layer's timing (Pokemon Emerald uses spriteIndex * 9)
        float layerOffset = float(i) * 9.0;
        float layerTime = (Time * 60.0 * RainAnimationSpeed) + layerOffset;
        
        // Stagger positions (based on Pokemon Emerald's sprite coordinates)
        // Each sprite starts at different X/Y positions
        float2 layerPosOffset = float2(
            fmod(float(i) * 37.0, ScreenSize.x),
            fmod(float(i) * 23.0, ScreenSize.y)
        );
        
        // Calculate UV for this layer with diagonal movement
        // Movement is diagonal: left and down (like Pokemon Emerald)
        float2 rainUV = input.TextureCoordinates;
        rainUV.x = rainUV.x * ScreenSize.x + layerPosOffset.x;
        rainUV.y = rainUV.y * ScreenSize.y + layerPosOffset.y;
        
        // Apply diagonal movement based on time
        rainUV.x += layerTime * RainMovement.x;
        rainUV.y += layerTime * RainMovement.y;
        
        // Wrap coordinates for tiling
        rainUV.x = fmod(rainUV.x + ScreenSize.x, ScreenSize.x);
        rainUV.y = fmod(rainUV.y + ScreenSize.y, ScreenSize.y);
        
        // Normalize back to 0-1 range
        rainUV /= ScreenSize;
        
        // Scale UV to sample individual raindrops (tile the rain pattern)
        rainUV *= 4.0; // Adjust this to control rain density
        
        // Get current frame index for this layer
        bool isDownpour = (RainIntensity > 0.8);
        float frameIndex = GetRainFrameIndex(layerTime, isDownpour);
        
        // Sample rain texture at current frame
        float4 rainSample = SampleRainFrame(rainUV, frameIndex);
        
            // Only accumulate if rain is visible (alpha > 0)
            if (rainSample.a > 0.01)
            {
                rainAccum.rgb += rainSample.rgb * rainSample.a;
                rainAccum.a = max(rainAccum.a, rainSample.a);
            }
        }
    }
    
    // Normalize accumulated rain (average across active layers)
    if (activeLayerCount > 0.0)
    {
        rainAccum.rgb /= activeLayerCount;
        rainAccum.a = min(rainAccum.a, 1.0);
    }
    
    // Blend rain over scene
    float rainAlpha = rainAccum.a * RainIntensity;
    float4 finalColor = lerp(sceneColor, rainAccum, rainAlpha);
    
    // Additive blending for rain (makes it brighter/visible)
    finalColor.rgb += rainAccum.rgb * rainAlpha * 0.3;
    
    // Darken scene slightly (Pokemon Emerald applies color map index 3)
    finalColor.rgb *= (1.0 - RainDarkness);
    
    // Preserve scene alpha
    finalColor.a = sceneColor.a;
    finalColor *= input.Color;
    
    return finalColor;
}

technique Rain
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
