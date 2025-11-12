using Microsoft.Maui.Controls;
using Donezo.Services;
using Microsoft.Maui.Storage;

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
        Title = "Donezo"; // brand title
        BuildUi();
    }

    private void BuildUi()
    {
        _usernameEntry = new Entry { Placeholder = "username", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _passwordEntry = new Entry { Placeholder = "password", IsPassword = true, Style = (Style)Application.Current!.Resources["FilledEntry"] };

        var registerButton = new Button { Text = "Register", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        registerButton.Clicked += OnRegisterClicked;
        var loginButton = new Button { Text = "Login", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        loginButton.Clicked += OnLoginClicked;

        var logo = new Image
        {
            Source = "donezo_logo_vertical.svg",
            HeightRequest = 180,
            WidthRequest = 180,
            HorizontalOptions = LayoutOptions.Center
        };

        var tagline = new Label
        {
            Text = "From to-do... to Donezo.",
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = (Color)Application.Current!.Resources["Primary"],
            Margin = new Thickness(0, 8, 0, 24)
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 40, 24, 24),
                Spacing = 16,
                Children =
                {
                    logo,
                    tagline,
                    new Label { Text = "Username", FontAttributes = FontAttributes.Bold },
                    _usernameEntry,
                    new Label { Text = "Password", FontAttributes = FontAttributes.Bold },
                    _passwordEntry,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { registerButton, loginButton }
                    }
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
        await SecureStorage.SetAsync("AUTH_USERNAME", username);
        await Shell.Current.Navigation.PushAsync(new DashboardPage(_db, username));
    }
}
