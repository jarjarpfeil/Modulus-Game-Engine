using Modulus.Modding.Api;
using Stride.Core.Diagnostics;

namespace MyMod;

/// <summary>
/// Entry point for the MyMod mod.
/// Implements IMod — the stable ABI interface that the engine calls on load/unload.
/// </summary>
public class MyModEntry : IMod
{
    public string Id => "com.example.mymod";
    public string Name => "MyMod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    public void Initialize(IModContext context)
    {
        // Called when the mod is loaded by ModHost.
        // Access game services via context.Services.
        // Register event handlers via context.EventBus.
        context.Logger.Info($"[MyMod] Loaded successfully!");
    }

    public void OnEnabled()
    {
        // Called when the mod is enabled (or re-enabled after disable).
    }

    public void OnDisabled()
    {
        // Called when the mod is disabled.
        // Unsubscribe event handlers, release resources, cancel microthreads.
    }
}
