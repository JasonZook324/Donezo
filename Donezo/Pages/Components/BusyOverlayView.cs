using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Donezo.Pages.Components;

// Reusable full-screen busy overlay with message + spinner and nested depth support.
public class BusyOverlayView : ContentView
{
    private readonly Grid _root;
    private readonly ActivityIndicator _spinner;
    private readonly Label _messageLabel;
    private int _depth;

    public BusyOverlayView()
    {
        IsVisible = false; InputTransparent = false; Opacity = 0;
        _spinner = new ActivityIndicator { IsRunning = true, WidthRequest = 36, HeightRequest = 36, Color = (Color)Application.Current!.Resources["Primary"] };
        _messageLabel = new Label { Text = "Loading...", TextColor = Colors.White, FontSize = 14, HorizontalTextAlignment = TextAlignment.Center };
        var card = new Border
        {
            BackgroundColor = Colors.Black.WithAlpha(0.75f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(22,18),
            Content = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Center, Children = { _spinner, _messageLabel } }
        };
        _root = new Grid { Children = { new Grid { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { card } } } };
        Content = _root;
    }

    public void Show(string? message)
    {
        _depth++;
        _messageLabel.Text = string.IsNullOrWhiteSpace(message) ? "Loading..." : message;
        if (!IsVisible)
        {
            IsVisible = true; Opacity = 0; _ = this.FadeTo(1, 140, Easing.CubicOut);
        }
    }
    public void Hide()
    {
        if (_depth > 0) _depth--;
        if (_depth > 0) return;
        if (IsVisible)
        {
            _ = this.FadeTo(0, 110, Easing.CubicOut).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => IsVisible = false));
        }
    }
    public async Task RunAsync(Func<Task> work, string? message)
    {
        Show(message);
        try { await work(); }
        finally { Hide(); }
    }
}
