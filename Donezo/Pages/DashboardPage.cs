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
        if (value is ItemVm vm)
        {
            return vm.Level switch
            {
                1 => string.Empty,
                2 => "-",
                _ => "--"
            };
        }
        return string.Empty;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
public class LevelIndentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int lvl ? new Thickness((lvl - 1) * 16, 0, 0, 0) : new Thickness(0);
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
    public string? CompletedByUsername { get => _completedByUsername; set { if (_completedByUsername == value) return; _completedByUsername = value; OnPropertyChanged(nameof(CompletedByUsername)); OnPropertyChanged(nameof(ShowCompletedUser)); } }
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
    private CancellationTokenSource? _pollCts;
    private long _lastRevision;
    private bool _isRefreshing;

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
    private Entry _newItemEntry = null!;
    private Entry _newChildEntry = null!;
    private Button _addItemButton = null!;
    private Button _addChildButton = null!;
    private DataTemplate _itemViewTemplate = null!;
    private CollectionView _itemsView = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Button _resetSubtreeButton = null!;
    private Label _emptyFilteredLabel = null!;
    private Dictionary<int,bool> _expandedStates = new();
    private bool _suppressCompletionEvent;

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

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _userId = await _db.GetUserIdAsync(_username);
        if (_userId == null) return;
        await LoadThemePreferenceAsync();
        await RefreshListsAsync();
    }

    private async Task LogoutAsync()
    {
        try { SecureStorage.Remove("AUTH_USERNAME"); } catch { }
        _username = string.Empty; if (_dualHeader != null) _dualHeader.Username = string.Empty;
        try { await Shell.Current.GoToAsync("//login"); } catch { }
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
        _listPicker = new Picker { Title = "Select List" };
        // Ensure picker displays list names instead of record type names for both owned and shared list records.
        _listPicker.ItemDisplayBinding = new Binding("Name");
        _listPicker.SelectedIndexChanged += async (s,e)=>
        {
            if (_listPicker.SelectedItem is ListRecord lr) { _selectedListId = lr.Id; await RefreshItemsAsync(userInitiated:true); }
            else if (_listPicker.SelectedItem is SharedListRecord sl) { _selectedListId = sl.Id; await RefreshItemsAsync(userInitiated:true); }
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

        Content = _pageOverlay;
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
        var ownedRaw = await _db.GetOwnedListsAsync(_userId.Value);
        var actuallyOwned = new List<ListRecord>();
        foreach (var lr in ownedRaw)
        { try { var ownerId = await _db.GetListOwnerUserIdAsync(lr.Id); if (ownerId == _userId) actuallyOwned.Add(lr); } catch { } }
        var shared = await _db.GetSharedListsAsync(_userId.Value);

        // UPDATE: persist lists for role checks (was missing, causing all roles to appear as Viewer)
        _ownedLists = actuallyOwned; // role: Owner by definition
        _sharedLists = shared;      // roles from membership records

        // Combine into single object list for picker
        var combined = new List<object>(); combined.AddRange(actuallyOwned); combined.AddRange(shared);
        _listPicker.ItemsSource = combined;
        if (_selectedListId != null)
        {
            var match = combined.FirstOrDefault(o => (o is ListRecord lr && lr.Id == _selectedListId) || (o is SharedListRecord sl && sl.Id == _selectedListId));
            if (match != null) _listPicker.SelectedItem = match; else _listPicker.SelectedIndex = 0;
        }
        else if (combined.Count > 0)
        { _listPicker.SelectedIndex = 0; }
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
        foreach (var it in _allItems) it.IsSelected = false;
        _selectedItem = vm;
        if (vm != null) vm.IsSelected = true;
        UpdateMoveButtons();
        UpdateChildControls();
    }
    private void ClearSelectionAndUi()
    {
        foreach (var it in _allItems) it.IsSelected = false;
        _selectedItem = null;
        UpdateMoveButtons();
        UpdateChildControls();
    }

    // Card styling
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

    // Permissions / roles (uses _ownedLists / _sharedLists from Lists partial)
    private string? GetCurrentListRole()
    {
        var listId = _selectedListId; if (listId == null) return null;
        if (_ownedLists.Any(l => l.Id == listId.Value)) return "Owner";
        var shared = _sharedLists.FirstOrDefault(s => s.Id == listId.Value);
        return shared?.Role;
    }
    private bool IsOwnerRole() => GetCurrentListRole() == "Owner";
    public bool CanModifyItems() { var role = GetCurrentListRole(); return role == "Owner" || role == "Contributor"; }
    public bool CanReorderItems() => CanModifyItems();
    public bool CanAddItems() => CanModifyItems();
    public bool CanDeleteItems() => CanModifyItems();
    public bool CanRenameItems() => CanModifyItems();
    public bool CanCompleteItems() => CanModifyItems();
    public bool CanResetSubtree() => CanModifyItems();
    public bool CanDragItems() => CanModifyItems();
    private async Task ShowViewerBlockedAsync(string action)
    { try { await DisplayAlert("View Only", $"Your role (Viewer) does not permit {action}.", "OK"); } catch { } }

    private void ScheduleHoverExpand(ItemVm vm)
    {
        if (vm == null) return;
        CancelHoverExpand(vm);
        var cts = new CancellationTokenSource();
        _hoverExpandCts[vm.Id] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                if (!cts.IsCancellationRequested)
                    await MainThread.InvokeOnMainThreadAsync(() => { vm.IsExpanded = true; RebuildVisibleItems(); });
            }
            catch (TaskCanceledException) { }
        }, cts.Token);
    }
    private void CancelHoverExpand(ItemVm vm) => CancelHoverExpand(vm.Id);
    private void CancelHoverExpand(int itemId)
    { if (_hoverExpandCts.TryGetValue(itemId, out var cts)) { try { cts.Cancel(); } catch { } _hoverExpandCts.Remove(itemId); } }

    private async Task HandleDropAsync(ItemVm target, string action)
    { await Task.CompletedTask; }
    private async Task SafeHandleDropAsync(ItemVm target, string action)
    {
        try { await HandleDropAsync(target, action); _dragDropCompleted = true; }
        catch (Exception ex) { if (_dragItem is { }) _dragItem.IsDragging = false; await DisplayAlert("Reorder Error", ex.Message, "OK"); }
        finally
        {
            if (_dragItem is { }) { _dragItem.IsDragging = false; _dragItem.IsPreDrag = false; }
            foreach (var it in _allItems) it.IsPreDrag = false;
            _pendingDragVm = null; _dragItem = null;
        }
    }
}
