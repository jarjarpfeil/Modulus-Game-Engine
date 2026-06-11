using Modulus.Modding.Api;
using Stride.Core;

namespace MyMod;

/// <summary>
/// Main entry point for MyMod.
/// Implement IMod to define your mod's lifecycle.
/// </summary>
public class ModEntry : IMod
{
    public string Id => "com.example.mymod";
    public string Name => "My Mod";
    public Version Version => new Version(1, 0, 0);
    public Version MinApiVersion => new Version(1, 0, 0);

    public void Initialize(IModContext context)
    {
        context.Logger.Info($"Initializing {Name} v{Version}");

        // Register components, subscribe to events, set up state
        // Example:
        //   context.EventBus.Subscribe<OnEntityCreated>(HandleEntityCreated);
    }

    public void OnEnabled()
    {
        // Called when the mod is enabled (or re-enabled after disable)
    }

    public void OnDisabled()
    {
        // Called when the mod is disabled (by user or error)
        // Clean up resources here
    }
}

/// <summary>
/// Example custom component. Add more components as needed.
/// </summary>
[DataContract("MyComponent")]
public class MyComponent : Stride.Engine.EntityComponent
{
    public float Value { get; set; } = 1.0f;
    public string Tag { get; set; } = "default";
}
