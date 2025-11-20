using Donezo.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;
using System.Linq;
using Microsoft.Maui; // CornerRadius
using Microsoft.Maui.Controls.Shapes; // RoundRectangle
using System.Collections.Generic;
using System.ComponentModel; // for PropertyChangedEventHandler
using Donezo.Pages.Components; // already present

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
    private Border _listsCard = null!;
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
        SizeChanged += (_, _) => ApplyResponsiveLayout(Width);
        Application.Current!.RequestedThemeChanged += (_, __) =>
        {
            MainThread.BeginInvokeOnMainThread(() => { RebuildVisibleItems(); UpdateAllListSelectionVisuals(); });
        };
    }

    private bool _ownershipSubscribed; // prevent duplicate MessagingCenter subscriptions

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_ownershipSubscribed)
        {
            MessagingCenter.Subscribe<ShareListPage, int>(this, "OwnershipTransferred", async (sender, listId) =>
            {
                try
                {
                    // Refresh lists; owned/shared classification handled inside RefreshListsAsync
                    await RefreshListsAsync();
                    _selectedListId = listId;
                    UpdateAllListSelectionVisuals();
                }
                catch { }
            });
            _ownershipSubscribed = true;
        }
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Unsubscribe to avoid leaks
        if (_ownershipSubscribed)
        {
            try { MessagingCenter.Unsubscribe<ShareListPage, int>(this, "OwnershipTransferred"); } catch { }
            _ownershipSubscribed = false;
        }
        StopPolling();
    }

    // Modify StartPolling to respect throttling and suppression
    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        DateTime lastRevisionCheckUtc = DateTime.MinValue;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct); // poll less frequently
                    var listId = SelectedListId;
                    if (listId == null) continue;
                    // Skip if a refresh is running or just finished very recently
                    if (_isRefreshing || _suppressListRevisionCheck || DateTime.UtcNow - _lastItemsRefreshStartUtc < ItemsRefreshMinInterval)
                        continue;
                    // Debounce revision checks
                    if (DateTime.UtcNow - lastRevisionCheckUtc < TimeSpan.FromSeconds(2)) continue;
                    lastRevisionCheckUtc = DateTime.UtcNow;
                    var rev = await _db.GetListRevisionAsync(listId.Value);
                    if (rev != _lastRevision)
                    {
                        // Update stored revision and schedule a single refresh (if not throttled)
                        _lastRevision = rev;
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            if (!_isRefreshing && DateTime.UtcNow - _lastItemsRefreshStartUtc >= ItemsRefreshMinInterval)
                                await RefreshItemsAsync();
                        });
                    }
                }
                catch (TaskCanceledException) { }
                catch { }
            }
        }, ct);
    }

    private void StopPolling()
    {
        try { _pollCts?.Cancel(); } catch { }
        _pollCts = null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("username", out var val) && val is string name && !string.IsNullOrWhiteSpace(name))
        {
            _username = name;
            if (_dualHeader != null) { _dualHeader.Username = _username; }
            if (!_initialized) _ = InitializeAsync();
        }
    }

    private async Task LogoutAsync()
    { try { SecureStorage.Remove("AUTH_USERNAME"); } catch { } _username = string.Empty; if (_dualHeader != null) { _dualHeader.Username = string.Empty; } try { await Shell.Current.GoToAsync("//login"); } catch { } }

    private void BuildUi()
    {
        _listsCard = BuildListsCard();
        _itemsCard = BuildItemsCard();
        _twoPaneGrid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        _twoPaneGrid.Add(_listsCard, 0, 0); _twoPaneGrid.Add(_itemsCard, 1, 0);
        var contentStack = new VerticalStackLayout { Padding = new Thickness(20, 10), Spacing = 16, Children = { _twoPaneGrid } };

        _dualHeader = new DualHeaderView { TitleText = "Dashboard", Username = _username };
        _dualHeader.ThemeToggled += async (_, dark) => await OnThemeToggledAsync(dark);
        _dualHeader.LogoutRequested += async (_, __) => await LogoutAsync();
        _dualHeader.DashboardRequested += (_, __) => { /* already on dashboard */ };
        _dualHeader.ManageAccountRequested += async (_, __) => { try { await Shell.Current.GoToAsync("//manageaccount"); } catch { } };
        _dualHeader.ManageListsRequested += async (_, __) => { try { await Shell.Current.GoToAsync("//dashboard"); } catch { } };
        // Initialize theme state
        _dualHeader.SetTheme(Application.Current!.RequestedTheme == AppTheme.Dark, suppressEvent:true);

        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(_dualHeader, 0, 0);
        root.Add(new ScrollView { Content = contentStack }, 0, 1);
        Content = root; ApplyResponsiveLayout(Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (_twoPaneGrid == null) return;
        const double threshold = 900;
        _twoPaneGrid.ColumnDefinitions.Clear(); _twoPaneGrid.RowDefinitions.Clear();
        if (width >= threshold)
        { _twoPaneGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); _twoPaneGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); _twoPaneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetColumn(_listsCard, 0); Grid.SetRow(_listsCard, 0); Grid.SetColumn(_itemsCard, 1); Grid.SetRow(_itemsCard, 0); }
        else
        { _twoPaneGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); _twoPaneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); _twoPaneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetColumn(_listsCard, 0); Grid.SetRow(_listsCard, 0); Grid.SetColumn(_itemsCard, 0); Grid.SetRow(_itemsCard, 1); }
    }

    private async Task InitializeAsync()
    { if (_initialized) return; _initialized = true; _userId = await _db.GetUserIdAsync(_username); if (_userId == null) { await DisplayAlert("Error", "User not found.", "OK"); return; } await LoadThemePreferenceAsync(); await RefreshListsAsync(); }

    private async Task LoadThemePreferenceAsync()
    { if (_userId == null) return; var dark = await _db.GetUserThemeDarkAsync(_userId.Value); _suppressThemeEvent = true; _dualHeader.SetTheme(dark ?? false, suppressEvent:true); ApplyTheme(dark ?? false); _suppressThemeEvent = false; }

    private async Task OnThemeToggledAsync(bool dark)
    { if (_suppressThemeEvent) return; ApplyTheme(dark); if (_userId != null) await _db.SetUserThemeDarkAsync(_userId.Value, dark); }

    private void ApplyTheme(bool dark)
    {
        if (Application.Current is App app) app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        _dualHeader?.SyncFromAppTheme();
        RebuildVisibleItems();
        UpdateAllListSelectionVisuals();
    }

    // Restore missing converters and item helper methods
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
        if (vm != null)
        {
            vm.IsSelected = true;
        }
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
    private void NavigateSiblingSelection(int delta)
    {
        if (_selectedItem is null) return;
        var siblings = _allItems.Where(x => x.ParentId == _selectedItem.ParentId).OrderBy(x => x.SortKey).ToList();
        var idx = siblings.FindIndex(x => x.Id == _selectedItem.Id);
        if (idx < 0) return;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= siblings.Count) return;
        var target = siblings[newIdx];
        SetSingleSelection(target);
    }

    // Card styling
    private void ApplyItemCardStyle(Border b, ItemVm vm)
    {
        var primary = (Color)Application.Current!.Resources["Primary"];
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
        b.BackgroundColor = vm.IsSelected ? primary.WithAlpha(0.18f) : baseBg;
        b.Stroke = vm.IsDragging ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
    }
    private void RefreshItemCardStyles()
    {
        foreach (var b in _itemCardBorders)
            if (b.BindingContext is ItemVm vm) ApplyItemCardStyle(b, vm);
    }

    // Drag & drop helpers
    private static int ComputeSubtreeDepth(ItemVm node)
    { if (node.Children.Count == 0) return 1; int max = 0; foreach (var c in node.Children) max = Math.Max(max, ComputeSubtreeDepth(c)); return 1 + max; }
    private static bool IsDescendant(ItemVm potentialAncestor, ItemVm node)
    { foreach (var c in node.Children) { if (c.Id == potentialAncestor.Id) return true; if (IsDescendant(potentialAncestor, c)) return true; } return false; }
    private static int ComputeBetweenOrder(int? prev, int? next)
    {
        if (prev.HasValue && next.HasValue) return prev.Value + Math.Max(1, (next.Value - prev.Value) / 2);
        if (prev.HasValue) return prev.Value + 1;
        if (next.HasValue) return next.Value - 1;
        return 0;
    }
    private async Task HandleDropAsync(ItemVm target, string action)
    {
        // No-op fallback
        await Task.CompletedTask;
    }
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
    private void ScheduleHoverExpand(ItemVm vm)
    {
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
    {
        if (_hoverExpandCts.TryGetValue(itemId, out var cts)) { try { cts.Cancel(); } catch { } _hoverExpandCts.Remove(itemId); }
    }

    // Permissions (fields _ownedLists/_sharedLists live in Lists partial)
    private string? GetCurrentListRole()
    {
        var listId = SelectedListId; if (listId == null) return null;
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

    private async Task OpenShareAsync(ListRecord lr)
    {
        // Stub: real implementation may exist in another partial.
        await Task.CompletedTask;
    }
#if WINDOWS
    private void RestorePageFocus()
    {
        try { if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe) fe.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); } catch { }
    }
#else
    private void RestorePageFocus() { }
#endif
}
// end class DashboardPage

// end namespace Donezo.Pages
