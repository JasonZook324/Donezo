using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using Microsoft.Maui.Storage;

namespace Donezo.Pages;

public class LoginPage : ContentPage
{
    private readonly INeonDbService _db;

    private Entry _usernameEntry = null!;
    private Entry _passwordEntry = null!;
    private Label _loginErrorLabel = null!;

    private const double FormMaxWidth = 880; // further increased width

    // Parameterless ctor for XAML/Shell. Resolves service via ServiceHelper.
    public LoginPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }

    public LoginPage(INeonDbService db)
    {
        _db = db;
        Title = "Donezo"; // brand title
        BuildUi();
    }

    private void BuildUi()
    {
        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _usernameEntry.TextChanged += (_, _) => HideError();
        _passwordEntry.TextChanged += (_, _) => HideError();
        // ENTER submits form from any field
        _usernameEntry.Completed += OnAnyEntryCompleted;
        _passwordEntry.Completed += OnAnyEntryCompleted;

        var loginButton = new Button { Text = "Login", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        loginButton.Clicked += OnLoginClicked;

        _loginErrorLabel = new Label
        {
            TextColor = Colors.Red,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false
        };

        var logo = BuildLogoView();
        var tabsRow = BuildInlineTabs(); // now directly under logo for consistency
        var header = new Label
        {
            Text = "Login",
            FontAttributes = FontAttributes.Bold,
            FontSize = 24,
            HorizontalTextAlignment = TextAlignment.Center
        };
        var tagline = new Label
        {
            Text = "From to-do... to Donezo.",
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = (Color)Application.Current!.Resources["Primary"],
            Margin = new Thickness(0, 4, 0, 20)
        };

        // Inner form stack (constrained width)
        var formStack = new VerticalStackLayout
        {
            Padding = new Thickness(24, 40, 24, 24),
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = FormMaxWidth,
            // MaximumWidthRequest removed to force width
            Children =
            {
                logo,
                tabsRow,
                header,
                tagline,
                _loginErrorLabel,
                new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                _usernameEntry,
                new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                _passwordEntry,
                loginButton
            }
        };

        // Outer container to center content while allowing scrolling
        Content = new ScrollView
        {
            Content = new Grid
            {
                HorizontalOptions = LayoutOptions.Fill,
                Children = { formStack }
            }
        };
    }

    private View BuildInlineTabs()
    {
        var loginBtn = new Button
        {
            Text = "Login",
            Style = (Style)Application.Current!.Resources["OutlinedButton"],
            FontSize = 14,
            Padding = new Thickness(14,6),
            CornerRadius = 20
        };
        var registerBtn = new Button
        {
            Text = "Register",
            Style = (Style)Application.Current!.Resources["OutlinedButton"],
            FontSize = 14,
            Padding = new Thickness(14,6),
            CornerRadius = 20
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
            loginBtn.BackgroundColor = onLogin ? primary.WithAlpha(0.15f) : Colors.Transparent;
            registerBtn.BackgroundColor = onRegister ? primary.WithAlpha(0.15f) : Colors.Transparent;
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
            Margin = new Thickness(0,0,0,8),
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
        _loginErrorLabel.IsVisible = false; // reset

        var username = _usernameEntry.Text ?? string.Empty;
        var password = _passwordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Username and password required.");
            return;
        }

        var ok = await _db.AuthenticateUserAsync(username, password);
        if (!ok)
        {
            ShowError("Invalid username or password.");
            return;
        }

        await NavigateToDashboardAsync(username);
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
