using Donezo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Linq;
using Donezo.Pages.Components;

namespace Donezo.Pages;

public class ManageListsPage : ContentPage, IQueryAttributable
{
    private readonly INeonDbService _db;
    private string _username = string.Empty;
    private int? _userId;
    private DualHeaderView _dualHeader = null!;

    // Collections (no longer readonly so we can replace atomically to avoid modification during enumeration crashes)
    private ObservableCollection<ListRecord> _ownedListsObs = new();
    private ObservableCollection<SharedListRecord> _sharedListsObs = new();

    private CollectionView _ownedListsView = null!;
    private CollectionView _sharedListsView = null!;

    private Entry _newListEntry = null!;
    private CheckBox _dailyCheck = null!;
    private Button _createButton = null!;
    private Button _cancelCreateButton = null!;
    private Button _deleteButton = null!;
    private Button _newListToggleButton = null!; // opens overlay
    private Entry _shareCodeEntry = null!;
    private Button _redeemButton = null!;
    private Label _sharedEmptyLabel = null!;

    private int? _selectedListId;
    private IReadOnlyList<ListRecord> _ownedLists = Array.Empty<ListRecord>();
    private IReadOnlyList<SharedListRecord> _sharedLists = Array.Empty<SharedListRecord>();

    // Overlay modal elements
    private Grid _overlayRoot = null!;
    private Border _newListModal = null!;

    public ManageListsPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }
    public ManageListsPage(INeonDbService db)
    {
        _db = db;
        Shell.SetNavBarIsVisible(this, false);
        Title = string.Empty;
        BuildUi();
        _ = InitializeAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    { if (query.TryGetValue("username", out var val) && val is string name && !string.IsNullOrWhiteSpace(name)) { _username = name; _dualHeader.Username = name; if (_userId == null) _ = InitializeAsync(); } }

    private async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_username)) { _username = await SecureStorage.GetAsync("AUTH_USERNAME") ?? string.Empty; if (!string.IsNullOrWhiteSpace(_username)) _dualHeader.Username = _username; }
            if (!string.IsNullOrWhiteSpace(_username)) { _userId = await _db.GetUserIdAsync(_username); var dark = await _db.GetUserThemeDarkAsync(_userId!.Value); _dualHeader.SetTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark), suppressEvent:true); ApplyTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark)); await RefreshListsAsync(); }
        }
        catch { }
    }

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        var ownedRaw = await _db.GetOwnedListsAsync(_userId.Value);
        var actuallyOwned = new List<ListRecord>();
        foreach (var lr in ownedRaw) { try { var owner = await _db.GetListOwnerUserIdAsync(lr.Id); if (owner == _userId) actuallyOwned.Add(lr); } catch { } }
        _ownedLists = actuallyOwned;
        _sharedLists = await _db.GetSharedListsAsync(_userId.Value);

        // Replace entire observable collections on UI thread to avoid mid-enumeration mutation crash
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _ownedListsObs = new ObservableCollection<ListRecord>(_ownedLists);
            _sharedListsObs = new ObservableCollection<SharedListRecord>(_sharedLists);
            _ownedListsView.ItemsSource = _ownedListsObs;
            _sharedListsView.ItemsSource = _sharedListsObs;
            var allIds = _ownedLists.Select(x => x.Id).Concat(_sharedLists.Select(x => x.Id)).ToHashSet();
            if (_selectedListId == null || !allIds.Contains(_selectedListId.Value))
                _selectedListId = _ownedLists.FirstOrDefault()?.Id ?? _sharedLists.FirstOrDefault()?.Id;
            UpdateSelectionVisuals();
            _sharedEmptyLabel.IsVisible = _sharedLists.Count == 0;
        });
    }

    private void BuildUi()
    {
        _dualHeader = new DualHeaderView { TitleText = "Manage Lists", Username = string.Empty };
        _dualHeader.ThemeToggled += async (_, dark) => await OnThemeToggledAsync(dark);
        _dualHeader.LogoutRequested += async (_, __) => await LogoutAsync();
        _dualHeader.DashboardRequested += async (_, __) => { try { await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(_username)}"); } catch { } };
        _dualHeader.ManageListsRequested += (_, __) => { };
        _dualHeader.SetTheme(Application.Current!.RequestedTheme == AppTheme.Dark, suppressEvent:true);

        _shareCodeEntry = new Entry { Placeholder = "Redeem share code", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _shareCodeEntry.TextChanged += (_, __) => _redeemButton.IsEnabled = !string.IsNullOrWhiteSpace(_shareCodeEntry.Text);
        _redeemButton = new Button { Text = "Redeem", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _redeemButton.Clicked += async (_, __) => await RedeemShareCodeAsync();
        _sharedEmptyLabel = new Label { Text = "No shared lists yet.", FontSize = 12, TextColor = Colors.Gray, IsVisible = false };

        _ownedListsView = new CollectionView { ItemsSource = _ownedListsObs, SelectionMode = SelectionMode.Single, ItemTemplate = new DataTemplate(() => CreateListTemplate(false)) };
        _sharedListsView = new CollectionView { ItemsSource = _sharedListsObs, SelectionMode = SelectionMode.Single, ItemTemplate = new DataTemplate(() => CreateListTemplate(true)) };
        _ownedListsView.SelectionChanged += (_, e) => { _selectedListId = (e.CurrentSelection.FirstOrDefault() as ListRecord)?.Id; UpdateSelectionVisuals(); };
        _sharedListsView.SelectionChanged += (_, e) => { _selectedListId = (e.CurrentSelection.FirstOrDefault() as SharedListRecord)?.Id; UpdateSelectionVisuals(); };

        _newListToggleButton = new Button { Text = "+ New List", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _newListToggleButton.Clicked += (_, __) => ShowNewListOverlay();

        _deleteButton = new Button { Text = "Delete Selected", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red };
        _deleteButton.Clicked += async (_, __) => await DeleteSelectedAsync();

        var redeemRow = new HorizontalStackLayout { Spacing = 8, Children = { _shareCodeEntry, _redeemButton } };
        var actionsRow = new HorizontalStackLayout { Spacing = 12, Children = { _newListToggleButton, _deleteButton } };

        var listsCard = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Padding = 20,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label { Text = "Owned Lists", Style = (Style)Application.Current!.Resources["SectionTitle"] },
                    _ownedListsView,
                    new Label { Text = "Shared With Me", Style = (Style)Application.Current!.Resources["SectionSubTitle"] },
                    _sharedEmptyLabel,
                    _sharedListsView,
                    actionsRow,
                    new Label { Text = "Redeem Code", FontAttributes = FontAttributes.Bold },
                    redeemRow
                }
            }
        };
        if (Application.Current!.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) listsCard.Style = style;

        var scroll = new ScrollView { Content = listsCard };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(_dualHeader, 0, 0); root.Add(scroll, 0, 1);

        // Build overlay modal (hidden by default)
        _overlayRoot = new Grid { IsVisible = false, BackgroundColor = Colors.Black.WithAlpha(0.45f), InputTransparent = false };
        var modalContent = BuildNewListModal();
        _overlayRoot.Children.Add(new Grid { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { modalContent } });
        var backdropTap = new TapGestureRecognizer(); backdropTap.Tapped += (_, __) => HideNewListOverlay(); _overlayRoot.GestureRecognizers.Add(backdropTap);
        root.Children.Add(_overlayRoot);
        Content = root;
    }

    private Border BuildNewListModal()
    {
        _newListEntry = new Entry { Placeholder = "List name", Style = (Style)Application.Current!.Resources["FilledEntry"], WidthRequest = 260 };
        _dailyCheck = new CheckBox { VerticalOptions = LayoutOptions.Center };
        _createButton = new Button { Text = "Create", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _createButton.Clicked += async (_, __) => await CreateListAsync();
        _cancelCreateButton = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _cancelCreateButton.Clicked += (_, __) => HideNewListOverlay(clear:true);
        var dailyRow = new HorizontalStackLayout { Spacing = 6, Children = { new Label { Text = "Daily" }, _dailyCheck } };
        _newListModal = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24) },
            Padding = new Thickness(24, 28),
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                WidthRequest = 300,
                Children =
                {
                    new Label { Text = "Create New List", FontAttributes = FontAttributes.Bold, FontSize = 20, HorizontalTextAlignment = TextAlignment.Center },
                    _newListEntry,
                    dailyRow,
                    new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Center, Children = { _createButton, _cancelCreateButton } }
                }
            }
        };
        if (Application.Current!.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) _newListModal.Style = style;
        return _newListModal;
    }

    private void ShowNewListOverlay()
    {
        _overlayRoot.IsVisible = true;
        _newListEntry.Text = string.Empty; _dailyCheck.IsChecked = false;
        _newListModal.Opacity = 0; _newListModal.Scale = 0.90;
        _ = _newListModal.FadeTo(1, 160, Easing.CubicOut);
        _ = _newListModal.ScaleTo(1, 160, Easing.CubicOut);
        Device.BeginInvokeOnMainThread(() => _newListEntry.Focus());
    }

    private void HideNewListOverlay(bool clear = false)
    {
        if (clear) { _newListEntry.Text = string.Empty; _dailyCheck.IsChecked = false; }
        if (!_overlayRoot.IsVisible) return;
        _ = _newListModal.FadeTo(0, 120, Easing.CubicOut);
        _ = _newListModal.ScaleTo(0.92, 120, Easing.CubicOut);
        Device.StartTimer(TimeSpan.FromMilliseconds(130), () => { _overlayRoot.IsVisible = false; return false; });
    }

    private Border CreateListTemplate(bool isShared)
    {
        var border = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) }, Padding = new Thickness(10,6), Margin = new Thickness(0,4) };
        var name = new Label { FontAttributes = FontAttributes.Bold };
        var role = new Label { FontSize = 12, TextColor = Colors.Gray, IsVisible = false };
        var daily = new Border { BackgroundColor = (Color)Application.Current!.Resources["Primary"], StrokeThickness = 0, Padding = new Thickness(6,2), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Content = new Label { Text = "Daily", FontSize = 12, TextColor = Colors.White } };
        daily.SetBinding(IsVisibleProperty, nameof(ListRecord.IsDaily));
        border.BindingContextChanged += (_, __) =>
        {
            if (border.BindingContext is ListRecord lr) { name.Text = lr.Name; role.IsVisible = false; }
            else if (border.BindingContext is SharedListRecord sl) { name.Text = sl.Name; role.Text = sl.Role; role.IsVisible = true; }
            ApplyListBorderVisual(border);
        };
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, __) => { if (isShared) _selectedListId = (border.BindingContext as SharedListRecord)?.Id; else _selectedListId = (border.BindingContext as ListRecord)?.Id; UpdateSelectionVisuals(); };
        border.GestureRecognizers.Add(tap);
        border.Content = new HorizontalStackLayout { Spacing = 8, Children = { name, role, daily } };
        return border;
    }

    private void ApplyListBorderVisual(Border b)
    {
        var primary = (Color)Application.Current!.Resources["Primary"]; var dark = Application.Current!.RequestedTheme == AppTheme.Dark; var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"]; bool selected = false; if (b.BindingContext is ListRecord lr) selected = _selectedListId == lr.Id; if (b.BindingContext is SharedListRecord sl) selected = _selectedListId == sl.Id; b.BackgroundColor = selected ? primary.WithAlpha(0.12f) : baseBg; b.Stroke = selected ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"]; }

    private void UpdateSelectionVisuals()
    { _deleteButton.IsEnabled = _selectedListId != null && _ownedLists.Any(o => o.Id == _selectedListId); }

    private async Task CreateListAsync()
    { if (_userId == null) return; var name = _newListEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked); HideNewListOverlay(clear:true); await RefreshListsAsync(); }

    private async Task DeleteSelectedAsync()
    { if (_selectedListId == null) return; if (!_ownedLists.Any(o => o.Id == _selectedListId)) { await DisplayAlert("Delete", "Only owners can delete lists.", "OK"); return; } var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel"); if (!confirm) return; if (await _db.DeleteListAsync(_selectedListId.Value)) { await RefreshListsAsync(); } }

    private async Task RedeemShareCodeAsync()
    { var code = _shareCodeEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(code) || _userId == null) return; var (ok, listId, list, membership) = await _db.RedeemShareCodeByCodeAsync(_userId.Value, code); if (!ok || listId == null || list == null || membership == null) { await DisplayAlert("Redeem", "Invalid or unusable code.", "OK"); return; } _shareCodeEntry.Text = string.Empty; _selectedListId = listId; await RefreshListsAsync(); }

    private async Task OnThemeToggledAsync(bool dark)
    { ApplyTheme(dark); if (_userId != null) { try { await _db.SetUserThemeDarkAsync(_userId.Value, dark); } catch { } } }

    private void ApplyTheme(bool dark)
    { if (Application.Current is App app) app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light; _dualHeader?.SyncFromAppTheme(); }

    private async Task LogoutAsync()
    { try { SecureStorage.Remove("AUTH_USERNAME"); } catch { } _username = string.Empty; _dualHeader.Username = string.Empty; try { await Shell.Current.GoToAsync("//login"); } catch { } }
}
