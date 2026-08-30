using Modulus.Modding.Api;
using Stride.Core.Diagnostics;

namespace TestMod;

/// <summary>
/// Entry point for the TestMod mod.
/// Implements IMod — the stable ABI interface that the engine calls on load/unload.
/// </summary>
public class TestModEntry : IMod
{
    public string Id => "com.example.testmod";
    public string Name => "TestMod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    public void Initialize(IModContext context)
    {
        // Called when the mod is loaded by ModHost.
        // Access game services via context.Services.
        // Register event handlers via context.EventBus.
        context.Logger.Info($"[TestMod] Loaded successfully!");
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
