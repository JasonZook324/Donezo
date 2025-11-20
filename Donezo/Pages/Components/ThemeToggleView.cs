using System; // for Math
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Donezo.Pages.Components;

// Simple reusable visual theme toggle (sun/moon). Raises Toggled(bool dark).
public class ThemeToggleView : ContentView
{
    private readonly Grid _root;
    private readonly Grid _sunIcon;
    private readonly Grid _moonIcon;
    private bool _isDark;
    public bool IsDark => _isDark;
    public event EventHandler<bool>? Toggled;
    private Border _chromeBorder; // new: store border for dynamic theme refresh

    public ThemeToggleView()
    {
        HeightRequest = 40; WidthRequest = 40;
        _chromeBorder = new Border
        {
            StrokeThickness = 1,
            Stroke = (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? "Gray600" : "Gray300"],
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Padding = 0
        };
        var primary = (Color)Application.Current!.Resources["Primary"];
        _sunIcon = BuildSunIcon(primary);
        _moonIcon = BuildMoonIcon(primary);
        _moonIcon.Opacity = 0; _moonIcon.IsVisible = false;
        _root = new Grid { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        _root.Children.Add(_sunIcon);
        _root.Children.Add(_moonIcon);
        _chromeBorder.Content = _root;
        Content = _chromeBorder;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Toggle();
        GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(this, "Toggle application theme");
    }

    private Grid BuildSunIcon(Color c)
    {
        var g = new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };        
        var circle = new Ellipse { WidthRequest = 18, HeightRequest = 18, Fill = c, StrokeThickness = 0 };
        g.Children.Add(circle);
        // rays
        for (int i = 0; i < 8; i++)
        {
            double angle = i * 45 * Math.PI / 180.0;
            var line = new BoxView { WidthRequest = 3, HeightRequest = 8, BackgroundColor = c, CornerRadius = 1.5, Opacity = 0.9 };            
            var container = new Grid { WidthRequest = 40, HeightRequest = 40, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            line.Rotation = i * 45;
            line.TranslationX = Math.Cos(angle) * 12;
            line.TranslationY = Math.Sin(angle) * 12;
            container.Children.Add(line);
            g.Children.Add(container);
        }
        return g;
    }
    private Grid BuildMoonIcon(Color c)
    {
        var g = new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        // Crescent via two overlapping circles clipped: draw full circle then overlay smaller background circle offset.
        var baseCircle = new Ellipse { WidthRequest = 20, HeightRequest = 20, Fill = c };
        var cutCircle = new Ellipse { WidthRequest = 20, HeightRequest = 20, Fill = (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"], TranslationX = 6 };
        g.Children.Add(baseCircle);
        g.Children.Add(cutCircle);
        return g;
    }

    public void SetState(bool dark, bool suppressEvent = true, bool animate = false)
    {
        var changed = _isDark != dark;
        _isDark = dark;
        if (changed && animate)
        {
            // run animation but suppress event if requested
            _ = AnimateChangeAsync(suppressEvent ? null : (Action)(() => Toggled?.Invoke(this, _isDark)));
        }
        else
        {
            UpdateVisuals();
            if (changed && !suppressEvent) Toggled?.Invoke(this, _isDark);
        }
        RefreshChromeColors(); // always update border colors to match active theme
    }

    private async Task AnimateChangeAsync(Action? fireEvent = null)
    {
        var fadeIn = _isDark ? _moonIcon : _sunIcon;
        var fadeOut = _isDark ? _sunIcon : _moonIcon;
        fadeIn.Opacity = 0; fadeIn.Scale = 0.6; fadeIn.Rotation = -30; fadeIn.IsVisible = true;
        // parallel animations: fade, scale, rotation for fadeIn; fade & rotate for fadeOut
        var t1 = fadeOut.FadeTo(0, 160, Easing.CubicOut);
        var t2 = fadeIn.FadeTo(1, 220, Easing.CubicOut);
        var t3 = fadeIn.ScaleTo(1, 220, Easing.CubicOut);
        var t4 = fadeIn.RotateTo(0, 220, Easing.CubicOut);
        var t5 = fadeOut.RotateTo(30 * (_isDark ? -1 : 1), 180, Easing.CubicOut);
        await Task.WhenAll(t1, t2, t3, t4, t5);
        fadeOut.IsVisible = false; fadeOut.Opacity = 0; fadeOut.Scale = 1; fadeOut.Rotation = 0;
        UpdateAppTheme();
        RefreshChromeColors();
        fireEvent?.Invoke();
    }

    private async void Toggle()
    {
        _isDark = !_isDark;
        await AnimateChangeAsync(() => Toggled?.Invoke(this, _isDark));
    }

    private void UpdateVisuals()
    {
        _sunIcon.Opacity = _isDark ? 0 : 1;
        _moonIcon.Opacity = _isDark ? 1 : 0;
        _sunIcon.IsVisible = !_isDark;
        _moonIcon.IsVisible = _isDark;
        UpdateAppTheme();
    }

    private void UpdateAppTheme()
    {
        if (Application.Current is App app)
            app.UserAppTheme = _isDark ? AppTheme.Dark : AppTheme.Light;
    }

    private void RefreshChromeColors()
    {
        if (Application.Current == null) return;
        var isDark = Application.Current.RequestedTheme == AppTheme.Dark || (Application.Current is App app && app.UserAppTheme == AppTheme.Dark);
        _chromeBorder.BackgroundColor = (Color)Application.Current.Resources[isDark ? "OffBlack" : "White"];
        _chromeBorder.Stroke = (Color)Application.Current.Resources[isDark ? "Gray600" : "Gray300"];
    }
}
