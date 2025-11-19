using Donezo.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;
using System.Linq;
using Microsoft.Maui; // CornerRadius
using Microsoft.Maui.Controls.Shapes; // RoundRectangle
using System.Collections.Generic;
using System.ComponentModel; // for PropertyChangedEventHandler

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

    private Switch _themeSwitch = null!; // light/dark toggle
    private Label _themeLabel = null!;
    private Label _headerTitle = null!;

    // Hide Completed filter state/UI
    private bool _hideCompleted;
    private bool _suppressHideCompletedEvent;

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

    // Parameterless ctor for Shell route activation
    public DashboardPage() : this(ServiceHelper.GetRequiredService<INeonDbService>(), string.Empty) { }

    public DashboardPage(INeonDbService db, string username)
    {
        _db = db;
        _username = username ?? string.Empty;
        Title = "Dashboard";
        BuildUi();
        if (!string.IsNullOrWhiteSpace(_username)) _ = InitializeAsync();
        SizeChanged += (_, _) => ApplyResponsiveLayout(Width);
        Application.Current!.RequestedThemeChanged += (_, __) =>
        {
            MainThread.BeginInvokeOnMainThread(RebuildVisibleItems);
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

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    var listId = SelectedListId;
                    if (listId == null) continue;
                    var rev = await _db.GetListRevisionAsync(listId.Value);
                    if (rev != _lastRevision)
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () => await RefreshItemsAsync());
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
            if (_headerTitle != null) _headerTitle.Text = $"Welcome, {_username}";
            if (!_initialized) _ = InitializeAsync();
        }
    }

    private async Task LogoutAsync()
    { try { SecureStorage.Remove("AUTH_USERNAME"); } catch { } try { await Shell.Current.GoToAsync("//login"); } catch { } }

    private View Header()
    {
        var grid = new Grid
        {
            Padding = new Thickness(20, 30, 20, 30),
            BackgroundColor = (Color)Application.Current!.Resources["Primary"],
            ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        _headerTitle = new Label { Text = string.IsNullOrWhiteSpace(_username) ? "Welcome" : $"Welcome, {_username}", TextColor = Colors.White, FontSize = 24, FontAttributes = FontAttributes.Bold };
        var subtitle = new Label { Text = "Manage your lists", TextColor = Colors.White, Opacity = 0.9, FontSize = 14 };
        var titleStack = new VerticalStackLayout { Spacing = 4, Children = { _headerTitle, subtitle } };
        var logoutBtn = new Button { Text = "Logout", BackgroundColor = Colors.Transparent, TextColor = Colors.White, BorderColor = Colors.White, BorderWidth = 1, CornerRadius = 8, Padding = new Thickness(12, 6), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        logoutBtn.Clicked += async (_, _) => await LogoutAsync();
        grid.Add(titleStack, 0, 0); grid.Add(logoutBtn, 1, 0); return grid;
    }

    private void BuildUi()
    {
        // Preferences controls
        _themeLabel = new Label { Text = "Light", VerticalTextAlignment = TextAlignment.Center };
        _themeSwitch = new Switch();
        _themeSwitch.Toggled += async (s, e) => await OnThemeToggledAsync(e.Value);

        // Build list & item panels via partial helpers
        _listsCard = BuildListsCard();
        _itemsCard = BuildItemsCard();

        var prefsCard = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = 16,
                Children =
                {
                    new Label { Text = "Preferences", Style = (Style)Application.Current!.Resources["SectionTitle"] },
                    new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Theme" }, _themeLabel, _themeSwitch } },
                    BuildHideCompletedPreferenceRow() // partial helper builds hide completed row
                }
            }
        };
        prefsCard.Style = (Style)Application.Current!.Resources["CardBorder"]; // after creation

        _twoPaneGrid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        _twoPaneGrid.Add(_listsCard, 0, 0); _twoPaneGrid.Add(_itemsCard, 1, 0);

        var contentStack = new VerticalStackLayout { Padding = new Thickness(20, 10), Spacing = 16, Children = { prefsCard, _twoPaneGrid } };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(Header(), 0, 0); root.Add(new ScrollView { Content = contentStack }, 0, 1);
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
    { if (_userId == null) return; var dark = await _db.GetUserThemeDarkAsync(_userId.Value); _suppressThemeEvent = true; _themeSwitch.IsToggled = dark ?? false; ApplyTheme(_themeSwitch.IsToggled); _suppressThemeEvent = false; }

    private async Task OnThemeToggledAsync(bool dark)
    { if (_suppressThemeEvent) return; ApplyTheme(dark); if (_userId != null) await _db.SetUserThemeDarkAsync(_userId.Value, dark); }

    private void ApplyTheme(bool dark)
    {
        _themeLabel.Text = dark ? "Dark" : "Light";
        if (Application.Current is App app) app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        RebuildVisibleItems(); // ensure theme-aware bindings update
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
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        vm.IsExpanded = true;
                        if (_userId != null) await _db.SetItemExpandedAsync(_userId.Value, vm.Id, true);
                        RebuildVisibleItems();
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, cts.Token);
    }

#if WINDOWS
    private void RestoreFocusAfterListChange() => RestorePageFocus();
#else
    private void RestoreFocusAfterListChange() { }
#endif

    private void CancelHoverExpand(ItemVm vm) => CancelHoverExpand(vm.Id);
    private void CancelHoverExpand(ItemVm vm, bool remove) => CancelHoverExpand(vm.Id);

    private void CancelHoverExpand(int itemId)
    {
        if (_hoverExpandCts.TryGetValue(itemId, out var cts))
        {
            try { cts.Cancel(); } catch { }
            _hoverExpandCts.Remove(itemId);
        }
    }

    private async Task HandleDropAsync(ItemVm target, string action)
    {
        if (_dragItem == null) return;
        if (_dragItem.Id == target.Id) return;
        var listId = SelectedListId; if (listId == null) return;

        // Prevent moving into descendant
        if (action == "into" && IsDescendant(target, _dragItem))
        { await DisplayAlert("Move blocked", "Cannot move an item into its own descendant.", "OK"); return; }

        int? newParentId = action == "into" ? target.Id : target.ParentId;

        // Depth limit pre-check (matches server rule: newParentDepth + subtreeDepth > MaxDepth)
        var subtreeDepth = ComputeSubtreeDepth(_dragItem);
        var newParentDepth = 0;
        if (newParentId != null)
        {
            var parent = _allItems.FirstOrDefault(x => x.Id == newParentId.Value);
            newParentDepth = parent?.Level ?? 0;
        }
        if (newParentDepth + subtreeDepth > 3)
        {
            await DisplayAlert("Move blocked", "Depth limit reached (max depth is 3).", "OK");
            return;
        }

        var siblings = _allItems.Where(x => x.ParentId == newParentId).OrderBy(x => x.Order).ThenBy(x => x.Id).ToList();
        int? prevOrder = null; int? nextOrder = null;
        if (action == "above") { var prev = siblings.TakeWhile(x => x.Id != target.Id).LastOrDefault(); prevOrder = prev?.Order; nextOrder = target.Order; }
        else if (action == "below") { var after = siblings.SkipWhile(x => x.Id != target.Id).Skip(1).FirstOrDefault(); prevOrder = target.Order; nextOrder = after?.Order; }
        else { var last = siblings.LastOrDefault(); prevOrder = last?.Order; nextOrder = null; }
        int newOrder = ComputeBetweenOrder(prevOrder, nextOrder);

        long expectedRevision = await _db.GetListRevisionAsync(listId.Value);
        if (_dragItem.ParentId != newParentId)
        {
            var moved = await _db.MoveItemAsync(_dragItem.Id, newParentId, expectedRevision);
            if (!moved.Ok) { await DisplayAlert("Move blocked", "Depth/cycle/concurrency prevented move.", "OK"); await RefreshItemsAsync(); return; }
            expectedRevision = moved.NewRevision;
        }
        var ordered = await _db.SetItemOrderAsync(_dragItem.Id, newOrder, expectedRevision);
        if (!ordered.Ok) { await RefreshItemsAsync(); return; }
        await RefreshItemsAsync();
    }

    private async Task SafeHandleDropAsync(ItemVm target, string action)
    {
        try { await HandleDropAsync(target, action); _dragDropCompleted = true; }
        catch (Exception ex) { if (_dragItem is { }) _dragItem.IsDragging = false; await DisplayAlert("Reorder Error", ex.Message, "OK"); }
        finally
        {
            if (_dragItem is { }) { _dragItem.IsDragging = false; _dragItem.IsPreDrag = false; }
            foreach (var it in _allItems) { it.IsPreDrag = false; }
            _pendingDragVm = null; _dragItem = null;
        }
    }

    private static int ComputeSubtreeDepth(ItemVm node)
    { if (node.Children.Count == 0) return 1; int max = 0; foreach (var c in node.Children) max = Math.Max(max, ComputeSubtreeDepth(c)); return 1 + max; }

    private static bool IsDescendant(ItemVm potentialAncestor, ItemVm node)
    { if (node.Children.Count == 0) return false; foreach (var c in node.Children) { if (c.Id == potentialAncestor.Id) return true; if (IsDescendant(potentialAncestor, c)) return true; } return false; }

    // Helper: returns true if candidate is within the descendant chain of parent (excluding parent itself)
    private bool IsUnder(ItemVm parent, ItemVm candidate)
    {
        if (parent == null || candidate == null) return false;
        if (parent.Id == candidate.Id) return false;
        var current = candidate;
        int guard = 0;
        while (current.ParentId != null && guard++ < 256)
        {
            var p = _allItems.FirstOrDefault(x => x.Id == current.ParentId);
            if (p == null) break;
            if (p.Id == parent.Id) return true;
            current = p;
        }
        return false;
    }

    // Recursive visible population (unfiltered)
    private void AddWithDescendants(ItemVm node, List<ItemVm> target)
    {
        target.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children.OrderBy(c => c.SortKey)) AddWithDescendants(child, target);
    }

    // Filtered population: skip completed nodes entirely when hiding
    private void AddWithDescendantsFiltered(ItemVm node, List<ItemVm> target)
    {
        if (_hideCompleted && node.IsCompleted) return;
        target.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children.OrderBy(c => c.SortKey)) AddWithDescendantsFiltered(child, target);
    }

    private static int ComputeBetweenOrder(int? prev, int? next)
    {
        if (prev.HasValue && next.HasValue)
            return prev.Value + Math.Max(1, (next.Value - prev.Value) / 2);
        if (prev.HasValue)
            return prev.Value + 1;
        if (next.HasValue)
            return next.Value - 1;
        return 0;
    }

    protected override void OnHandlerChanged()
    { base.OnHandlerChanged();
#if WINDOWS
        try { if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe) { fe.KeyDown -= OnPageKeyDown; fe.KeyDown += OnPageKeyDown; fe.IsTabStop = true; fe.Loaded += (_, __) => fe.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); } } catch { }
#endif
    }
#if WINDOWS
    private void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            if (_selectedItem == null) return;
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Up:
                    NavigateSiblingSelection(-1); e.Handled = true; break;
                case Windows.System.VirtualKey.Down:
                    NavigateSiblingSelection(1); e.Handled = true; break;
                case Windows.System.VirtualKey.Left:
                    if (_selectedItem.HasChildren && _selectedItem.IsExpanded)
                    { _selectedItem.IsExpanded = false; RebuildVisibleItems(); if (_selectedItem != null) SetSingleSelection(_selectedItem); e.Handled = true; }
                    else
                    {
                        if (_selectedItem.ParentId != null)
                        {
                            var parent = _allItems.FirstOrDefault(x => x.Id == _selectedItem.ParentId.Value);
                            if (parent != null)
                            { SetSingleSelection(parent); e.Handled = true; }
                        }
                    }
                    break;
                case Windows.System.VirtualKey.Right:
                    if (_selectedItem.HasChildren && !_selectedItem.IsExpanded)
                    { _selectedItem.IsExpanded = true; RebuildVisibleItems(); if (_selectedItem != null) SetSingleSelection(_selectedItem); e.Handled = true; }
                    else if (_selectedItem.HasChildren && _selectedItem.IsExpanded)
                    {
                        var firstChild = _selectedItem.Children.OrderBy(c => c.SortKey).FirstOrDefault();
                        if (firstChild != null)
                        { SetSingleSelection(firstChild); e.Handled = true; }
                    }
                    break;
            }
        }
        catch { }
    }
#endif

    private static readonly BindableProperty TrackedVmProperty = BindableProperty.CreateAttached("TrackedVm", typeof(ItemVm), typeof(DashboardPage), null);
    private static readonly BindableProperty TrackedHandlerProperty = BindableProperty.CreateAttached("TrackedHandler", typeof(PropertyChangedEventHandler), typeof(DashboardPage), null);

    private void SetSingleSelection(ItemVm vm)
    {
        foreach (var it in _allItems)
            if (it.IsSelected) it.IsSelected = false;
        _selectedItem = vm;
        if (vm != null)
            vm.IsSelected = true;
        UpdateMoveButtons();
        UpdateChildControls();
    }
    private void ClearSelectionAndUi()
    {
        foreach (var it in _allItems)
            if (it.IsSelected) it.IsSelected = false;
        _selectedItem = null;
        UpdateMoveButtons();
        UpdateChildControls();
    }

    private void NavigateSiblingSelection(int delta)
    {
        if (_selectedItem == null) return;
        var siblings = _allItems.Where(x => x.ParentId == _selectedItem.ParentId)
                                 .OrderBy(x => x.SortKey)
                                 .ToList();
        var idx = siblings.FindIndex(x => x.Id == _selectedItem.Id);
        if (idx < 0) return;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= siblings.Count) return;
        var target = siblings[newIdx];
        var cur = target; int guard = 0; while (cur.ParentId != null && guard++ < 256)
        {
            var parent = _allItems.FirstOrDefault(p => p.Id == cur.ParentId);
            if (parent == null || !parent.IsExpanded) return;
            cur = parent;
        }
        SetSingleSelection(target);
    }

#if WINDOWS
    private void RestorePageFocus()
    {
        try
        {
            if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                fe.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            }
        }
        catch { }
    }
#endif

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

    private async Task OpenShareAsync(ListRecord lr)
    {
        try
        {
            if (_userId == null)
            {
                await DisplayAlert("Share", "User context missing.", "OK");
                return;
            }
            // Only allow owners to access share options
            var ownerId = await _db.GetListOwnerUserIdAsync(lr.Id);
            if (ownerId == null || ownerId.Value != _userId.Value)
            {
                await DisplayAlert("Share", "Only the list owner can manage sharing.", "OK");
                return;
            }
            var page = new ShareListPage(_db, lr, _userId.Value);
            await Navigation.PushModalAsync(page);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Share", $"Unable to open share dialog: {ex.Message}", "OK");
        }
    }
}
