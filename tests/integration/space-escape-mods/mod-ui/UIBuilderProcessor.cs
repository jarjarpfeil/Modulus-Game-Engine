// UIBuilderProcessor.cs — Port of UIScript.Start() and UI state transitions
// Tests: UI element creation, button event wiring, event bus publishing

using System;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace ModUI;

/// <summary>
/// Processor that creates UI elements and handles screen transitions.
/// Ported from SpaceEscape's UIScript (StartupScript).
/// </summary>
public class UIBuilderProcessor : EntityProcessor<UIStateComponent>
{
    private IModEventBus? _eventBus;
    private bool _initialized;
    private SpriteFont? _font;
    private SpriteSheet? _uiImages;
    private ISpriteProvider? _buttonImage;

    public override void Update(GameTime gameTime)
    {
        if (!_initialized)
        {
            _eventBus = Services.GetService<IModEventBus>();
            _initialized = true;
        }

        foreach (var kvp in ComponentDatas)
        {
            var component = kvp.Key;
            EnsureUIElements(component);
            UpdateUI(component);
        }
    }

    private void EnsureUIElements(UIStateComponent ui)
    {
        if (ui.MainMenuRoot != null) return; // Already created

        // Load font and sprite sheet from content
        _font ??= LoadFont();
        _uiImages ??= LoadUISprites();
        _buttonImage = _uiImages != null ? SpriteFromSheet.Create(_uiImages, "button") : null;

        CreateMainMenuUI(ui);
        CreateGameUI(ui);
        CreateGameOverUI(ui);

        // Show main menu by default
        ApplyScreen(ui, UIScreen.MainMenu);
    }

    private SpriteFont? LoadFont()
    {
        try
        {
            var content = Services.GetService<Stride.Core.Serialization.Contents.IContentManager>();
            return content?.Load<SpriteFont>("Font");
        }
        catch
        {
            // Font not available — UI will render without text
            return null;
        }
    }

    private SpriteSheet? LoadUISprites()
    {
        try
        {
            var content = Services.GetService<Stride.Core.Serialization.Contents.IContentManager>();
            return content?.Load<SpriteSheet>("UIImages");
        }
        catch
        {
            return null;
        }
    }

    private void CreateMainMenuUI(UIStateComponent ui)
    {
        var startButton = CreateButton("Touch to Start", _buttonImage);
        startButton.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 1f));
        startButton.SetCanvasRelativePosition(new Vector3(0.5f, 0.8f, 0f));
        startButton.Click += (_, _) => _eventBus?.Publish(new StartGameRequest());

        var canvas = new Canvas();
        canvas.Children.Add(startButton);

        ui.MainMenuRoot = new ModalElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = canvas
        };
    }

    private void CreateGameUI(UIStateComponent ui)
    {
        var distanceText = new TextBlock
        {
            Font = _font,
            TextColor = Color.Gold,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Distance :      0"
        };
        distanceText.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 1f));
        distanceText.SetCanvasRelativePosition(new Vector3(0.2f, 0.05f, 0f));

        ui.DistanceTextBlock = distanceText;

        ui.GameRoot = new Canvas();
        ui.GameRoot.Children.Add(distanceText);
    }

    private void CreateGameOverUI(UIStateComponent ui)
    {
        var menuButton = CreateButton("Menu", _buttonImage);
        menuButton.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 1f));
        menuButton.SetCanvasRelativePosition(new Vector3(0.70f, 0.7f, 0f));
        menuButton.Click += (_, _) => _eventBus?.Publish(new GoToMenuRequest());

        var retryButton = CreateButton("Retry", _buttonImage);
        retryButton.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 1f));
        retryButton.SetCanvasRelativePosition(new Vector3(0.3f, 0.7f, 0f));
        retryButton.Click += (_, _) => _eventBus?.Publish(new RestartGameRequest());

        var gameOverCanvas = new Canvas();
        gameOverCanvas.Children.Add(menuButton);
        gameOverCanvas.Children.Add(retryButton);

        ui.GameOverRoot = new ModalElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = gameOverCanvas,
            MinimumWidth = 200f
        };
    }

    private Button CreateButton(string text, ISpriteProvider? image)
    {
        return new Button
        {
            Content = new TextBlock
            {
                Font = _font,
                Text = text,
                TextColor = Color.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            NotPressedImage = image,
            PressedImage = image,
            MouseOverImage = image,
            Padding = new Thickness(80, 27, 25, 35),
            MinimumWidth = 250f
        };
    }

    private void UpdateUI(UIStateComponent ui)
    {
        if (ui.DistanceTextBlock == null) return;
        ui.DistanceTextBlock.Text = $"Distance : {ui.Distance,6}";
    }

    private void ApplyScreen(UIStateComponent ui, UIScreen screen)
    {
        ui.CurrentScreen = screen;
        var uiComponent = ui.Entity.Get<UIComponent>();
        if (uiComponent == null)
        {
            uiComponent = new UIComponent();
            ui.Entity.Add(uiComponent);
        }

        uiComponent.Page = screen switch
        {
            UIScreen.MainMenu => new UIPage { RootElement = ui.MainMenuRoot },
            UIScreen.Playing => new UIPage { RootElement = ui.GameRoot },
            UIScreen.GameOver => new UIPage { RootElement = ui.GameOverRoot },
            _ => uiComponent.Page
        };
    }

    /// <summary>
    /// Called externally to change the UI screen.
    /// </summary>
    public void SetScreen(UIStateComponent ui, UIScreen screen)
    {
        ApplyScreen(ui, screen);
    }
}