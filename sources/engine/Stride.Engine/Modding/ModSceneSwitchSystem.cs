// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Games;

namespace Stride.Engine.Modding;

/// <summary>
/// Game system that processes pending scene switches at the end of each frame.
/// This ensures scene switches happen between frames to avoid timing issues.
/// </summary>
public class ModSceneSwitchSystem : GameSystemBase
{
    public ModSceneSwitchSystem(IServiceRegistry registry) : base(registry)
    {
        Enabled = true;
        Visible = false;
        // Run after all other systems to ensure scene switches happen at the end of the frame
        UpdateOrder = 1000;
    }

    public override void Update(GameTime gameTime)
    {
        var modSceneManager = Services.GetService<ModSceneManager>();
        modSceneManager?.ProcessPendingSwitch();
    }
}
