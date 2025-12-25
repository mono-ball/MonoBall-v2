# Component Design Recommendation for ShaderParameterTimelineComponent

## Problem

`ShaderParameterTimelineComponent` needs to store multiple keyframes, but using `List<ShaderParameterKeyframe>` in a struct component violates ECS best practices.

## Codebase Pattern Analysis

The codebase already has struct components with reference types:
- `FlagsComponent` - `Dictionary<string, int>`, `byte[]`
- `VariablesComponent` - `Dictionary<string, string>`
- `AnimatedTileDataComponent` - `Dictionary<int, TileAnimationState>`
- `LayerShaderComponent` - `Dictionary<string, object>?`

**Pattern Used:**
- Struct components with reference types are acceptable
- Must be initialized (non-null) when component is created
- Document that mutation should be careful
- Systems handle initialization and mutation

## Options

### Option A: Dictionary in System (Recommended)

**Approach:** Store keyframes in the system, component only has metadata.

**Component:**
```csharp
public struct ShaderParameterTimelineComponent
{
    public string ParameterName { get; set; }
    public float ElapsedTime { get; set; }
    public bool IsLooping { get; set; }
    public bool IsEnabled { get; set; }
    public float Duration { get; set; }  // Calculated from keyframes
}
```

**System Storage:**
```csharp
private readonly Dictionary<Entity, List<ShaderParameterKeyframe>> _keyframes = new();
```

**Pros:**
- Component is pure value type
- Follows ECS best practices strictly
- Matches pattern used in `ShaderManagerSystem._previousParameterValues`
- Flexible (can add/remove keyframes dynamically)

**Cons:**
- Keyframes not directly accessible from component
- Need to look up keyframes in system

**Recommendation:** ✅ **Use this approach** - Cleanest architecture, matches existing patterns.

---

### Option B: Array in Component

**Approach:** Use fixed-size array in component.

**Component:**
```csharp
public struct ShaderParameterTimelineComponent
{
    public string ParameterName { get; set; }
    public ShaderParameterKeyframe[] Keyframes { get; set; }  // Must be initialized
    public int KeyframeCount { get; set; }  // Actual count (array may be larger)
    public float ElapsedTime { get; set; }
    public bool IsLooping { get; set; }
    public bool IsEnabled { get; set; }
}
```

**Pros:**
- Keyframes directly accessible from component
- Matches codebase pattern (other components use arrays/dictionaries)

**Cons:**
- Still has reference type in struct (same issue)
- Fixed size (or need to resize/reallocate)
- Less flexible than Option A

**Recommendation:** ⚠️ Acceptable but not ideal - Same issue as List, just less flexible.

---

### Option C: Separate KeyframeComponent per Keyframe

**Approach:** Each keyframe is a separate component.

**Components:**
```csharp
public struct ShaderParameterTimelineComponent
{
    public string ParameterName { get; set; }
    public float ElapsedTime { get; set; }
    public bool IsLooping { get; set; }
    public bool IsEnabled { get; set; }
}

public struct ShaderParameterKeyframeComponent
{
    public float Time { get; set; }
    public object Value { get; set; }
    public EasingFunction Easing { get; set; }
}
```

**Pros:**
- Pure ECS approach
- All components are value types
- Flexible (can add/remove keyframes by adding/removing components)

**Cons:**
- Many components per timeline (one per keyframe)
- Complex queries (need to query timeline + all keyframes)
- Harder to manage (need to track which keyframes belong to which timeline)

**Recommendation:** ❌ Too complex - Over-engineered for this use case.

---

## Recommendation: Option A

**Reasoning:**
1. Matches existing pattern in `ShaderManagerSystem` (`_previousParameterValues`)
2. Component stays pure value type
3. Flexible and easy to manage
4. Keyframes are typically set once and don't change frequently
5. System already needs to process timelines, so storing keyframes there makes sense

**Implementation:**
- Component: Pure value type with metadata only
- System: `Dictionary<Entity, List<ShaderParameterKeyframe>>` for keyframe storage
- When component is added: Initialize keyframes list in system
- When component is removed: Clean up keyframes from system dictionary

