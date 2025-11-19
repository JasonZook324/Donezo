using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
namespace Donezo.Pages;

public class ShareListPage : ContentPage
{
    private readonly Donezo.Services.ListRecord _list;
    public ShareListPage(Donezo.Services.ListRecord list)
    {
        _list = list;
        BackgroundColor = Colors.Black.WithAlpha(0.6f); // overlay dim
        Padding = 0;
        // Outer grid acts as overlay
        var overlay = new Grid
        {
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };

        var card = new Border
        {
            StrokeThickness = 1,
            Stroke = (Color)Application.Current!.Resources["Primary"],
            // Fallback shape if RoundRectangle unavailable
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            Padding = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = BuildContent()
        };

        overlay.Children.Add(card);
        Content = overlay;
    }

    private View BuildContent()
    {
        var title = new Label
        {
            Text = $"Share '{_list.Name}'",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0,0,0,12)
        };
        var info = new Label
        {
            Text = "Provide a way to share this list (implementation placeholder).",
            FontSize = 14,
            TextColor = Colors.Gray,
            Margin = new Thickness(0,0,0,20)
        };

        var closeBtn = new Button
        {
            Text = "Close",
            Style = (Style)Application.Current!.Resources["OutlinedButton"],
            HorizontalOptions = LayoutOptions.End
        };
        closeBtn.Clicked += async (_,__) => await Navigation.PopModalAsync();

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children = { title, info, closeBtn }
        };
    }
}
