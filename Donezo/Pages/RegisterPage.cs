using Microsoft.Maui.Controls;
using Donezo.Services;
using Microsoft.Maui.Storage;

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

    private const double FormMaxWidth = 440; // constrained width for register form

    public RegisterPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }

    public RegisterPage(INeonDbService db)
    {
        _db = db;
        Title = "Register";
        BuildUi();
    }

    private void BuildUi()
    {
        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _emailEntry = new Entry { Placeholder = "email", Keyboard = Keyboard.Email, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _firstNameEntry = new Entry { Placeholder = "first name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _lastNameEntry = new Entry { Placeholder = "last name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _confirmEntry = new Entry { Placeholder = "confirm password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };

        var registerButton = new Button { Text = "Register", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        registerButton.Clicked += OnRegisterClicked;

        // ENTER submits form from any field
        _usernameEntry.Completed += OnAnyEntryCompleted;
        _emailEntry.Completed += OnAnyEntryCompleted;
        _firstNameEntry.Completed += OnAnyEntryCompleted;
        _lastNameEntry.Completed += OnAnyEntryCompleted;
        _passwordEntry.Completed += OnAnyEntryCompleted;
        _confirmEntry.Completed += OnAnyEntryCompleted;

        var tabsRow = BuildInlineTabs();

        var formStack = new VerticalStackLayout
        {
            Padding = new Thickness(24, 40, 24, 24),
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center,
            MaximumWidthRequest = FormMaxWidth,
            Children =
            {
                new Label { Text = "Create Account", FontAttributes = FontAttributes.Bold, FontSize = 24, HorizontalTextAlignment = TextAlignment.Center },
                tabsRow,
                new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                _usernameEntry,
                new Label { Text = "Email", FontAttributes = FontAttributes.Bold },
                _emailEntry,
                new Label { Text = "First Name", FontAttributes = FontAttributes.Bold },
                _firstNameEntry,
                new Label { Text = "Last Name", FontAttributes = FontAttributes.Bold },
                _lastNameEntry,
                new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                _passwordEntry,
                new Label { Text = "Confirm Password", FontAttributes = FontAttributes.Bold },
                _confirmEntry,
                registerButton
            }
        };

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
        SyncActive();

        return new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children = { loginBtn, registerBtn }
        };
    }

    private void OnAnyEntryCompleted(object? sender, EventArgs e)
    {
        OnRegisterClicked(sender!, e);
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var username = _usernameEntry.Text ?? string.Empty;
        var email = _emailEntry.Text ?? string.Empty;
        var first = _firstNameEntry.Text ?? string.Empty;
        var last = _lastNameEntry.Text ?? string.Empty;
        var password = _passwordEntry.Text ?? string.Empty;
        var confirm = _confirmEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            await DisplayAlert("Register", "All fields are required", "OK");
            return;
        }
        if (password != confirm)
        {
            await DisplayAlert("Register", "Passwords do not match", "OK");
            return;
        }

        var ok = await _db.RegisterUserAsync(username, password, email, first, last);
        if (ok)
        {
            await NavigateToDashboardAsync(username); // no popup on success
        }
        else
        {
            await DisplayAlert("Register", "Failed (username/email may already exist or input invalid)", "OK");
        }
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        // Navigate to dashboard route with username as query parameter and reset nav stack (hides TabBar)
        await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
    }
}
