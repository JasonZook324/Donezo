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
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _confirmEntry = new Entry { Placeholder = "confirm password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };

        var registerButton = new Button { Text = "Register", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        registerButton.Clicked += OnRegisterClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 40, 24, 24),
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Create Account", FontAttributes = FontAttributes.Bold, FontSize = 24 },
                    new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                    _usernameEntry,
                    new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                    _passwordEntry,
                    new Label { Text = "Confirm Password", FontAttributes = FontAttributes.Bold },
                    _confirmEntry,
                    registerButton
                }
            }
        };
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var username = _usernameEntry.Text ?? string.Empty;
        var password = _passwordEntry.Text ?? string.Empty;
        var confirm = _confirmEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Register", "Username and password are required", "OK");
            return;
        }
        if (password != confirm)
        {
            await DisplayAlert("Register", "Passwords do not match", "OK");
            return;
        }

        var ok = await _db.RegisterUserAsync(username, password);
        await DisplayAlert("Register", ok ? "Success" : "Failed", "OK");
        if (ok)
            await NavigateToDashboardAsync(username);
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        await Shell.Current.Navigation.PushAsync(new DashboardPage(_db, username));
    }
}
