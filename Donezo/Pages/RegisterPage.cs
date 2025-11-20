using Microsoft.Maui.Controls;
using Donezo.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls.Shapes;
using System.Text.RegularExpressions;
using System.Linq;

namespace Donezo.Pages;

public class RegisterPage : ContentPage
{
    private readonly INeonDbService _db;

    private Entry _usernameEntry = null!;
    private Entry _passwordEntry = null!;
    private Entry _confirmEntry = null!;
    private Entry _emailEntry = null!;
    private Entry _firstNameEntry = null!;
    private Entry _lastNameEntry = null!;
    private Label _errorLabel = null!;
    private Label _passwordStrengthLabel = null!;
    private Button _registerButton = null!;
    private Button _togglePasswordBtn = null!;
    private Button _toggleConfirmBtn = null!;
    private ActivityIndicator _loadingIndicator = null!;
    private Border _card = null!;

    private const double FormMaxWidth = 830; // midpoint width

    public RegisterPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }
    public RegisterPage(INeonDbService db)
    {
        _db = db;
        Title = "Register";
        BuildUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _card.Opacity = 0;
        await _card.FadeTo(1, 300, Easing.CubicOut);
    }

    private void BuildUi()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];

        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegUsername" };
        _emailEntry = new Entry { Placeholder = "email", Keyboard = Keyboard.Email, Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegEmail" };
        _firstNameEntry = new Entry { Placeholder = "first name", Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegFirst" };
        _lastNameEntry = new Entry { Placeholder = "last name", Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegLast" };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegPassword" };
        _confirmEntry = new Entry { Placeholder = "confirm password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"], AutomationId = "RegConfirm" };

        _togglePasswordBtn = new Button { Text = "Show", FontSize = 12, Padding = new Thickness(10,4), Style = (Style)Application.Current!.Resources["OutlinedButton"], AutomationId = "ToggleRegPassword" };
        _toggleConfirmBtn = new Button { Text = "Show", FontSize = 12, Padding = new Thickness(10,4), Style = (Style)Application.Current!.Resources["OutlinedButton"], AutomationId = "ToggleRegConfirm" };
        _togglePasswordBtn.Clicked += (_, _) => ToggleVisibility(_passwordEntry, _togglePasswordBtn);
        _toggleConfirmBtn.Clicked += (_, _) => ToggleVisibility(_confirmEntry, _toggleConfirmBtn);

        _passwordStrengthLabel = new Label { FontSize = 12, TextColor = Colors.Gray, AutomationId = "RegPasswordStrength" };

        _errorLabel = new Label { TextColor = Colors.Red, FontAttributes = FontAttributes.Bold, IsVisible = false, AutomationId = "RegError" };

        _registerButton = new Button { Text = "Create Account", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false, AutomationId = "RegSubmit" };
        _registerButton.Clicked += OnRegisterClicked;

        _loadingIndicator = new ActivityIndicator { IsVisible = false, IsRunning = false, Color = primary, AutomationId = "RegLoading" };

        // Hook validation
        _usernameEntry.TextChanged += (_, _) => ValidateAll();
        _emailEntry.TextChanged += (_, _) => ValidateAll();
        _firstNameEntry.TextChanged += (_, _) => ValidateAll();
        _lastNameEntry.TextChanged += (_, _) => ValidateAll();
        _passwordEntry.TextChanged += (_, _) => { ValidateAll(); UpdatePasswordStrength(); };
        _confirmEntry.TextChanged += (_, _) => ValidateAll();

        _usernameEntry.Completed += OnAnyEntryCompleted;
        _emailEntry.Completed += OnAnyEntryCompleted;
        _firstNameEntry.Completed += OnAnyEntryCompleted;
        _lastNameEntry.Completed += OnAnyEntryCompleted;
        _passwordEntry.Completed += OnAnyEntryCompleted;
        _confirmEntry.Completed += OnAnyEntryCompleted;

        var logo = BuildLogoView();
        var tabsRow = BuildInlineTabs();
        var header = new Label { Text = "Create Account", FontAttributes = FontAttributes.Bold, FontSize = 26, HorizontalTextAlignment = TextAlignment.Center, TextColor = primary };
        var tagline = new Label { Text = "Start organizing your tasks", FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, TextColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "Gray400" : "Gray600"], Margin = new Thickness(0,2,0,18) };

        var pwRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        pwRow.Add(_passwordEntry, 0, 0); pwRow.Add(_togglePasswordBtn, 1, 0);
        var confirmRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        confirmRow.Add(_confirmEntry, 0, 0); confirmRow.Add(_toggleConfirmBtn, 1, 0);

        var formStack = new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                tabsRow,
                header,
                tagline,
                _errorLabel,
                new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                _usernameEntry,
                new Label { Text = "Email", FontAttributes = FontAttributes.Bold },
                _emailEntry,
                new Label { Text = "First Name", FontAttributes = FontAttributes.Bold },
                _firstNameEntry,
                new Label { Text = "Last Name", FontAttributes = FontAttributes.Bold },
                _lastNameEntry,
                new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                pwRow,
                _passwordStrengthLabel,
                new Label { Text = "Confirm Password", FontAttributes = FontAttributes.Bold },
                confirmRow,
                _registerButton,
                _loadingIndicator
            }
        };

        _card = new Border
        {
            StrokeThickness = 1,
            Stroke = primary,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) },
            Padding = new Thickness(32, 36),
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            WidthRequest = FormMaxWidth,
            Content = formStack,
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0,4), Radius = 18f, Opacity = 0.25f }
        };
        if (Application.Current.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) _card.Style = style;

        var outerStack = new VerticalStackLayout
        {
            Spacing = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { logo, _card }
        };

        var scroll = new ScrollView { Content = outerStack };
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0,0),
            EndPoint = new Point(0,1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(primary.WithAlpha(0.30f),0),
                new GradientStop(primary.WithAlpha(0.05f),1)
            }
        };

        Content = new Grid { Background = gradient, Children = { scroll } };
    }

    private void ToggleVisibility(Entry entry, Button btn)
    {
        entry.IsPassword = !entry.IsPassword;
        btn.Text = entry.IsPassword ? "Show" : "Hide";
    }

    private void UpdatePasswordStrength()
    {
        var pwd = _passwordEntry.Text ?? string.Empty;
        if (string.IsNullOrEmpty(pwd)) { _passwordStrengthLabel.Text = string.Empty; return; }
        int score = 0;
        if (pwd.Length >= 8) score++;
        if (Regex.IsMatch(pwd, "[A-Z]") && Regex.IsMatch(pwd, "[a-z]")) score++;
        if (Regex.IsMatch(pwd, "[0-9]") && Regex.IsMatch(pwd, "[^A-Za-z0-9]")) score++;
        if (pwd.Length >= 12) score++;
        string text = score switch { 0 => "Weak", 1 => "Fair", 2 => "Good", 3 => "Strong", _ => "Excellent" };
        Color col = score switch { 0 => Colors.Red, 1 => Colors.Orange, 2 => Colors.Goldenrod, 3 => Colors.Green, _ => Colors.Teal };
        _passwordStrengthLabel.Text = $"Password Strength: {text}";
        _passwordStrengthLabel.TextColor = col;
    }

    private void ValidateAll()
    {
        _errorLabel.IsVisible = false;
        bool ok = !string.IsNullOrWhiteSpace(_usernameEntry.Text)
            && !string.IsNullOrWhiteSpace(_emailEntry.Text)
            && !string.IsNullOrWhiteSpace(_firstNameEntry.Text)
            && !string.IsNullOrWhiteSpace(_lastNameEntry.Text)
            && !string.IsNullOrWhiteSpace(_passwordEntry.Text)
            && !string.IsNullOrWhiteSpace(_confirmEntry.Text)
            && _passwordEntry.Text == _confirmEntry.Text
            && IsValidEmail(_emailEntry.Text!);
        _registerButton.IsEnabled = ok && !_loadingIndicator.IsRunning;
    }

    private static bool IsValidEmail(string email) => Regex.IsMatch(email, @"^[^@\n]+@[^@\n]+\.[^@\n]+$");

    private void OnAnyEntryCompleted(object? sender, EventArgs e) => OnRegisterClicked(sender!, e);

    private View BuildInlineTabs()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        var loginBtn = new Button { Text = "Login", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 14, Padding = new Thickness(14,6), CornerRadius = 20, AutomationId = "TabLogin" };
        var registerBtn = new Button { Text = "Register", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 14, Padding = new Thickness(14,6), CornerRadius = 20, AutomationId = "TabRegister" };
        void SyncActive()
        {
            var route = Shell.Current?.CurrentState?.Location?.ToString() ?? string.Empty;
            bool onLogin = route.Contains("login", StringComparison.OrdinalIgnoreCase);
            bool onRegister = route.Contains("register", StringComparison.OrdinalIgnoreCase);
            loginBtn.BorderColor = primary; registerBtn.BorderColor = primary;
            loginBtn.BackgroundColor = onLogin ? primary.WithAlpha(0.18f) : Colors.Transparent;
            registerBtn.BackgroundColor = onRegister ? primary.WithAlpha(0.18f) : Colors.Transparent;
        }
        loginBtn.Clicked += async (_, _) => { await Shell.Current.GoToAsync("//login"); SyncActive(); };
        registerBtn.Clicked += async (_, _) => { await Shell.Current.GoToAsync("//register"); SyncActive(); };
        SyncActive();
        return new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center, Children = { loginBtn, registerBtn } };
    }

    private View BuildLogoView()
    {
        var primary = (Color)Application.Current!.Resources["Primary"];        
        var size = 128d;
        var circle = new Ellipse { WidthRequest = size, HeightRequest = size, Stroke = new SolidColorBrush(primary), StrokeThickness = 8, Fill = Colors.Transparent };
        var check = new Polyline { Stroke = new SolidColorBrush(primary), StrokeThickness = 8, StrokeLineJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Points = new PointCollection { new(38,64), new(60,86), new(94,52) } };
        var grid = new Grid { HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0,0,0,4), WidthRequest = size, HeightRequest = size };
        grid.Add(circle); grid.Add(check); return grid;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        if (_loadingIndicator.IsRunning) return;
        if (!_registerButton.IsEnabled) { _errorLabel.Text = "Please complete all required fields."; _errorLabel.IsVisible = true; return; }
        if (_passwordEntry.Text != _confirmEntry.Text) { _errorLabel.Text = "Passwords do not match."; _errorLabel.IsVisible = true; return; }
        _loadingIndicator.IsVisible = true; _loadingIndicator.IsRunning = true; _registerButton.IsEnabled = false; _registerButton.Text = "Creating...";
        try
        {
            var ok = await _db.RegisterUserAsync(_usernameEntry.Text!, _passwordEntry.Text!, _emailEntry.Text!, _firstNameEntry.Text!, _lastNameEntry.Text!);
            if (ok) { await NavigateToDashboardAsync(_usernameEntry.Text!); return; }
            _errorLabel.Text = "Registration failed (username/email may exist)."; _errorLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            _errorLabel.Text = ex.Message; _errorLabel.IsVisible = true;
        }
        finally
        {
            _loadingIndicator.IsVisible = false; _loadingIndicator.IsRunning = false; _registerButton.Text = "Create Account"; ValidateAll();
        }
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
    }
}
