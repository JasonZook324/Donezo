using Microsoft.Maui.Controls;
using Donezo.Services;

namespace Donezo.Pages;

public class LoginPage : ContentPage
{
    private readonly INeonDbService _db;

    private Entry _usernameEntry;
    private Entry _passwordEntry;

    // Parameterless ctor for XAML/Shell. Resolves service via ServiceHelper.
    public LoginPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }

    public LoginPage(INeonDbService db)
    {
        _db = db;
        BuildUi();
    }

    private void BuildUi()
    {
        _usernameEntry = new Entry { Placeholder = "username" };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true };

        var registerButton = new Button { Text = "Register" };
        registerButton.Clicked += OnRegisterClicked;
        var loginButton = new Button { Text = "Login" };
        loginButton.Clicked += OnLoginClicked;

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = "Username" },
                _usernameEntry,
                new Label { Text = "Password" },
                _passwordEntry,
                new HorizontalStackLayout
                {
                    Spacing = 12,
                    Children = { registerButton, loginButton }
                }
            }
        };
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var ok = await _db.RegisterUserAsync(_usernameEntry.Text ?? string.Empty, _passwordEntry.Text ?? string.Empty);
        await DisplayAlert("Register", ok ? "Success" : "Failed", "OK");
        if (ok)
            await NavigateToDashboardAsync(_usernameEntry.Text!);
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var ok = await _db.AuthenticateUserAsync(_usernameEntry.Text ?? string.Empty, _passwordEntry.Text ?? string.Empty);
        await DisplayAlert("Login", ok ? "Success" : "Failed", "OK");
        if (ok)
            await NavigateToDashboardAsync(_usernameEntry.Text!);
    }

    private async Task NavigateToDashboardAsync(string username)
    {
        await Shell.Current.Navigation.PushAsync(new DashboardPage(_db, username));
    }
}
