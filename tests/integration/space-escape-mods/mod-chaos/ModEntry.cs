// ModEntry.cs — Adversarial mod that deliberately throws exceptions
// Tests: ModExceptionHandler, crash safety, event handler isolation, error boundaries

using System;
using System.IO;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Games;

namespace ModChaos;

/// <summary>Component for the chaos mod.</summary>
[DataContract("ChaosComponent")]
public class ChaosComponent : EntityComponent
{
    [DataMember(10)]
    public bool ThrowOnUpdate { get; set; }
}

/// <summary>Processor that optionally throws on update.</summary>
public class ChaosProcessor : EntityProcessor<ChaosComponent>
{
    private IModEventBus? _eventBus;

    public override void Update(GameTime gameTime)
    {
        _eventBus ??= Services.GetService<IModEventBus>();

        foreach (var kvp in ComponentDatas)
        {
            var component = kvp.Key;
            if (component.ThrowOnUpdate)
            {
                throw new InvalidOperationException("[ChaosMod] Intentional processor crash!");
            }
        }
    }
}

/// <summary>
/// Adversarial mod entry point. Tests:
/// 1. Initialize() that throws → mod marked Errored
/// 2. OnEnabled() that throws → mod marked Errored
/// 3. Event handler that throws → other handlers still fire
/// 4. Duplicate mod ID → rejected
/// 5. State persistence survives crash
/// </summary>
public class ModEntry : IMod, IModSerializable
{
    public string Id => "com.spaceescape.chaos";
    public string Name => "SpaceEscape Chaos Mod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    private IModContext? _context;
    private int _initializeCount;
    private int _enableCount;
    private int _disableCount;
    private int _eventHandlerCount;
    private bool _shouldThrowOnInitialize;
    private bool _shouldThrowOnEnable;

    public ModEntry()
    {
        // Default: throw on initialize to test crash safety
        _shouldThrowOnInitialize = true;
    }

    /// <summary>Configure whether Initialize() throws (for testing).</summary>
    public void SetThrowOnInitialize(bool value) => _shouldThrowOnInitialize = value;

    /// <summary>Configure whether OnEnabled() throws (for testing).</summary>
    public void SetThrowOnEnable(bool value) => _shouldThrowOnEnable = value;

    public void Initialize(IModContext context)
    {
        _context = context;
        _initializeCount++;

        try
        {
            context.Services.AddService<IModEventBus>(context.EventBus);
        }
        catch (Exception ex)
        {
            context.Logger.Warning($"[ChaosMod] Could not register event bus: {ex.Message}");
        }

        // Subscribe to events — handler intentionally throws
        context.EventBus.Subscribe<DistanceUpdatedEvent>(_ =>
        {
            _eventHandlerCount++;
            throw new NullReferenceException("[ChaosMod] Intentional event handler crash!");
        });

        context.Logger.Info("[ChaosMod] Initialize complete");

        if (_shouldThrowOnInitialize)
        {
            throw new InvalidOperationException("[ChaosMod] Intentional initialization failure!");
        }
    }

    public void OnEnabled()
    {
        _enableCount++;
        _context?.Logger.Info("[ChaosMod] Enabled");

        if (_shouldThrowOnEnable)
        {
            throw new AccessViolationException("[ChaosMod] Intentional enable failure!");
        }
    }

    public void OnDisabled()
    {
        _disableCount++;
        _context?.Logger.Info("[ChaosMod] Disabled");
    }

    // ── IModSerializable — state persistence across crash cycles ──

    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(_initializeCount);
        writer.Write(_enableCount);
        writer.Write(_disableCount);
        writer.Write(_eventHandlerCount);
    }

    public void Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        _initializeCount = reader.ReadInt32();
        _enableCount = reader.ReadInt32();
        _disableCount = reader.ReadInt32();
        _eventHandlerCount = reader.ReadInt32();
    }

    // ── Test Accessors ──

    public int InitializeCount => _initializeCount;
    public int EnableCount => _enableCount;
    public int DisableCount => _disableCount;
    public int EventHandlerCount => _eventHandlerCount;
}
