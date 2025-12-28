namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// Aggregate shader API interface combining entity, layer, and animation control.
    /// Extends IShaderEntityApi, IShaderLayerApi, and IShaderAnimationApi for convenience.
    /// Split per Interface Segregation Principle (ISP) - consumers can depend on specific sub-interfaces.
    /// </summary>
    public interface IShaderApi : IShaderEntityApi, IShaderLayerApi, IShaderAnimationApi
    {
        // Aggregate interface - no additional members.
        // All functionality provided by inherited interfaces:
        //
        // From IShaderEntityApi:
        //   - EnableShader, DisableShader, IsShaderEnabled
        //   - SetParameter, GetParameter, GetShaderId
        //
        // From IShaderLayerApi:
        //   - AddLayerShader, RemoveLayerShader
        //   - EnableLayerShader, DisableLayerShader
        //   - SetLayerParameter, GetLayerParameter
        //   - FindLayerShader, GetLayerShaders
        //
        // From IShaderAnimationApi:
        //   - AnimateParameter, StopAnimation
        //   - TransitionToShader
        //   - ApplyPreset
        //   - CreateAnimationChain
    }
}
