using Donezo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System;

namespace Donezo.Pages;

public class ShareOptionsPage : ContentPage, IQueryAttributable
{
    private readonly INeonDbService _db;
    private int _listId;
    private string _username = string.Empty;

    private ObservableCollection<ShareCodeRecord> _shareCodesObs = new();
    private ObservableCollection<MembershipRecord> _membersObs = new();
    private CollectionView _shareCodesView = null!;
    private CollectionView _membersView = null!;
    private Picker _newCodeRolePicker = null!;
    private Entry _newCodeMaxRedeemsEntry = null!;
    private DatePicker _newCodeExpireDatePicker = null!;
    private CheckBox _newCodeHasExpiry = null!;
    private Button _generateCodeButton = null!;

    public ShareOptionsPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }
    public ShareOptionsPage(INeonDbService db)
    {
        _db = db;
        Title = "Manage Sharing";

        // Back toolbar item
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Back",
            Command = new Command(async () =>
            {
                try { await Shell.Current.GoToAsync($"//managelists?username={Uri.EscapeDataString(_username ?? string.Empty)}"); } catch { }
            })
        });

        BuildUi();
    }

    protected override bool OnBackButtonPressed()
    {
        // Handle Android hardware back to go to Manage Lists
        try { Shell.Current.GoToAsync($"//managelists?username={Uri.EscapeDataString(_username ?? string.Empty)}"); } catch { }
        return true; // prevent default back behavior
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("listId", out var lid) && lid is string s && int.TryParse(s, out var id)) _listId = id;
        else if (query.TryGetValue("listId", out var idObj) && idObj is int id2) _listId = id2;
        if (query.TryGetValue("username", out var u) && u is string name) _username = name;
        _ = RefreshShareAsync();
    }

    private void BuildUi()
    {
        // Share codes view
        _shareCodesView = new CollectionView
        {
            ItemsSource = _shareCodesObs,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var codeLbl = new Label { FontAttributes = FontAttributes.Bold };
                codeLbl.SetBinding(Label.TextProperty, nameof(ShareCodeRecord.Code));
                var rolePicker = new Picker { WidthRequest = 110, ItemsSource = new[] { "Viewer", "Contributor" } };
                rolePicker.SetBinding(Picker.SelectedItemProperty, nameof(ShareCodeRecord.Role));
                var countsLbl = new Label { FontSize = 11, TextColor = Colors.Gray };
                countsLbl.SetBinding(Label.TextProperty, new Binding(path: nameof(ShareCodeRecord.RedeemedCount), stringFormat: "Redeemed: {0}") );
                var copyBtn = new Button { Text = "Copy", FontSize = 11, Padding = new Thickness(6,2) };
                copyBtn.Clicked += async (s,e) =>
                {
                    if (copyBtn.BindingContext is ShareCodeRecord rec && !string.IsNullOrWhiteSpace(rec.Code))
                    {
                        try { await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(rec.Code); } catch { }
                    }
                };
                var revokeBtn = new Button { Text = "Revoke", FontSize = 11, Padding = new Thickness(6,2), BackgroundColor = Colors.Transparent, TextColor = Colors.Red };
                revokeBtn.Clicked += async (s,e) =>
                {
                    if (revokeBtn.BindingContext is ShareCodeRecord rec)
                    { try { await _db.SoftDeleteShareCodeAsync(rec.Id); await RefreshShareAsync(); } catch { } }
                };
                rolePicker.SelectedIndexChanged += async (s,e) =>
                {
                    if (rolePicker.BindingContext is ShareCodeRecord rec && rolePicker.SelectedItem is string newRole && newRole != rec.Role)
                    { try { await _db.UpdateShareCodeRoleAsync(rec.Id, newRole); await RefreshShareAsync(); } catch { } }
                };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(6,4) };
                grid.Add(codeLbl,0,0);
                grid.Add(rolePicker,1,0);
                grid.Add(countsLbl,2,0);
                grid.Add(copyBtn,3,0);
                grid.Add(revokeBtn,4,0);
                grid.BindingContextChanged += (_,__) =>
                {
                    if (grid.BindingContext is ShareCodeRecord rec)
                    {
                        if (rec.IsDeleted)
                        { grid.Opacity = 0.4; revokeBtn.IsEnabled = false; rolePicker.IsEnabled = false; copyBtn.IsEnabled = false; }
                        else
                        { grid.Opacity = 1; revokeBtn.IsEnabled = true; rolePicker.IsEnabled = true; copyBtn.IsEnabled = true; }
                    }
                };
                return new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = 0, Content = grid, Margin = new Thickness(0,4) };
            })
        };

        // Members view
        _membersView = new CollectionView
        {
            ItemsSource = _membersObs,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var userLbl = new Label { FontAttributes = FontAttributes.Bold };
                userLbl.SetBinding(Label.TextProperty, nameof(MembershipRecord.Username));
                var roleLbl = new Label { FontSize = 12, TextColor = Colors.Gray };
                roleLbl.SetBinding(Label.TextProperty, nameof(MembershipRecord.Role));
                var ownerBtn = new Button { Text = "Make Owner", FontSize = 11, Padding = new Thickness(6,2) };
                ownerBtn.Clicked += async (s,e) =>
                {
                    if (ownerBtn.BindingContext is MembershipRecord mem && mem.Role != "Owner")
                    {
                        bool confirm = await DisplayAlert("Transfer Ownership", $"Make {mem.Username} the owner?", "Yes", "Cancel");
                        if (!confirm) return;
                        try { await _db.TransferOwnershipAsync(_listId, mem.UserId); await RefreshShareAsync(); } catch { }
                    }
                };
                var row = new HorizontalStackLayout { Spacing = 10, Children = { userLbl, roleLbl, ownerBtn }, Padding = new Thickness(6,4) };
                row.BindingContextChanged += (_,__) =>
                {
                    if (row.BindingContext is MembershipRecord mem)
                        ownerBtn.IsVisible = mem.Role != "Owner" && !mem.Revoked;
                };
                return new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = 0, Content = row, Margin = new Thickness(0,4) };
            })
        };

        // Create new code controls
        _newCodeRolePicker = new Picker { Title = "Role", ItemsSource = new[] { "Viewer", "Contributor" }, SelectedIndex = 0, WidthRequest = 140 };
        _newCodeMaxRedeemsEntry = new Entry { Placeholder = "Max redeems (0=unlimited)", Keyboard = Keyboard.Numeric, WidthRequest = 180 };
        _newCodeHasExpiry = new CheckBox { IsChecked = false, VerticalOptions = LayoutOptions.Center };
        _newCodeExpireDatePicker = new DatePicker { IsEnabled = false, MinimumDate = DateTime.Today, HorizontalOptions = LayoutOptions.Start };
        _newCodeHasExpiry.CheckedChanged += (_, e) => { _newCodeExpireDatePicker.IsEnabled = _newCodeHasExpiry.IsChecked; if (!_newCodeHasExpiry.IsChecked) _newCodeExpireDatePicker.Date = DateTime.Today; };
        _generateCodeButton = new Button { Text = "Generate Code", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _generateCodeButton.Clicked += async (s,e) => await GenerateShareCodeAsync();

        var expiryRow = new HorizontalStackLayout { Spacing = 6, Children = { new Label { Text = "Expires" }, _newCodeHasExpiry, _newCodeExpireDatePicker } };
        var newCodeRow = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { _newCodeRolePicker, _newCodeMaxRedeemsEntry } },
                expiryRow,
                _generateCodeButton
            }
        };

        // Page content
        var root = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 12),
            Children =
            {
                new Label { Text = "Share Codes", FontAttributes = FontAttributes.Bold },
                _shareCodesView,
                new Label { Text = "Create New Code", FontAttributes = FontAttributes.Bold },
                newCodeRow,
                new Label { Text = "Members", FontAttributes = FontAttributes.Bold },
                _membersView
            }
        };
        Content = new ScrollView { Content = root };
    }

    private async Task GenerateShareCodeAsync()
    {
        if (_listId == 0) return;
        var role = _newCodeRolePicker.SelectedItem as string ?? "Viewer";
        int maxRedeems = 0; if (int.TryParse(_newCodeMaxRedeemsEntry.Text?.Trim(), out var mr) && mr >= 0) maxRedeems = mr;
        DateTime? expiration = null;
        if (_newCodeHasExpiry.IsChecked)
        {
            try { var local = DateTime.SpecifyKind(_newCodeExpireDatePicker.Date.Date, DateTimeKind.Local); expiration = local.ToUniversalTime(); } catch { expiration = null; }
        }
        try { await _db.CreateShareCodeAsync(_listId, role, expiration, maxRedeems); await RefreshShareAsync(); _newCodeMaxRedeemsEntry.Text = string.Empty; _newCodeHasExpiry.IsChecked = false; } catch { }
    }

    private async Task RefreshShareAsync()
    {
        if (_listId == 0) return;
        try
        {
            var codes = await _db.GetShareCodesAsync(_listId);
            var members = await _db.GetMembershipsAsync(_listId);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _shareCodesObs = new ObservableCollection<ShareCodeRecord>(codes.OrderByDescending(c => c.Id));
                _membersObs = new ObservableCollection<MembershipRecord>(members.OrderByDescending(m => m.JoinedUtc));
                _shareCodesView.ItemsSource = _shareCodesObs;
                _membersView.ItemsSource = _membersObs;
            });
        }
        catch { }
    }
}
