namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader region detection system.
///     Detects when players enter/exit shader regions and applies/reverts shaders.
/// </summary>
/// <remarks>
///     ShaderRegionDetectionSystem is update-driven with no public methods.
///     This interface exists for dependency inversion in IShaderSystems.
/// </remarks>
public interface IShaderRegionDetectionSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IShaderSystems
}
