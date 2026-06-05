// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Wraps mod code execution in try/catch to ensure mods never crash the game.
/// Rule: Exceptions in mod code are caught, logged, and the mod is disabled.
/// </summary>
public sealed class ModExceptionHandler
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModExceptionHandler");

    /// <summary>
    /// Executes an action wrapped in mod exception handling.
    /// If the action throws, the exception is logged and onDisable is called.
    /// </summary>
    public void ExecuteModCode(string modId, Action action, Action onDisable)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModExceptionHandler] Mod '{modId}' threw exception: {ex.Message}");
            try { onDisable(); }
            catch (Exception disableEx)
            {
                Log.Error($"[ModExceptionHandler] Failed to disable mod '{modId}' after error: {disableEx.Message}");
            }
        }
    }

    /// <summary>
    /// Executes a function wrapped in mod exception handling.
    /// Returns default(T) if the action throws.
    /// </summary>
    public T? ExecuteModCode<T>(string modId, Func<T> func, Action onDisable)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModExceptionHandler] Mod '{modId}' threw exception: {ex.Message}");
            try { onDisable(); }
            catch (Exception disableEx)
            {
                Log.Error($"[ModExceptionHandler] Failed to disable mod '{modId}' after error: {disableEx.Message}");
            }
            return default;
        }
    }

    /// <summary>
    /// Executes mod initialization code. Unlike ExecuteModCode, initialization
    /// failures should propagate so the caller can reject the mod.
    /// </summary>
    public void ExecuteModInitialize(string modId, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModExceptionHandler] Mod '{modId}' failed during initialization: {ex.Message}");
            throw new InvalidOperationException($"Mod '{modId}' initialization failed: {ex.Message}", ex);
        }
    }
}
