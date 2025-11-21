using Donezo.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;
using System.Linq;
using Microsoft.Maui; // CornerRadius
using Microsoft.Maui.Controls.Shapes; // RoundRectangle
using System.Collections.Generic;
using System.ComponentModel; // for PropertyChangedEventHandler
using Donezo.Pages.Components; // already present
using Microsoft.Maui.Layouts; // AbsoluteLayoutFlags
using Donezo.Pages.Components; // ensure namespace for BusyOverlayView
using System.Threading.Tasks; // added for Task

namespace Donezo.Pages;

// Restore types removed during refactor
public enum CompletionVisualState { Incomplete, Partial, Complete }

public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && b ? 0.6 : 1.0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class LevelAccentColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is ItemVm vm)
        {
            var primary = (Color)Application.Current!.Resources["Primary"];
            return vm.Level switch
            {
                1 => primary,
                2 => primary.WithAlpha(0.7f),
                _ => primary.WithAlpha(0.45f)
            };
        }
        return Colors.Transparent;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class LevelBadgeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        // Badges ("-" / "--") no longer needed due to indentation + expand chevrons.
        return string.Empty;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class LevelIndentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int lvl)
        {
            // Treat levels 1 and 2 as same visual indent so newly added root items (level 1) align with existing roots (level 2)
            int left = lvl <= 2 ? 16 : (lvl - 1) * 16;
            return new Thickness(left, 0, 0, 0);
        }
        return new Thickness(16,0,0,0);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class LevelBorderGapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int lvl)
        {
            int left = lvl <= 2 ? 16 : (lvl - 1) * 16;
            return new Thickness(left, 2, 0, 0);
        }
        return new Thickness(16,2,0,0);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class BoolToRotationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => (value is bool b && b) ? 90d : 0d;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class BoolToAccessibleExpandNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => (value is bool b && b) ? "Collapse" : "Expand";
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class ItemVm : BindableObject
{
    public int Id { get; }
    public int ListId { get; }
    private string _name;
    // Make setter public for inline rename updates
    public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(nameof(Name)); } }
    public bool ShowCompletedUser => IsCompleted && !string.IsNullOrWhiteSpace(CompletedByUsername);
    private string? _completedByUsername;
    public string? CompletedByUsername { get => _completedByUsername; set { if (_completedByUsername == value) return; _completedByUsername = value; OnPropertyChanged(nameof(CompletedByUsername)); OnPropertyChanged(nameof(ShowCompletedUser)); OnPropertyChanged(nameof(CompletedInfo)); OnPropertyChanged(nameof(ShowCompletedInfo)); } }
    private bool _isCompleted;
    public bool IsCompleted { get => _isCompleted; set { if (_isCompleted == value) return; _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); OnPropertyChanged(nameof(ShowCompletedUser)); } }
    public int? ParentId { get; }
    public bool HasChildren { get; }
    public int ChildrenCount { get; }
    public int IncompleteChildrenCount { get; private set; }
    public int Level { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }
    public List<ItemVm> Children { get; } = new();
    public int Order { get; set; }
    public string SortKey { get; }

    private bool _isDragging;
    public bool IsDragging { get => _isDragging; set { if (_isDragging == value) return; _isDragging = value; if (value) { IsPreDrag = false; } OnPropertyChanged(nameof(IsDragging)); } }
    private bool _isPreDrag;
    public bool IsPreDrag { get => _isPreDrag; set { if (_isPreDrag == value) return; _isPreDrag = value; OnPropertyChanged(nameof(IsPreDrag)); } }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

    private bool _isRenaming;
    public bool IsRenaming { get => _isRenaming; set { if (_isRenaming == value) return; _isRenaming = value; OnPropertyChanged(nameof(IsRenaming)); } }

    private string _editableName = string.Empty;
    public string EditableName { get => _editableName; set { if (_editableName == value) return; _editableName = value; OnPropertyChanged(nameof(EditableName)); } }

    private DateTime? _completedAtUtc;
    public DateTime? CompletedAtUtc { get => _completedAtUtc; set { if (_completedAtUtc == value) return; _completedAtUtc = value; OnPropertyChanged(nameof(CompletedAtUtc)); OnPropertyChanged(nameof(CompletedInfo)); OnPropertyChanged(nameof(ShowCompletedInfo)); } }
    public bool ShowCompletedInfo => IsCompleted && (!string.IsNullOrWhiteSpace(CompletedByUsername) || CompletedAtUtc != null);
    public string CompletedInfo
    {
        get
        {
            if (!IsCompleted) return string.Empty;
            var user = string.IsNullOrWhiteSpace(CompletedByUsername) ? "" : CompletedByUsername;
            // Updated format to include year
            var date = CompletedAtUtc?.ToLocalTime().ToString("M/d/yyyy HH:mm") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(date)) return user + " • " + date;
            if (!string.IsNullOrWhiteSpace(user)) return user;
            return date;
        }
    }
    public CompletionVisualState CompletionState { get; private set; }
    public string PartialGlyph => CompletionState == CompletionVisualState.Partial ? "-" : string.Empty;
    public string ExpandGlyph => HasChildren ? (_isExpanded ? "v" : ">") : string.Empty;

    public ItemVm(int id, int listId, string name, bool isCompleted, int? parentId, bool hasChildren, int childrenCount, int incompleteChildrenCount, int level, bool isExpanded, int order, string sortKey)
    {
        Id = id; ListId = listId; _name = name; _isCompleted = isCompleted; ParentId = parentId; HasChildren = hasChildren; ChildrenCount = childrenCount; IncompleteChildrenCount = incompleteChildrenCount; Level = level; _isExpanded = isExpanded; Order = order; SortKey = sortKey; RecalcState();
    }
    public void RecalcState()
    {
        if (Children.Count > 0)
            IncompleteChildrenCount = Children.Count(c => !c.IsCompleted);
        if (IsCompleted)
            CompletionState = CompletionVisualState.Complete;
        else if (Children.Count > 0 && IncompleteChildrenCount > 0 && IncompleteChildrenCount < Children.Count)
            CompletionState = CompletionVisualState.Partial;
        else
            CompletionState = CompletionVisualState.Incomplete;
        OnPropertyChanged(nameof(CompletionState));
        OnPropertyChanged(nameof(PartialGlyph));
    }
}

public partial class DashboardPage : ContentPage, IQueryAttributable
{
    private INeonDbService _db;
    private string _username = string.Empty;
    private int? _userId;

    private DualHeaderView _dualHeader = null!; // custom visual toggle

    // Hide Completed filter state/UI
    private bool _hideCompleted;
    private bool _suppressHideCompletedEvent; // now used to suppress toggle sync

    private int? _selectedListId; // selection moved to Lists partial, kept for property continuity
    private int? SelectedListId => _selectedListId;

    // Responsive layout fields
    private Grid _twoPaneGrid = null!;
    private Border _itemsCard = null!;

    // Polling state
    private CancellationTokenSource? _pollCts; // already declared; ensure used by polling logic
    private long _lastRevision;
    private bool _isRefreshing;
    private DateTime _skipAutoRefreshUntil; // suppression window for auto refresh

    // Hover auto-expand timers
    private readonly Dictionary<int, CancellationTokenSource> _hoverExpandCts = new();

    private ItemVm? _dragItem;
    private ItemVm? _pendingDragVm; // track drag start awaiting drop
    private bool _dragDropCompleted; // flag set true when a drop handler runs
    private ItemVm? _holdItem; // item currently pressed
    private CancellationTokenSource? _holdCts; // timer for drag engage
    private bool _dragGestureActive; // true during active drag
    private ItemVm? _selectedItem; // currently selected item for keyboard/accessibility moves

    private bool _suppressDailyEvent;
    private bool _suppressThemeEvent;
    private bool _initialized;

    // Item panel fields (added back)
    private ObservableCollection<ItemVm> _items = new(); // removed readonly to allow replacement
    private readonly List<ItemVm> _allItems = new();
    private readonly List<Border> _itemCardBorders = new();
    private Entry _newItemEntry = null!; // now lives inside overlay
    private Button _addItemButton = null!; // inside overlay
    private DataTemplate _itemViewTemplate = null!;
    private CollectionView _itemsView = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Button _resetSubtreeButton = null!;
    private Label _emptyFilteredLabel = null!;
    private Dictionary<int,bool> _expandedStates = new();
    private bool _suppressCompletionEvent;

    // Add missing fields near other item panel fields
    private Button _openNewItemButton = null!; // trigger button (defined in items card)
    private DateTime _recentLocalMutationUtc; // track last local mutation for polling suppression

    // Removed child item support (entries/buttons) per requirement.

    // List panel fields referenced by item helpers
    private Switch _hideCompletedSwitch = null!; // now built inside items panel
    private Picker _listPicker = null!; // list selection dropdown

    // Page overlay for user menu
    private Grid _pageRoot = null!; // 2-row grid (header, content)
    private AbsoluteLayout _pageOverlay = null!; // overlay root
    private Grid _menuAlignGrid = null!; // alignment grid for menu (right aligned)
    private Border _userMenuBorder = null!; // dropdown container
    private BoxView _menuScrim = null!; // captures outside taps
    private bool _userMenuVisible;

    // Search and filter fields
    private Entry _searchEntry; // search box (items panel) (unused but retained)
    private Label _statsLabel; // stats label (items panel)
    private string _itemSearchText = string.Empty; // current search text

    // New item overlay fields
    private Grid _newItemOverlayRoot = null!; // scrim
    private Border _newItemModal = null!; // modal content

    // Parameterless ctor for Shell route activation
    public DashboardPage() : this(ServiceHelper.GetRequiredService<INeonDbService>(), string.Empty) { }

    public DashboardPage(INeonDbService db, string username)
    {
        _db = db;
        _username = username ?? string.Empty;
        Title = string.Empty; // remove shell title bar so we can control placement
        Shell.SetNavBarIsVisible(this, false); // hide shell nav bar globally for this page
        BuildUi();
        if (!string.IsNullOrWhiteSpace(_username)) _ = InitializeAsync();
        // Responsive layout now simplified; keep stub
        SizeChanged += (_, _) => ApplyResponsiveLayout(Width);
        Application.Current!.RequestedThemeChanged += (_, __) =>
        {
            MainThread.BeginInvokeOnMainThread(() => { RebuildVisibleItems(); UpdateAllListSelectionVisuals(); });
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("username", out var v) && v is string name && !string.IsNullOrWhiteSpace(name))
        {
            _username = name; if (_dualHeader != null) _dualHeader.Username = name; if (!_initialized) _ = InitializeAsync();
        }
    }

    // Busy overlay fields
    private BusyOverlayView _busyOverlay; // overlay instance

    // Busy helpers
    private void ShowBusy(string msg){ _busyOverlay?.Show(msg); }
    private void HideBusy(){ _busyOverlay?.Hide(); }
    private async Task RunBusy(Func<Task> op,string msg){ if(_busyOverlay==null){ await op(); return;} await _busyOverlay.RunAsync(op,msg); }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RunBusy(async () =>
        {
            _userId = await _db.GetUserIdAsync(_username);
            if (_userId == null) return;
            await LoadThemePreferenceAsync();
            await RefreshListsPickerAsync();
        }, "Initializing dashboard...");
    }

    private async Task LoadThemePreferenceAsync()
    {
        if (_userId == null) return;
        var dark = await _db.GetUserThemeDarkAsync(_userId.Value);
        _suppressThemeEvent = true;
        _dualHeader.SetTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark), suppressEvent:true);
        ApplyTheme(dark ?? (Application.Current!.RequestedTheme == AppTheme.Dark));
        _suppressThemeEvent = false;
    }

    private async Task OnThemeToggledAsync(bool dark)
    {
        if (_suppressThemeEvent) return;
        ApplyTheme(dark);
        if (_userId != null) { try { await _db.SetUserThemeDarkAsync(_userId.Value, dark); } catch { } }
    }

    private async Task LogoutAsync()
    {
        try { SecureStorage.Remove("AUTH_USERNAME"); } catch { }
        _username = string.Empty; if (_dualHeader != null) _dualHeader.Username = string.Empty;
        try { await Shell.Current.GoToAsync("//login"); } catch { }
    }

    // Duplicate busy-wrapped RefreshItemsAsync and RefreshItemsInternalAsync removed to eliminate ambiguity.
    // Use the canonical implementation in DashboardPage.Items.cs. Busy overlay can be applied manually if desired.

    private void ApplyTheme(bool dark)
    {
        if (Application.Current is App app)
            app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        _dualHeader?.SyncFromAppTheme();
        RebuildVisibleItems();
        UpdateAllListSelectionVisuals();
    }

    private void ApplyResponsiveLayout(double width)
    {
        // No-op: layout simplified to vertical stack
    }

    private void BuildUi()
    {
        _listPicker = new Picker { Title = "Select List", ItemDisplayBinding = new Binding("Name") };
        _listPicker.SelectedIndexChanged += async (s,e)=>
        {
            var sel = _listPicker.SelectedItem;
            if (sel is ListRecord lr)
            {
                await RunBusy(async () => { _selectedListId = lr.Id; await RefreshItemsAsync(true); }, "Loading items...");
                StartRevisionPolling();
            }
            else if (sel is SharedListRecord sl)
            {
                await RunBusy(async () => { _selectedListId = sl.Id; await RefreshItemsAsync(true); }, "Loading items...");
                StartRevisionPolling();
            }
        };
        _itemsCard = BuildItemsCard();
        var contentStack = new VerticalStackLayout { Padding = new Thickness(20,10), Spacing = 16, Children = { _listPicker, _itemsCard } };
        _dualHeader = new DualHeaderView { TitleText = "Dashboard", Username = _username };
        _dualHeader.ThemeToggled += async (_, dark) => await OnThemeToggledAsync(dark);
        _dualHeader.LogoutRequested += async (_, __) => await LogoutAsync();
        _dualHeader.DashboardRequested += (_, __) => { /* already on dashboard */ };
        _dualHeader.ManageAccountRequested += async (_, __) => { try { await Shell.Current.GoToAsync("//manageaccount"); } catch { } };
        _dualHeader.ManageListsRequested += async (_, __) => { try { await Shell.Current.GoToAsync("//managelists?username=" + Uri.EscapeDataString(_username)); } catch { } };
        _dualHeader.UserMenuToggleRequested += (_, __) => ToggleUserMenu();
        _dualHeader.SetTheme(Application.Current!.RequestedTheme == AppTheme.Dark, suppressEvent:true);

        // Root layout
        _pageRoot = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        _pageRoot.Add(_dualHeader,0,0); _pageRoot.Add(new ScrollView { Content = contentStack },0,1);

        // Overlay root
        _pageOverlay = new AbsoluteLayout { IsClippedToBounds = false };
        AbsoluteLayout.SetLayoutBounds(_pageRoot, new Rect(0,0,1,1));
        AbsoluteLayout.SetLayoutFlags(_pageRoot, AbsoluteLayoutFlags.All);

        // Build scrim to catch outside taps
        _menuScrim = new BoxView { BackgroundColor = Colors.Transparent, IsVisible = false, InputTransparent = true };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.Tapped += async (_, __) => await HideUserMenuAsync();
        _menuScrim.GestureRecognizers.Add(scrimTap);
        AbsoluteLayout.SetLayoutBounds(_menuScrim, new Rect(0,0,1,1));
        AbsoluteLayout.SetLayoutFlags(_menuScrim, AbsoluteLayoutFlags.All);

        // Build user menu dropdown reusing menu content from header
        var primary = (Color)Application.Current!.Resources["Primary"];
        var menuContent = _dualHeader.GetMenuContentStack();
        _userMenuBorder = new Border
        {
            StrokeThickness = 1,
            Stroke = Colors.White.WithAlpha(0.35f),
            BackgroundColor = primary.WithAlpha(0.90f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(14, 12),
            Content = menuContent,
            TranslationY = 60, // drop below header
            Opacity = 0,
            IsVisible = false,
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0,4), Radius = 12, Opacity = 0.35f },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start
        };

        // Right align: a 2-col grid inside overlay (Star, Auto)
        _menuAlignGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0,0,6,0),
            InputTransparent = true,
            IsVisible = false
        };
        Grid.SetColumn(_userMenuBorder, 1);
        _menuAlignGrid.Children.Add(_userMenuBorder);

        // NEW: allow tapping anywhere outside the border (inside alignment grid) to close menu
        var outsideTap = new TapGestureRecognizer();
        outsideTap.Tapped += async (_, __) => { if (_userMenuVisible) await HideUserMenuAsync(); };
        _menuAlignGrid.GestureRecognizers.Add(outsideTap);

        AbsoluteLayout.SetLayoutBounds(_menuAlignGrid, new Rect(0,0,1,1));
        AbsoluteLayout.SetLayoutFlags(_menuAlignGrid, AbsoluteLayoutFlags.All);

        // Add children (order defines z-index): content, scrim, menu
        _pageOverlay.Children.Add(_pageRoot); // base content
        _pageOverlay.Children.Add(_menuScrim); // invisible click-catcher
        _pageOverlay.Children.Add(_menuAlignGrid); // overlay dropdown

        // Add new-item overlay (initially hidden)
        _newItemOverlayRoot = new Grid { IsVisible = false, BackgroundColor = Colors.Black.WithAlpha(0.45f) };
        _newItemModal = BuildNewItemModal();
        _newItemOverlayRoot.Children.Add(new Grid { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { _newItemModal } });
        var newBackdropTap = new TapGestureRecognizer(); newBackdropTap.Tapped += (_, __) => HideNewItemOverlay(); _newItemOverlayRoot.GestureRecognizers.Add(newBackdropTap);
        AbsoluteLayout.SetLayoutBounds(_newItemOverlayRoot, new Rect(0,0,1,1));
        AbsoluteLayout.SetLayoutFlags(_newItemOverlayRoot, AbsoluteLayoutFlags.All);
        _pageOverlay.Children.Add(_newItemOverlayRoot);

        Content = _pageOverlay;
        // Busy overlay last child for z-order
        _busyOverlay = new BusyOverlayView();
        AbsoluteLayout.SetLayoutBounds(_busyOverlay, new Rect(0,0,1,1));
        AbsoluteLayout.SetLayoutFlags(_busyOverlay, AbsoluteLayoutFlags.All);
        _pageOverlay.Children.Add(_busyOverlay);
    }

    private Border BuildNewItemModal()
    {
        _newItemEntry = new Entry { Placeholder = "Item name", Style = (Style)Application.Current!.Resources["FilledEntry"], WidthRequest = 260 };
        _addItemButton = new Button { Text = "Add", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _newItemEntry.TextChanged += (_, __) => _addItemButton.IsEnabled = CanAddItems() && !string.IsNullOrWhiteSpace(_newItemEntry.Text);
        _addItemButton.Clicked += async (_, __) => { if (!CanAddItems()) { await ShowViewerBlockedAsync("adding items"); return; } await AddItemAsync(); };
        var cancelButton = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        cancelButton.Clicked += (_, __) => HideNewItemOverlay(clear:true);
        var modal = new Border
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
                    new Label { Text = "Add New Item", FontAttributes = FontAttributes.Bold, FontSize = 20, HorizontalTextAlignment = TextAlignment.Center },
                    _newItemEntry,
                    new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Center, Children = { _addItemButton, cancelButton } }
                }
            }
        };
        if (Application.Current!.Resources.TryGetValue("CardBorder", out var styleObj) && styleObj is Style style) modal.Style = style;
        return modal;
    }

    private void ShowNewItemOverlay()
    {
        if (!CanAddItems()) { _ = ShowViewerBlockedAsync("adding items"); return; }
        _newItemOverlayRoot.IsVisible = true;
        _newItemEntry.Text = string.Empty;
        _addItemButton.IsEnabled = false;
        _newItemModal.Opacity = 0; _newItemModal.Scale = 0.90;
        _ = _newItemModal.FadeTo(1,160,Easing.CubicOut);
        _ = _newItemModal.ScaleTo(1,160,Easing.CubicOut);
        Device.BeginInvokeOnMainThread(() => _newItemEntry.Focus());
    }

    private void HideNewItemOverlay(bool clear=false)
    {
        if (clear) _newItemEntry.Text = string.Empty;
        if (!_newItemOverlayRoot.IsVisible) return;
        _ = _newItemModal.FadeTo(0,120,Easing.CubicOut);
        _ = _newItemModal.ScaleTo(0.92,120,Easing.CubicOut);
        Device.StartTimer(TimeSpan.FromMilliseconds(130), () => { _newItemOverlayRoot.IsVisible = false; return false; });
    }

    private async void ToggleUserMenu()
    {
        if (string.IsNullOrWhiteSpace(_dualHeader?.Username)) return; // guard by displayed header user
        if (_userMenuVisible)
        {
            await HideUserMenuAsync();
        }
        else
        {
            await ShowUserMenuAsync();
        }
    }

    private async Task ShowUserMenuAsync()
    {
        _userMenuVisible = true;
        _menuScrim.IsVisible = true;
        _menuScrim.InputTransparent = false;
        _menuAlignGrid.IsVisible = true;
        _menuAlignGrid.InputTransparent = false; // allow interaction with menu & outside tap on grid
        _userMenuBorder.Scale = 0.85;
        _userMenuBorder.IsVisible = true;
        await Task.WhenAll(_userMenuBorder.FadeTo(1, 160, Easing.CubicOut), _userMenuBorder.ScaleTo(1, 160, Easing.CubicOut));
    }

    private async Task HideUserMenuAsync()
    {
        _userMenuVisible = false;
        await Task.WhenAll(_userMenuBorder.FadeTo(0, 120, Easing.CubicOut), _userMenuBorder.ScaleTo(0.92, 120, Easing.CubicOut));
        _userMenuBorder.IsVisible = false;
        _menuAlignGrid.InputTransparent = true; // stop blocking touches
        _menuAlignGrid.IsVisible = false;
        _menuScrim.InputTransparent = true;
        _menuScrim.IsVisible = false;
    }

    private async Task RefreshListsPickerAsync()
    {
        if (_userId == null) return;
        await RunBusy(async () =>
        {
            var ownedRaw = await _db.GetOwnedListsAsync(_userId.Value);
            var actuallyOwned = new List<ListRecord>();
            foreach (var lr in ownedRaw)
            { try { var ownerId = await _db.GetListOwnerUserIdAsync(lr.Id); if (ownerId == _userId) actuallyOwned.Add(lr); } catch { } }
            var shared = await _db.GetSharedListsAsync(_userId.Value);
            _ownedLists = actuallyOwned; _sharedLists = shared;
            var combined = new List<object>(); combined.AddRange(actuallyOwned); combined.AddRange(shared);
            _listPicker.ItemsSource = combined;
            if (_selectedListId != null)
            {
                var match = combined.FirstOrDefault(o => (o is ListRecord lr && lr.Id == _selectedListId) || (o is SharedListRecord sl && sl.Id == _selectedListId));
                if (match != null) _listPicker.SelectedItem = match; else _listPicker.SelectedIndex = combined.Count > 0 ? 0 : -1;
            }
            else if (combined.Count > 0)
            { _listPicker.SelectedIndex = 0; }
        }, "Loading lists...");
    }

    private async Task RefreshListsAsync()
    { await RefreshListsPickerAsync(); }

    // ===== Added helper methods & converters restored after refactor =====

    // Converters referenced in Items partial
    private class InvertBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is bool b ? !b : true;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
    private class BoolToStringConverter : IValueConverter
    {
        public string TrueText { get; set; } = string.Empty;
        public string FalseText { get; set; } = string.Empty;
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => (value is bool b && b) ? TrueText : FalseText;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    // Selection helpers
    private void SetSingleSelection(ItemVm vm)
    {
        foreach (var it in _allItems)
            it.IsSelected = false;
        _selectedItem = vm;
        if (vm != null)
            vm.IsSelected = true;
        UpdateMoveButtons();
    }
    private void ClearSelectionAndUi()
    {
        foreach (var it in _allItems)
            it.IsSelected = false;
        _selectedItem = null;
        UpdateMoveButtons();
    }
    private bool IsUnder(ItemVm parent, ItemVm candidate)
    {
        if (parent == null || candidate == null || parent.Id == candidate.Id) return false;
        var cur = candidate; int guard = 0;
        while (cur.ParentId != null && guard++ < 256)
        {
            var p = _allItems.FirstOrDefault(x => x.Id == cur.ParentId);
            if (p == null) break;
            if (p.Id == parent.Id) return true;
            cur = p;
        }
        return false;
    }
    // Permissions
    private string? GetCurrentListRole()
    {
        var listId = _selectedListId; if (listId == null) return null;
        if (_ownedLists.Any(l => l.Id == listId.Value)) return "Owner";
        var shared = _sharedLists.FirstOrDefault(s => s.Id == listId.Value);
        return shared?.Role;
    }
    public bool CanModifyItems() => (GetCurrentListRole()) is string r && (r == "Owner" || r == "Contributor");
    public bool CanReorderItems() => CanModifyItems();
    public bool CanAddItems() => CanModifyItems();
    public bool CanDeleteItems() => CanModifyItems();
    public bool CanRenameItems() => CanModifyItems();
    public bool CanCompleteItems() => CanModifyItems();
    public bool CanResetSubtree() => CanModifyItems();
    public bool CanDragItems() => CanModifyItems();
    private async Task ShowViewerBlockedAsync(string action)
    { try { await DisplayAlert("View Only", $"Your role (Viewer) does not permit {action}.", "OK"); } catch { } }
    private void UpdateMoveButtons()
    {
        if (_moveUpButton == null || _moveDownButton == null || _resetSubtreeButton == null) return;
        bool hasSelection = _selectedItem != null;
        bool canReorder = hasSelection && CanReorderItems();
        _moveUpButton.IsEnabled = canReorder;
        _moveDownButton.IsEnabled = canReorder;
        _resetSubtreeButton.IsEnabled = hasSelection && CanResetSubtree();
    }
    // Revision polling (reinsert)
    private bool PollingSuppressed => _suppressListRevisionCheck;
    private void StartRevisionPolling()
    {
        StopRevisionPolling();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
        {
            if (token.IsCancellationRequested) return false;
            if (_selectedListId == null) return true;
            if (PollingSuppressed || _isRefreshing) return true;
            _ = CheckRevisionAsync();
            return true;
        });
    }
    private void StopRevisionPolling()
    { try { _pollCts?.Cancel(); } catch { } _pollCts = null; }
    private async Task CheckRevisionAsync()
    {
        if (_selectedListId == null) return;
        try
        {
            var current = await _db.GetListRevisionAsync(_selectedListId.Value);
            if (current != _lastRevision)
            {
                if (DateTime.UtcNow < _skipAutoRefreshUntil)
                { _lastRevision = current; return; }
                _lastRevision = current;
                await RefreshItemsAsync(false);
            }
        }
        catch { }
    }
    protected override void OnAppearing()
    { base.OnAppearing(); if (_selectedListId != null) StartRevisionPolling(); }
    protected override void OnDisappearing()
    { base.OnDisappearing(); StopRevisionPolling(); }

    private void ApplyItemCardStyle(Border b, ItemVm vm)
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        var dark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
        b.BackgroundColor = vm.IsSelected ? primary.WithAlpha(0.18f) : baseBg;
        b.Stroke = vm.IsDragging ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
    }
    private void RefreshItemCardStyles()
    {
        foreach (var b in _itemCardBorders)
            if (b.BindingContext is ItemVm vm) ApplyItemCardStyle(b, vm);
    }
    private void UpdateStats()
    {
        if (_statsLabel == null) return;
        int total = _allItems.Count;
        int completed = _allItems.Count(i => i.IsCompleted);
        int shown = _items.Count;
        _statsLabel.Text = $"Shown {shown} / Total {total} • Completed {completed}";
        _statsLabel.TextColor = completed == total && total > 0 ? Color.FromArgb("#008A2E") : (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "Gray300" : "Gray600"];
    }
    private async Task LoadHideCompletedPreferenceForSelectedListAsync()
    {
        if (_userId == null || _selectedListId == null) return;
        try
        {
            var pref = await _db.GetListHideCompletedAsync(_userId.Value, _selectedListId.Value);
            _hideCompleted = pref ?? false;
            if (_hideCompletedSwitch != null)
            {
                _suppressHideCompletedEvent = true;
                _hideCompletedSwitch.IsToggled = _hideCompleted;
                _suppressHideCompletedEvent = false;
            }
        }
        catch { }
    }
    private async Task OnHideCompletedToggledAsync(bool value)
    {
        if (_suppressHideCompletedEvent) return;
        _hideCompleted = value;
        if (_userId != null && _selectedListId != null)
        { try { await _db.SetListHideCompletedAsync(_userId.Value, _selectedListId.Value, value); } catch { } }
        RebuildVisibleItems();
    }
}
