// ModEntry.cs — Rendering mod entry point
// Tests: IMod lifecycle, render feature registration, event bus

using System;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace ModRendering;

public class ModEntry : IMod
{
    public string Id => "com.spaceescape.rendering";
    public string Name => "SpaceEscape Rendering Mod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    private IModContext? _context;
    private int _registrationAttempts;
    private int _registrationSuccesses;

    public void Initialize(IModContext context)
    {
        _context = context;

        try
        {
            context.Services.AddService<IModEventBus>(context.EventBus);
        }
        catch (Exception ex)
        {
            context.Logger.Warning($"[RenderingMod] Could not register event bus: {ex.Message}");
        }

        context.EventBus.Subscribe<RenderFeatureRegistrationEvent>(evt =>
        {
            _registrationAttempts++;
            if (evt.Success) _registrationSuccesses++;
        });

        context.Logger.Info("[RenderingMod] Initialized");
    }

    public void OnEnabled() => _context?.Logger.Info("[RenderingMod] Enabled");
    public void OnDisabled() => _context?.Logger.Info("[RenderingMod] Disabled");

    public int RegistrationAttempts => _registrationAttempts;
    public int RegistrationSuccesses => _registrationSuccesses;
}
