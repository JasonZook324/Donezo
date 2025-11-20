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

    private ObservableCollection<ListRecord> _ownedListsObs = new();
    private ObservableCollection<SharedListRecord> _sharedListsObs = new();

    private CollectionView _ownedListsView = null!;
    private CollectionView _sharedListsView = null!;

    private Entry _newListEntry = null!;
    private CheckBox _dailyCheck = null!;
    private Button _createButton = null!;
    private Button _cancelCreateButton = null!;
    private Button _deleteButton = null!;
    private Button _newListToggleButton = null!;
    private Entry _shareCodeEntry = null!;
    private Button _redeemButton = null!;
    private Label _sharedEmptyLabel = null!;
    private Label _selectionStatusLabel = null!;

    private int? _selectedListId;
    private bool _selectedIsShared;

    private IReadOnlyList<ListRecord> _ownedLists = Array.Empty<ListRecord>();
    private IReadOnlyList<SharedListRecord> _sharedLists = Array.Empty<SharedListRecord>();

    private readonly Dictionary<int, Border> _ownedBorders = new();
    private readonly Dictionary<int, Border> _sharedBorders = new();

    private Grid _overlayRoot = null!;
    private Border _newListModal = null!;

    // Tag properties for accent & selected icon
    static readonly BindableProperty _accentTagProperty = BindableProperty.Create("AccentRef", typeof(BoxView), typeof(ManageListsPage), null);
    static readonly BindableProperty _selectedIconTagProperty = BindableProperty.Create("SelectedIconRef", typeof(Label), typeof(ManageListsPage), null);

    // Share management fields
    private Grid _shareOverlay = null!; // overlay root for share management
    private Border _shareModal = null!; // modal content
    private int? _shareListId; // currently managed list id
    private ObservableCollection<ShareCodeRecord> _shareCodesObs = new();
    private ObservableCollection<MembershipRecord> _membersObs = new();
    private CollectionView _shareCodesView = null!;
    private CollectionView _membersView = null!;
    private Picker _newCodeRolePicker = null!;
    private Entry _newCodeMaxRedeemsEntry = null!;
    private Entry _newCodeExpireDaysEntry = null!;
    private Button _generateCodeButton = null!;

    public ManageListsPage() : this(ServiceHelper.GetRequiredService<INeonDbService>()) { }
    public ManageListsPage(INeonDbService db)
    {
        _db = db;
        Shell.SetNavBarIsVisible(this, false);
        BuildUi();
        _ = InitializeAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("username", out var val) && val is string name && !string.IsNullOrWhiteSpace(name))
        {
            _username = name; _dualHeader.Username = name; if (_userId == null) _ = InitializeAsync();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_username))
            {
                _username = await SecureStorage.GetAsync("AUTH_USERNAME") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(_username)) _dualHeader.Username = _username;
            }
            if (!string.IsNullOrWhiteSpace(_username))
            {
                _userId = await _db.GetUserIdAsync(_username);
                var dark = await _db.GetUserThemeDarkAsync(_userId!.Value);
                _dualHeader.SetTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark), suppressEvent: true);
                ApplyTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark));
                await RefreshListsAsync();
            }
        }
        catch { }
    }

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        var ownedRaw = await _db.GetOwnedListsAsync(_userId.Value);
        var actuallyOwned = new List<ListRecord>();
        foreach (var lr in ownedRaw)
        {
            try { var owner = await _db.GetListOwnerUserIdAsync(lr.Id); if (owner == _userId) actuallyOwned.Add(lr); } catch { }
        }
        _ownedLists = actuallyOwned;
        _sharedLists = await _db.GetSharedListsAsync(_userId.Value);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _ownedListsObs = new ObservableCollection<ListRecord>(_ownedLists);
            _sharedListsObs = new ObservableCollection<SharedListRecord>(_sharedLists);
            _ownedListsView.ItemsSource = _ownedListsObs;
            _sharedListsView.ItemsSource = _sharedListsObs;

            if (_selectedListId != null)
            {
                bool stillOwned = !_selectedIsShared && _ownedLists.Any(o => o.Id == _selectedListId);
                bool stillShared = _selectedIsShared && _sharedLists.Any(s => s.Id == _selectedListId);
                if (!stillOwned && !stillShared) ClearSelection();
            }
            _sharedEmptyLabel.IsVisible = _sharedLists.Count == 0;
            UpdateSelectionVisuals();
        });
    }

    private void BuildUi()
    {
        _dualHeader = new DualHeaderView { TitleText = "Manage Lists", Username = string.Empty };
        _dualHeader.ThemeToggled += async (_, dark) => await OnThemeToggledAsync(dark);
        _dualHeader.LogoutRequested += async (_, __) => await LogoutAsync();
        _dualHeader.DashboardRequested += async (_, __) => { try { await Shell.Current.GoToAsync($"//dashboard?username={Uri.EscapeDataString(_username)}"); } catch { } };
        _dualHeader.ManageListsRequested += (_, __) => { };
        _dualHeader.SetTheme(Application.Current!.RequestedTheme == AppTheme.Dark, suppressEvent: true);

        _shareCodeEntry = new Entry { Placeholder = "Redeem share code", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _shareCodeEntry.TextChanged += (_, __) => _redeemButton.IsEnabled = !string.IsNullOrWhiteSpace(_shareCodeEntry.Text);
        _redeemButton = new Button { Text = "Redeem", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _redeemButton.Clicked += async (_, __) => await RedeemShareCodeAsync();
        _sharedEmptyLabel = new Label { Text = "No shared lists yet.", FontSize = 12, TextColor = Colors.Gray, IsVisible = false };
        _selectionStatusLabel = new Label { Text = "No list selected", FontSize = 12, TextColor = Colors.Gray };

        _ownedListsView = new CollectionView
        {
            ItemsSource = _ownedListsObs,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() => CreateListTemplate(false))
        };
        _sharedListsView = new CollectionView
        {
            ItemsSource = _sharedListsObs,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() => CreateListTemplate(true))
        };

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
                    _selectionStatusLabel,
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

        _overlayRoot = new Grid { IsVisible = false, BackgroundColor = Colors.Black.WithAlpha(0.45f) };
        var modalContent = BuildNewListModal();
        _overlayRoot.Children.Add(new Grid { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { modalContent } });
        var backdropTap = new TapGestureRecognizer(); backdropTap.Tapped += (_, __) => HideNewListOverlay(); _overlayRoot.GestureRecognizers.Add(backdropTap);
        root.Children.Add(_overlayRoot);

        // After existing overlay (_overlayRoot) creation, add share overlay
        _shareOverlay = new Grid { IsVisible = false, BackgroundColor = Colors.Black.WithAlpha(0.45f) };
        _shareModal = BuildShareModal();
        var shareBackdropTap = new TapGestureRecognizer(); shareBackdropTap.Tapped += (_, __) => HideShareOverlay(); _shareOverlay.GestureRecognizers.Add(shareBackdropTap);
        _shareOverlay.Children.Add(new Grid { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { _shareModal } });
        root.Children.Add(_shareOverlay); // root already defined earlier in method

        Content = root;
    }

    private Border BuildShareModal()
    {
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
                var revokeBtn = new Button { Text = "Revoke", FontSize = 11, Padding = new Thickness(6,2), BackgroundColor = Colors.Transparent, TextColor = Colors.Red };
                revokeBtn.Clicked += async (s,e) =>
                {
                    if ((revokeBtn.BindingContext as ShareCodeRecord) is ShareCodeRecord rec)
                    { try { await _db.SoftDeleteShareCodeAsync(rec.Id); await RefreshShareAsync(); } catch { } }
                };
                rolePicker.SelectedIndexChanged += async (s,e) =>
                {
                    if (rolePicker.BindingContext is ShareCodeRecord rec && rolePicker.SelectedItem is string newRole && newRole != rec.Role)
                    { try { await _db.UpdateShareCodeRoleAsync(rec.Id, newRole); await RefreshShareAsync(); } catch { } }
                };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(6,4) };
                grid.Add(codeLbl,0,0);
                grid.Add(rolePicker,1,0);
                grid.Add(countsLbl,2,0);
                grid.Add(revokeBtn,3,0);
                grid.BindingContextChanged += (_,__) =>
                {
                    if (grid.BindingContext is ShareCodeRecord rec)
                    {
                        // Show revoked styling
                        if (rec.IsDeleted)
                        {
                            grid.Opacity = 0.4; revokeBtn.IsEnabled = false; rolePicker.IsEnabled = false;
                        }
                        else
                        {
                            grid.Opacity = 1; revokeBtn.IsEnabled = true; rolePicker.IsEnabled = true;
                        }
                    }
                };
                return new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = 0, Content = grid, Margin = new Thickness(0,4) };
            })
        };

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
                    if (_shareListId != null && ownerBtn.BindingContext is MembershipRecord mem && mem.Role != "Owner")
                    {
                        bool confirm = await DisplayAlert("Transfer Ownership", $"Make {mem.Username} the owner?", "Yes", "Cancel");
                        if (!confirm) return;
                        try { await _db.TransferOwnershipAsync(_shareListId.Value, mem.UserId); await RefreshShareAsync(); await RefreshListsAsync(); } catch { }
                    }
                };
                var row = new HorizontalStackLayout { Spacing = 10, Children = { userLbl, roleLbl, ownerBtn }, Padding = new Thickness(6,4) };
                row.BindingContextChanged += (_,__) =>
                {
                    if (row.BindingContext is MembershipRecord mem)
                    {
                        ownerBtn.IsVisible = mem.Role != "Owner" && !mem.Revoked;
                    }
                };
                return new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = 0, Content = row, Margin = new Thickness(0,4) };
            })
        };

        _newCodeRolePicker = new Picker { Title = "Role", ItemsSource = new[] { "Viewer", "Contributor" }, SelectedIndex = 0, WidthRequest = 140 };
        _newCodeMaxRedeemsEntry = new Entry { Placeholder = "Max redeems (0=unlimited)", Keyboard = Keyboard.Numeric, WidthRequest = 180 };
        _newCodeExpireDaysEntry = new Entry { Placeholder = "Expires days (blank=none)", Keyboard = Keyboard.Numeric, WidthRequest = 180 };
        _generateCodeButton = new Button { Text = "Generate Code", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _generateCodeButton.Clicked += async (s,e) => await GenerateShareCodeAsync();

        var newCodeRow = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { _newCodeRolePicker, _newCodeMaxRedeemsEntry, _newCodeExpireDaysEntry } },
                _generateCodeButton
            }
        };

        var modal = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) },
            Padding = new Thickness(24,26),
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                WidthRequest = 560,
                Children =
                {
                    new HorizontalStackLayout { Spacing = 12, Children = { new Label { Text = "Manage Sharing", FontAttributes = FontAttributes.Bold, FontSize = 22, HorizontalOptions = LayoutOptions.StartAndExpand }, new Button { Text = "Close", Style = (Style)Application.Current!.Resources["OutlinedButton"], HorizontalOptions = LayoutOptions.End, Command = new Command(() => HideShareOverlay()) } } },
                    new Label { Text = "Share Codes", FontAttributes = FontAttributes.Bold },
                    _shareCodesView,
                    new Label { Text = "Create New Code", FontAttributes = FontAttributes.Bold },
                    newCodeRow,
                    new Label { Text = "Members", FontAttributes = FontAttributes.Bold },
                    _membersView
                }
            }
        };
        if (Application.Current!.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) modal.Style = style;
        return modal;
    }

    private async Task GenerateShareCodeAsync()
    {
        if (_shareListId == null) return;
        var role = _newCodeRolePicker.SelectedItem as string ?? "Viewer";
        int maxRedeems = 0; if (int.TryParse(_newCodeMaxRedeemsEntry.Text?.Trim(), out var mr) && mr >= 0) maxRedeems = mr;
        DateTime? expiration = null; if (int.TryParse(_newCodeExpireDaysEntry.Text?.Trim(), out var days) && days > 0) expiration = DateTime.UtcNow.AddDays(days);
        try { await _db.CreateShareCodeAsync(_shareListId.Value, role, expiration, maxRedeems); await RefreshShareAsync(); _newCodeMaxRedeemsEntry.Text = string.Empty; _newCodeExpireDaysEntry.Text = string.Empty; } catch { }
    }

    private async Task RefreshShareAsync()
    {
        if (_shareListId == null) return;
        try
        {
            var codes = await _db.GetShareCodesAsync(_shareListId.Value);
            var members = await _db.GetMembershipsAsync(_shareListId.Value);
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

    private void ShowShareOverlay(int listId)
    {
        _shareListId = listId;
        _shareOverlay.IsVisible = true;
        _shareModal.Opacity = 0; _shareModal.Scale = 0.92;
        _ = _shareModal.FadeTo(1,160,Easing.CubicOut);
        _ = _shareModal.ScaleTo(1,160,Easing.CubicOut);
        _ = RefreshShareAsync();
    }

    private void HideShareOverlay()
    {
        if (!_shareOverlay.IsVisible) return;
        _ = _shareModal.FadeTo(0,120,Easing.CubicOut);
        _ = _shareModal.ScaleTo(0.94,120,Easing.CubicOut);
        Device.StartTimer(TimeSpan.FromMilliseconds(130), () => { _shareOverlay.IsVisible = false; return false; });
    }

    private Border CreateListTemplate(bool isShared)
    {
        var accent = new BoxView { WidthRequest = 4, BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Start };
        var name = new Label { FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };
        var role = new Label { FontSize = 12, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center, IsVisible = isShared };
        var daily = new Border { BackgroundColor = (Color)Application.Current!.Resources["Primary"], StrokeThickness = 0, Padding = new Thickness(6,2), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Content = new Label { Text = "Daily", FontSize = 12, TextColor = Colors.White } };
        daily.SetBinding(IsVisibleProperty, nameof(ListRecord.IsDaily));
        var selectedIcon = new Label { Text = "?", FontSize = 14, TextColor = (Color)Application.Current!.Resources["Primary"], IsVisible = false, VerticalTextAlignment = TextAlignment.Center };
        var primaryColor = (Color)Application.Current!.Resources["Primary"];
        // Build a reliable share control: vector chain + text
        var chainIcon = new Grid { WidthRequest = 18, HeightRequest = 14, VerticalOptions = LayoutOptions.Center };
        var chainLeft = new Border { WidthRequest = 8, HeightRequest = 8, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }, StrokeThickness = 1.2, Stroke = primaryColor, TranslationX = 0, TranslationY = 3 };
        var chainRight = new Border { WidthRequest = 8, HeightRequest = 8, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }, StrokeThickness = 1.2, Stroke = primaryColor, TranslationX = 10, TranslationY = 3 };
        var chainConnector = new BoxView { WidthRequest = 6, HeightRequest = 2, Color = primaryColor, TranslationX = 6, TranslationY = 6 };
        chainIcon.Children.Add(chainLeft); chainIcon.Children.Add(chainRight); chainIcon.Children.Add(chainConnector);
        var shareText = new Label { Text = "Share", FontSize = 12, TextColor = primaryColor, VerticalTextAlignment = TextAlignment.Center };
        var shareWrap = new HorizontalStackLayout { Spacing = 4, IsVisible = !isShared, VerticalOptions = LayoutOptions.Center, Children = { chainIcon, shareText } };
        AutomationProperties.SetName(shareWrap, "Manage Shares");
        var shareTap = new TapGestureRecognizer();
        shareTap.Tapped += (s,e) =>
        {
            if (!isShared && (shareWrap.BindingContext is ListRecord lr)) ShowShareOverlay(lr.Id);
        };
        shareWrap.GestureRecognizers.Add(shareTap);

        if (!isShared) name.SetBinding(Label.TextProperty, nameof(ListRecord.Name));
        else { name.SetBinding(Label.TextProperty, nameof(SharedListRecord.Name)); role.SetBinding(Label.TextProperty, nameof(SharedListRecord.Role)); }

        var contentStack = new HorizontalStackLayout { Spacing = 8, Children = { accent, name, role, daily, shareWrap, selectedIcon } };
        var border = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 4),
            Content = contentStack
        };

        border.SetValue(_accentTagProperty, accent);
        border.SetValue(_selectedIconTagProperty, selectedIcon);
        border.BindingContextChanged += (_, __) => RegisterBorder(border, isShared);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, __) =>
        {
            if (border.BindingContext is ListRecord lr) SelectOwned(lr.Id, border);
            else if (border.BindingContext is SharedListRecord sl) SelectShared(sl.Id, border);
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void RegisterBorder(Border border, bool isShared)
    {
        foreach (var kv in _ownedBorders.Where(kv => kv.Value == border).Select(kv => kv.Key).ToList()) _ownedBorders.Remove(kv);
        foreach (var kv in _sharedBorders.Where(kv => kv.Value == border).Select(kv => kv.Key).ToList()) _sharedBorders.Remove(kv);
        if (border.BindingContext is ListRecord lr) _ownedBorders[lr.Id] = border;
        else if (border.BindingContext is SharedListRecord sl) _sharedBorders[sl.Id] = border;
        ApplyVisual(border);
    }

    private void SelectOwned(int id, Border? tappedBorder = null)
    {
        _selectedListId = id; _selectedIsShared = false;
        UpdateSelectionVisuals(tappedBorder);
    }
    private void SelectShared(int id, Border? tappedBorder = null)
    {
        _selectedListId = id; _selectedIsShared = true;
        UpdateSelectionVisuals(tappedBorder);
    }
    private void ClearSelection() { _selectedListId = null; _selectedIsShared = false; UpdateSelectionVisuals(); }

    private void UpdateSelectionVisuals(Border? tappedBorder = null)
    {
        // Immediate visual for tapped border (if provided) before iterating others to improve perceived responsiveness.
        if (tappedBorder != null)
        {
            ApplyVisual(tappedBorder);
        }
        foreach (var b in _ownedBorders.Values)
        {
            if (b == tappedBorder) continue; // already applied
            ApplyVisual(b);
        }
        foreach (var b in _sharedBorders.Values)
        {
            if (b == tappedBorder) continue;
            ApplyVisual(b);
        }
        _deleteButton.IsEnabled = _selectedListId != null && !_selectedIsShared && _ownedLists.Any(o => o.Id == _selectedListId);
        if (_selectedListId == null) _selectionStatusLabel.Text = "No list selected";
        else if (!_selectedIsShared)
        { var lr = _ownedLists.FirstOrDefault(o => o.Id == _selectedListId); _selectionStatusLabel.Text = lr != null ? $"Selected (Owned): {lr.Name}" : "No list selected"; }
        else
        { var sl = _sharedLists.FirstOrDefault(o => o.Id == _selectedListId); _selectionStatusLabel.Text = sl != null ? $"Selected (Shared): {sl.Name}" : "No list selected"; }
    }

    private void ApplyVisual(Border b)
    {
        var dark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var primary = (Color)Application.Current!.Resources["Primary"];
        var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
        var strokeNormal = (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
        bool selected = false;
        if (_selectedListId != null)
        {
            if (!_selectedIsShared && b.BindingContext is ListRecord lr && lr.Id == _selectedListId) selected = true;
            if (_selectedIsShared && b.BindingContext is SharedListRecord sl && sl.Id == _selectedListId) selected = true;
        }
        if (selected)
        {
            // Softer tint (much lower alpha) and slightly darker stroke, no shadow or scale
            var tintAlpha = dark ? 0.18f : 0.12f;
            b.BackgroundColor = primary.WithAlpha(tintAlpha);
            b.Stroke = primary.WithAlpha(0.85f);
            b.StrokeThickness = 1.5;
            b.Scale = 1.0; // remove pop effect
            b.Shadow = null; // remove heavy shadow
        }
        else
        {
            b.BackgroundColor = baseBg;
            b.Stroke = strokeNormal;
            b.StrokeThickness = 1;
            b.Scale = 1.0;
            b.Shadow = null;
        }
        var accent = (BoxView)b.GetValue(_accentTagProperty);
        if (accent != null)
        {
            accent.BackgroundColor = selected ? primary : Colors.Transparent;
            accent.WidthRequest = selected ? 4 : 4; // keep consistent thinner accent
        }
        var icon = (Label)b.GetValue(_selectedIconTagProperty);
        if (icon != null)
        {
            icon.IsVisible = selected;
            if (selected)
                icon.TextColor = dark ? Colors.White : primary.WithAlpha(0.9f);
        }
        if (b.Content is Layout layout2)
        {
            foreach (var lbl in layout2.Children.OfType<Label>())
            {
                if (lbl.FontAttributes.HasFlag(FontAttributes.Bold))
                    lbl.TextColor = selected ? (dark ? Colors.White : primary.WithAlpha(0.9f)) : (Color)Application.Current!.Resources[dark ? "Gray100" : "Black"];
            }
        }
    }

    private Border BuildNewListModal()
    {
        _newListEntry = new Entry { Placeholder = "List name", Style = (Style)Application.Current!.Resources["FilledEntry"], WidthRequest = 260 };
        _dailyCheck = new CheckBox { VerticalOptions = LayoutOptions.Center };
        _createButton = new Button { Text = "Create", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _createButton.Clicked += async (_, __) => await CreateListAsync();
        _cancelCreateButton = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _cancelCreateButton.Clicked += (_, __) => HideNewListOverlay(clear: true);
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

    private async Task CreateListAsync()
    { if (_userId == null) return; var name = _newListEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked); HideNewListOverlay(clear: true); ClearSelection(); await RefreshListsAsync(); }

    private async Task DeleteSelectedAsync()
    { if (_selectedListId == null) return; if (_selectedIsShared || !_ownedLists.Any(o => o.Id == _selectedListId)) { await DisplayAlert("Delete", "Only owners can delete lists.", "OK"); return; } var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel"); if (!confirm) return; if (await _db.DeleteListAsync(_selectedListId.Value)) { ClearSelection(); await RefreshListsAsync(); } }

    private async Task RedeemShareCodeAsync()
    { var code = _shareCodeEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(code) || _userId == null) return; var (ok, listId, list, membership) = await _db.RedeemShareCodeByCodeAsync(_userId.Value, code); if (!ok || listId == null || list == null || membership == null) { await DisplayAlert("Redeem", "Invalid or unusable code.", "OK"); return; } _shareCodeEntry.Text = string.Empty; _selectedListId = list.Id; _selectedIsShared = true; UpdateSelectionVisuals(); await RefreshListsAsync(); }

    private async Task OnThemeToggledAsync(bool dark)
    { ApplyTheme(dark); if (_userId != null) { try { await _db.SetUserThemeDarkAsync(_userId.Value, dark); } catch { } } }

    private void ApplyTheme(bool dark)
    { if (Application.Current is App app) app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light; _dualHeader?.SyncFromAppTheme(); }

    private async Task LogoutAsync()
    { try { SecureStorage.Remove("AUTH_USERNAME"); } catch { } _username = string.Empty; _dualHeader.Username = string.Empty; try { await Shell.Current.GoToAsync("//login"); } catch { } }
}
