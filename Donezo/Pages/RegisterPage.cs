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

    // simple arithmetic captcha
    private Label _captchaLabel = null!;
    private Entry _captchaEntry = null!;
    private int _captchaAnswer;

    private const double FormMaxWidth = 440; // constrained width for register form

    public RegisterPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }

    public RegisterPage(INeonDbService db)
    {
        _db = db;
        Title = "Register";
        BuildUi();
        GenerateCaptcha();
    }

    private void BuildUi()
    {
        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _emailEntry = new Entry { Placeholder = "email", Keyboard = Keyboard.Email, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _firstNameEntry = new Entry { Placeholder = "first name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _lastNameEntry = new Entry { Placeholder = "last name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _confirmEntry = new Entry { Placeholder = "confirm password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };

        _captchaLabel = new Label { FontAttributes = FontAttributes.Bold };
        _captchaEntry = new Entry { Placeholder = "answer", Keyboard = Keyboard.Numeric, Style = (Style)Application.Current!.Resources["FilledEntry"] };

        var refreshCaptcha = new Button { Text = "?", WidthRequest = 44 };
        refreshCaptcha.Clicked += (_, __) => GenerateCaptcha();

        // Build captcha grid explicitly and place children using Grid.Add
        var captchaGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            }
        };
        captchaGrid.Add(_captchaLabel, 0, 0);
        captchaGrid.Add(refreshCaptcha, 1, 0);
        captchaGrid.Add(_captchaEntry, 2, 0);

        var registerButton = new Button { Text = "Register", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        registerButton.Clicked += OnRegisterClicked;

        // ENTER submits form from any field
        _usernameEntry.Completed += OnAnyEntryCompleted;
        _emailEntry.Completed += OnAnyEntryCompleted;
        _firstNameEntry.Completed += OnAnyEntryCompleted;
        _lastNameEntry.Completed += OnAnyEntryCompleted;
        _passwordEntry.Completed += OnAnyEntryCompleted;
        _confirmEntry.Completed += OnAnyEntryCompleted;
        _captchaEntry.Completed += OnAnyEntryCompleted;

        var formStack = new VerticalStackLayout
        {
            Padding = new Thickness(24, 40, 24, 24),
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center,
            MaximumWidthRequest = FormMaxWidth,
            Children =
            {
                new Label { Text = "Create Account", FontAttributes = FontAttributes.Bold, FontSize = 24, HorizontalTextAlignment = TextAlignment.Center },
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
                captchaGrid,
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

    private void OnAnyEntryCompleted(object? sender, EventArgs e)
    {
        OnRegisterClicked(sender!, e);
    }

    private void GenerateCaptcha()
    {
        var rnd = Random.Shared;
        var a = rnd.Next(10, 50);
        var b = rnd.Next(1, 9);
        _captchaAnswer = a + b;
        _captchaLabel.Text = $"What is {a} + {b}?";
        _captchaEntry.Text = string.Empty;
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
        if (!int.TryParse(_captchaEntry.Text, out var ans) || ans != _captchaAnswer)
        {
            await DisplayAlert("Register", "Captcha answer incorrect", "OK");
            GenerateCaptcha();
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
            GenerateCaptcha();
        }
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        // Navigate to dashboard route with username as query parameter and reset nav stack (hides TabBar)
        await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
    }
}
