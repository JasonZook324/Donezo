using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

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

    // Username shown top of menu
    public static readonly BindableProperty UsernameProperty = BindableProperty.Create(
        nameof(Username), typeof(string), typeof(DualHeaderView), string.Empty, propertyChanged: (b, o, n) => ((DualHeaderView)b).UpdateUsername());
    public string Username { get => (string)GetValue(UsernameProperty); set => SetValue(UsernameProperty, value); }

    // Events
    public event EventHandler<bool>? ThemeToggled;
    public event EventHandler? LogoutRequested;
    public event EventHandler? DashboardRequested;
    public event EventHandler? ManageAccountRequested; // navigates to //manageaccount route
    public event EventHandler? ManageListsRequested;
    // New: request page to toggle user menu overlay
    public event EventHandler? UserMenuToggleRequested;

    private readonly Label _appTitleLabel;
    private readonly Label _taglineLabel;
    private readonly Label _pageTitleLabel;
    private readonly ThemeToggleView _toggle;
    private readonly Grid _root;
    private readonly Grid _userIconGrid;

    // NOTE: Internal dropdown menu removed from layout; page will host overlay menu.
    private readonly VerticalStackLayout _menuStack;
    private readonly Label _usernameMenuLabel;

    public DualHeaderView()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        BackgroundColor = primary;
        Padding = new Thickness(20, 18, 20, 18);
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;

        _toggle = new ThemeToggleView();
        _toggle.Toggled += (_, dark) => ThemeToggled?.Invoke(this, dark);

        // User icon (head + shoulders)
        _userIconGrid = BuildUserIcon();
        var iconTap = new TapGestureRecognizer();
        iconTap.Tapped += (_, _) => ToggleMenu();
        _userIconGrid.GestureRecognizers.Add(iconTap);
        SemanticProperties.SetDescription(_userIconGrid, "Open user menu");

        // Menu content model (used by page overlay)
        _usernameMenuLabel = new Label { FontAttributes = FontAttributes.Bold, TextColor = Colors.White, FontSize = 14 };
        _menuStack = new VerticalStackLayout { Spacing = 6 };
        _menuStack.Children.Add(_usernameMenuLabel);
        _menuStack.Children.Add(new BoxView { HeightRequest = 1, HorizontalOptions = LayoutOptions.Fill, BackgroundColor = Colors.White.WithAlpha(0.25f) });
        _menuStack.Children.Add(BuildMenuItem("Dashboard", () => DashboardRequested?.Invoke(this, EventArgs.Empty)));
        _menuStack.Children.Add(BuildMenuItem("Manage Account", () => ManageAccountRequested?.Invoke(this, EventArgs.Empty)));
        _menuStack.Children.Add(BuildMenuItem("Manage Lists", () => ManageListsRequested?.Invoke(this, EventArgs.Empty)));
        _menuStack.Children.Add(new BoxView { HeightRequest = 1, HorizontalOptions = LayoutOptions.Fill, BackgroundColor = Colors.White.WithAlpha(0.25f) });
        _menuStack.Children.Add(BuildMenuItem("Logout", () => LogoutRequested?.Invoke(this, EventArgs.Empty), isDestructive:true));

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
            InputTransparent = true
        };
        UpdatePageTitle();
        SemanticProperties.SetHeadingLevel(_pageTitleLabel, SemanticHeadingLevel.Level1);

        var rightStack = new HorizontalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children = { _toggle, _userIconGrid }
        };

        _root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _root.Add(leftStack, 0, 0);
        _root.Add(rightStack, 2, 0);
        Grid.SetColumn(_pageTitleLabel, 0); Grid.SetColumnSpan(_pageTitleLabel, 3); _root.Add(_pageTitleLabel);

        Content = _root;
        UpdateUsername();
    }

    // Expose menu items stack so page can host it inside its own overlay
    public VerticalStackLayout GetMenuContentStack() => _menuStack;

    private View BuildMenuItem(string text, Action action, bool isDestructive = false)
    {
        // Wrap label in a border so we can highlight background on hover
        var border = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(8,6),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }
        };
        var lbl = new Label { Text = text, TextColor = isDestructive ? Colors.OrangeRed : Colors.White, FontSize = 14, VerticalTextAlignment = TextAlignment.Center };
        border.Content = lbl;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        border.GestureRecognizers.Add(tap);

        // Pointer (hover) events for highlight
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            border.BackgroundColor = isDestructive ? Colors.OrangeRed.WithAlpha(0.18f) : Colors.White.WithAlpha(0.18f);
        };
        pointer.PointerExited += (_, _) =>
        {
            border.BackgroundColor = Colors.Transparent;
        };
        border.GestureRecognizers.Add(pointer);

        return border;
    }

    private Grid BuildUserIcon()
    {
        var size = 40d;
        var head = new Ellipse { WidthRequest = 16, HeightRequest = 16, Fill = Colors.White, TranslationY = -4 };
        var body = new Microsoft.Maui.Controls.Shapes.Path
        {
            Stroke = Colors.White,
            StrokeThickness = 2,
            Data = new PathGeometry
            {
                Figures = new PathFigureCollection
                {
                    new PathFigure
                    {
                        StartPoint = new Point(8,20),
                        Segments = new PathSegmentCollection
                        {
                            new QuadraticBezierSegment { Point1 = new Point(4,32), Point2 = new Point(20,32) }
                        }
                    }
                }
            }
        };
        var circleBg = new Ellipse { WidthRequest = size, HeightRequest = size, Fill = Colors.Transparent, Stroke = Colors.White, StrokeThickness = 2 };
        var g = new Grid { WidthRequest = size, HeightRequest = size, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        g.Children.Add(circleBg);
        g.Children.Add(head);
        g.Children.Add(body);
        return g;
    }

    private void ToggleMenu()
    {
        if (string.IsNullOrWhiteSpace(Username)) return;
        // Delegate to page overlay instead of internal dropdown
        UserMenuToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePageTitle() => _pageTitleLabel.Text = TitleText;
    private void UpdateTagline() => _taglineLabel.Text = Tagline;
    private void UpdateUsername()
    {
        var hasUser = !string.IsNullOrWhiteSpace(Username);
        _usernameMenuLabel.Text = hasUser ? Username : string.Empty;
        _userIconGrid.IsVisible = hasUser;
    }

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
    }
}
