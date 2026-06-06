// UIStateComponent.cs — Port of SpaceEscape's UIScript state
// Tests: UI component creation from mod code, UI types across ALC

using Stride.Core;
using Stride.Engine;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace ModUI;

/// <summary>Component that holds UI state for the mod. Replaces UIScript.</summary>
[DataContract("UIStateComponent")]
[Display("UI State")]
public class UIStateComponent : EntityComponent
{
    [DataMember(10)]
    public UIScreen CurrentScreen { get; set; } = UIScreen.MainMenu;

    [DataMember(20)]
    public int Distance { get; set; }

    // Transient UI elements (not serialized)
    [DataMemberIgnore]
    public ModalElement? MainMenuRoot { get; set; }

    [DataMemberIgnore]
    public Canvas? GameRoot { get; set; }

    [DataMemberIgnore]
    public ModalElement? GameOverRoot { get; set; }

    [DataMemberIgnore]
    public TextBlock? DistanceTextBlock { get; set; }
}

public enum UIScreen { MainMenu, Playing, GameOver }
