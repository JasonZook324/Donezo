using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Donezo.Pages.Components;

public class DualHeaderView : ContentView
{
    // Page title (center)
    public static readonly BindableProperty TitleTextProperty = BindableProperty.Create(
        nameof(TitleText), typeof(string), typeof(DualHeaderView), string.Empty, propertyChanged: (b, o, n) => ((DualHeaderView)b).UpdatePageTitle());
    public string TitleText { get => (string)GetValue(TitleTextProperty); set => SetValue(TitleTextProperty, value); }

    // Tagline (left under app name)
    public static readonly BindableProperty TaglineProperty = BindableProperty.Create(
        nameof(Tagline), typeof(string), typeof(DualHeaderView), "Organize your tasks", propertyChanged: (b, o, n) => ((DualHeaderView)b).UpdateTagline());
    public string Tagline { get => (string)GetValue(TaglineProperty); set => SetValue(TaglineProperty, value); }

    public static readonly BindableProperty ShowLogoutProperty = BindableProperty.Create(
        nameof(ShowLogout), typeof(bool), typeof(DualHeaderView), false, propertyChanged: (b, o, n) => ((DualHeaderView)b).UpdateLogoutVisibility());
    public bool ShowLogout { get => (bool)GetValue(ShowLogoutProperty); set => SetValue(ShowLogoutProperty, value); }

    // Events
    public event EventHandler<bool>? ThemeToggled;
    public event EventHandler? LogoutRequested;

    private readonly Label _appTitleLabel;
    private readonly Label _taglineLabel;
    private readonly Label _pageTitleLabel;
    private readonly Button _logoutButton;
    private readonly ThemeToggleView _toggle;
    private readonly Grid _root;

    public DualHeaderView()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        BackgroundColor = primary; // single bar uses brand color background
        Padding = new Thickness(20, 18, 20, 18);
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged; // subscribe
        _toggle = new ThemeToggleView();
        _toggle.Toggled += (_, dark) => ThemeToggled?.Invoke(this, dark);

        _logoutButton = new Button
        {
            Text = "Logout",
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.White,
            BorderColor = Colors.White,
            BorderWidth = 1,
            CornerRadius = 8,
            Padding = new Thickness(12,6)
        };
        _logoutButton.Clicked += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _logoutButton.IsVisible = false;

        // Logo (circle + check)
        var logoSize = 42d;
        var circle = new Ellipse { WidthRequest = logoSize, HeightRequest = logoSize, Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 3, Fill = Colors.Transparent };
        var check = new Polyline { Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Points = new PointCollection { new(13,22), new(20,30), new(31,16) } };
        var logoGrid = new Grid { WidthRequest = logoSize, HeightRequest = logoSize, HorizontalOptions = LayoutOptions.Start };
        logoGrid.Add(circle); logoGrid.Add(check);
        SemanticProperties.SetDescription(logoGrid, "Donezo application logo");

        _appTitleLabel = new Label { Text = "Donezo", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        _taglineLabel = new Label { Text = Tagline, FontSize = 13, TextColor = Colors.White.WithAlpha(0.90f) };
        var leftStack = new HorizontalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                logoGrid,
                new VerticalStackLayout { Spacing = 2, Children = { _appTitleLabel, _taglineLabel } }
            }
        };

        _pageTitleLabel = new Label
        {
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true // allow clicks to pass to underlying controls
        };
        UpdatePageTitle();
        SemanticProperties.SetHeadingLevel(_pageTitleLabel, SemanticHeadingLevel.Level1);

        // Right controls
        var rightStack = new HorizontalStackLayout
        {
            Spacing = 14,
            VerticalOptions = LayoutOptions.Center,
            Children = { _toggle, _logoutButton }
        };

        _root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto), // left logo/app
                new ColumnDefinition(GridLength.Star), // middle filler
                new ColumnDefinition(GridLength.Auto)  // right controls
            }
        };
        _root.Add(leftStack, 0, 0);
        _root.Add(rightStack, 2, 0);
        // Overlay page title spanning all columns for true center relative to full width
        Grid.SetColumn(_pageTitleLabel, 0);
        Grid.SetColumnSpan(_pageTitleLabel, 3);
        _root.Add(_pageTitleLabel);
        Content = _root;
    }

    private void UpdatePageTitle() => _pageTitleLabel.Text = TitleText;
    private void UpdateTagline() => _taglineLabel.Text = Tagline;
    private void UpdateLogoutVisibility() => _logoutButton.IsVisible = ShowLogout;

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        _toggle.SetState(e.RequestedTheme == AppTheme.Dark, suppressEvent:true, animate:true);
    }

    protected override void OnParentChanged()
    {
        base.OnParentChanged();
        if (Parent == null)
        {
            try { Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged; } catch { }
        }
    }

    public void SyncFromAppTheme()
    {
        if (Application.Current is App app)
            _toggle.SetState(app.UserAppTheme == AppTheme.Dark, suppressEvent:true, animate:true);
    }

    public void SetTheme(bool dark, bool suppressEvent = true)
    {
        _toggle.SetState(dark, suppressEvent, animate:true);
        // Background stays primary; controls already update via app theme.
    }
}
