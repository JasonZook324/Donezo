using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using Microsoft.Maui.Storage;
using System.Text.RegularExpressions;
using Donezo.Pages.Components; // add

namespace Donezo.Pages;

public class LoginPage : ContentPage
{
    private readonly INeonDbService _db;

    private Entry _usernameEntry = null!;
    private Entry _passwordEntry = null!;
    private Label _loginErrorLabel = null!;
    private Button _loginButton = null!;
    private Button _togglePasswordBtn = null!;
    private ActivityIndicator _loadingIndicator = null!;
    private Border _card = null!;

    // Theme header controls (logout removed per user request)
    private DualHeaderView _dualHeader = null!;
    private int? _currentUserId; // loaded if a user already logged in

    private const double FormMaxWidth = 830; // midpoint width

    // Parameterless ctor for XAML/Shell. Resolves service via ServiceHelper.
    public LoginPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }

    public LoginPage(INeonDbService db)
    {
        _db = db;
        Title = string.Empty; // remove shell title bar text
        Shell.SetNavBarIsVisible(this, false); // hide shell nav bar
        BuildUi();
        _ = InitializeHeaderUserContextAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // simple fade-in animation for card
        _card.Opacity = 0;
        await _card.FadeTo(1, 300, Easing.CubicOut);
    }

    private async Task InitializeHeaderUserContextAsync()
    {
        // Only load theme preference if a user is already logged in; no logout button shown.
        try
        {
            var username = await SecureStorage.GetAsync("AUTH_USERNAME");
            if (!string.IsNullOrWhiteSpace(username))
            {
                _currentUserId = await _db.GetUserIdAsync(username);
                await LoadThemePreferenceAsync();
            }
        }
        catch { }
    }

    private async Task LoadThemePreferenceAsync()
    {
        if (_currentUserId == null) return;
        var dark = await _db.GetUserThemeDarkAsync(_currentUserId.Value);
        _dualHeader.SetTheme(dark ?? false, suppressEvent:true);
        ApplyTheme(Application.Current!.RequestedTheme == AppTheme.Dark);
    }

    private void BuildUi()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];

        // Header: brand + visual theme toggle only
        _dualHeader = new DualHeaderView { TitleText = "Login", ShowLogout = false };
        _dualHeader.ThemeToggled += async (_, dark) => await OnThemeToggledAsync(dark);
        _dualHeader.SetTheme(Application.Current!.RequestedTheme == AppTheme.Dark, suppressEvent:true);

        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "LoginUsername" };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "LoginPassword" };
        _usernameEntry.TextChanged += (_, _) => { HideError(); ValidateFields(); };
        _passwordEntry.TextChanged += (_, _) => { HideError(); ValidateFields(); };
        // ENTER submits form from any field
        _usernameEntry.Completed += OnAnyEntryCompleted;
        _passwordEntry.Completed += OnAnyEntryCompleted;

        _togglePasswordBtn = new Button
        {
            Text = "Show",
            FontSize = 12,
            Padding = new Thickness(10, 4),
            Style = (Style)Application.Current.Resources["OutlinedButton"],
            AutomationId = "TogglePasswordVisibility"
        };
        _togglePasswordBtn.Clicked += (_, _) => TogglePasswordVisibility();

        _loginButton = new Button { Text = "Login", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false, AutomationId = "LoginSubmit" };
        _loginButton.Clicked += OnLoginClicked;

        _loginErrorLabel = new Label
        {
            TextColor = Colors.Red,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false,
            AutomationId = "LoginError"
        };

        _loadingIndicator = new ActivityIndicator { IsVisible = false, IsRunning = false, Color = primary, AutomationId = "LoginLoading" };

        var tabsRow = BuildInlineTabs();
        var header = new Label
        {
            Text = "Welcome Back",
            FontAttributes = FontAttributes.Bold,
            FontSize = 26,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = primary
        };
        var tagline = new Label
        {
            Text = "Sign in to access your lists",
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "Gray400" : "Gray600"],
            Margin = new Thickness(0, 2, 0, 18)
        };

        var passwordRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        passwordRow.Add(_passwordEntry, 0, 0);
        passwordRow.Add(_togglePasswordBtn, 1, 0);

        // Inner form stack (constrained width)
        var formStack = new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                tabsRow,
                header,
                tagline,
                _loginErrorLabel,
                new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                _usernameEntry,
                new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                passwordRow,
                _loginButton,
                _loadingIndicator
            }
        };

        // Outer card container
        _card = new Border
        {
            StrokeThickness = 1,
            Stroke = primary,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) },
            Padding = new Thickness(32, 36),
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            WidthRequest = FormMaxWidth,
            Content = formStack,
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 4), Radius = 18f, Opacity = 0.25f }
        };
        // allow style override if provided
        if (Application.Current.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) _card.Style = style;

        var logo = BuildLogoView();

        var outerStack = new VerticalStackLayout
        {
            Spacing = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { logo, _card }
        };

        var scroll = new ScrollView { Content = outerStack };

        // gradient background
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(primary.WithAlpha(0.30f), 0.0f),
                new GradientStop(primary.WithAlpha(0.05f), 1.0f)
            }
        };

        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, Background = gradient };
        root.Add(_dualHeader, 0, 0); root.Add(scroll, 0, 1);
        Content = root;
    }

    private async Task OnThemeToggledAsync(bool dark)
    {
        ApplyTheme(dark);
        if (_currentUserId != null)
        {
            try { await _db.SetUserThemeDarkAsync(_currentUserId.Value, dark); } catch { }
        }
    }

    private void ApplyTheme(bool dark)
    {
        if (Application.Current is App app) app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        _dualHeader?.SyncFromAppTheme();
    }

    private void TogglePasswordVisibility()
    {
        _passwordEntry.IsPassword = !_passwordEntry.IsPassword;
        _togglePasswordBtn.Text = _passwordEntry.IsPassword ? "Show" : "Hide";
    }

    private void ValidateFields()
    {
        bool valid = !string.IsNullOrWhiteSpace(_usernameEntry.Text) && !string.IsNullOrWhiteSpace(_passwordEntry.Text) && _passwordEntry.Text!.Length >= 3;
        _loginButton.IsEnabled = valid && !_loadingIndicator.IsRunning;
    }

    private View BuildInlineTabs()
    {
        var loginBtn = new Button
        {
            Text = "Login",
            Style = (Style)Application.Current!.Resources["OutlinedButton"],
            FontSize = 14,
            Padding = new Thickness(14,6),
            CornerRadius = 20,
            AutomationId = "TabLogin"
        };
        var registerBtn = new Button
        {
            Text = "Register",
            Style = (Style)Application.Current!.Resources["OutlinedButton"],
            FontSize = 14,
            Padding = new Thickness(14,6),
            CornerRadius = 20,
            AutomationId = "TabRegister"
        };

        // Visual selection state: emphasize active route
        void SyncActive()
        {
            var route = Shell.Current?.CurrentState?.Location?.ToString() ?? string.Empty;
            bool onLogin = route.Contains("login", StringComparison.OrdinalIgnoreCase);
            bool onRegister = route.Contains("register", StringComparison.OrdinalIgnoreCase);
            var primary = (Color)Application.Current!.Resources["Primary"];
            loginBtn.BorderColor = primary;
            registerBtn.BorderColor = primary;
            loginBtn.BackgroundColor = onLogin ? primary.WithAlpha(0.18f) : Colors.Transparent;
            registerBtn.BackgroundColor = onRegister ? primary.WithAlpha(0.18f) : Colors.Transparent;
        }

        loginBtn.Clicked += async (_, _) => { await Shell.Current.GoToAsync("//login"); SyncActive(); };
        registerBtn.Clicked += async (_, _) => { await Shell.Current.GoToAsync("//register"); SyncActive(); };

        // Initial state
        SyncActive();

        var row = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children = { loginBtn, registerBtn }
        };
        return row;
    }

    private void OnAnyEntryCompleted(object? sender, EventArgs e)
    {
        // Delegate to click handler so logic stays in one place
        OnLoginClicked(sender!, e);
    }

    private View BuildLogoView()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];        
        var size = 128d;
        var circle = new Ellipse
        {
            WidthRequest = size,
            HeightRequest = size,
            Stroke = new SolidColorBrush(primary),
            StrokeThickness = 8,
            Fill = Colors.Transparent
        };
        var check = new Polyline
        {
            Stroke = new SolidColorBrush(primary),
            StrokeThickness = 8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Points = new PointCollection
            {
                new(38, 64), new(60, 86), new(94, 52)
            }
        };
        var grid = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0,0,0,4),
            WidthRequest = size,
            HeightRequest = size
        };
        grid.Add(circle);
        grid.Add(check);
        return grid;
    }

    private void HideError()
    {
        if (_loginErrorLabel.IsVisible)
            _loginErrorLabel.IsVisible = false;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (_loadingIndicator.IsRunning) return;
        HideError();
        var username = _usernameEntry.Text ?? string.Empty;
        var password = _passwordEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) { ShowError("Username and password required."); return; }
        _loadingIndicator.IsVisible = true; _loadingIndicator.IsRunning = true; _loginButton.IsEnabled = false; _loginButton.Text = "Signing in...";
        try
        {
            var ok = await _db.AuthenticateUserAsync(username, password);
            if (!ok) { ShowError("Invalid username or password."); return; }
            await NavigateToDashboardAsync(username);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _loadingIndicator.IsRunning = false; _loadingIndicator.IsVisible = false; _loginButton.Text = "Login"; ValidateFields();
        }
    }

    private void ShowError(string message)
    {
        _loginErrorLabel.Text = message;
        _loginErrorLabel.IsVisible = true;
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        // Navigate to dashboard route with username as query parameter
        await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
    }
}
