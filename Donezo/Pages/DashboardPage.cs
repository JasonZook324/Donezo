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
    public bool IsCompleted { get; set; }
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
        Id = id; ListId = listId; _name = name; IsCompleted = isCompleted; ParentId = parentId; HasChildren = hasChildren; ChildrenCount = childrenCount; IncompleteChildrenCount = incompleteChildrenCount; Level = level; _isExpanded = isExpanded; Order = order; SortKey = sortKey; RecalcState();
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

public class DashboardPage : ContentPage, IQueryAttributable
{
    private INeonDbService _db;
    private string _username = string.Empty;
    private int? _userId;
    // REPLACED: private Picker _listsPicker = null!;
    private CollectionView _listsView = null!; // new list selection view
    private readonly ObservableCollection<ListRecord> _listsObservable = new();
    private int? _selectedListId; // replaces picker SelectedItem
    private Entry _newListEntry = null!;
    private CheckBox _dailyCheck = null!;
    private Button _createListButton = null!;
    private Button _deleteListButton = null!;
    private Button _resetListButton = null!;
    private CollectionView _itemsView = null!;
    private Entry _newItemEntry = null!;
    private Button _addItemButton = null!;
    private Switch _themeSwitch = null!; // light/dark toggle
    private Label _themeLabel = null!;
    private Label _headerTitle = null!;
    // Child creation controls (new)
    private Entry _newChildEntry = null!;
    private Button _addChildButton = null!;

    // Hide Completed filter state/UI
    private bool _hideCompleted;
    private Switch _hideCompletedSwitch = null!;
    private bool _suppressHideCompletedEvent;
    private Label _emptyFilteredLabel = null!;

    private readonly ObservableCollection<ItemVm> _items = new();
    private List<ItemVm> _allItems = new();
    private IReadOnlyList<ListRecord> _lists = Array.Empty<ListRecord>();
    private Dictionary<int, bool> _expandedStates = new();
    private ItemVm? _dragItem;
    private ItemVm? _pendingDragVm; // track drag start awaiting drop
    private bool _dragDropCompleted; // flag set true when a drop handler runs
    private ItemVm? _holdItem; // item currently pressed
    private CancellationTokenSource? _holdCts; // timer for drag engage
    private bool _dragGestureActive; // true during active drag
    private ItemVm? _selectedItem; // currently selected item for keyboard/accessibility moves

    private readonly Label _completedBadge = new() { Text = "Completed", BackgroundColor = Colors.Green, TextColor = Colors.White, Padding = new Thickness(8, 2), IsVisible = false, FontAttributes = FontAttributes.Bold };

    private bool _suppressDailyEvent;
    private bool _suppressThemeEvent;
    private bool _initialized;
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

    private DataTemplate? _itemViewTemplate; // for custom item template

    // List visual helper methods (inserted)
    private readonly List<Border> _listItemBorders = new(); // track list item borders for visual refresh
    private void ApplyListVisual(Border b)
    {
        if (b.BindingContext is ListRecord lr)
        {
            var primary = (Color)Application.Current!.Resources["Primary"];
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
            bool selected = _selectedListId == lr.Id;
            b.BackgroundColor = selected ? primary.WithAlpha(0.12f) : baseBg;
            b.Stroke = selected ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
        }
    }
    private void UpdateAllListSelectionVisuals()
    {
        foreach (var b in _listItemBorders)
        {
            ApplyListVisual(b);
        }
    }
    // Replace previous UpdateAllListVisuals logic
    private void UpdateAllListVisuals() => UpdateAllListSelectionVisuals();

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
            // Force rebind so theme-aware converters re-evaluate consistently
            MainThread.BeginInvokeOnMainThread(RebuildVisibleItems);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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
        // LISTS PANEL --------------------------------------------------
        _newListEntry = new Entry { Placeholder = "New list name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _dailyCheck = new CheckBox { VerticalOptions = LayoutOptions.Center };
        _dailyCheck.CheckedChanged += async (s, e) => await OnDailyToggledAsync(e.Value);
        var dailyRow = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Daily" }, _dailyCheck } };
        _createListButton = new Button { Text = "Create", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _createListButton.Clicked += async (_, _) => await CreateListAsync();
        _deleteListButton = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red };
        _deleteListButton.Clicked += async (_, _) => await DeleteCurrentListAsync();
        _resetListButton = new Button { Text = "Reset", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _resetListButton.Clicked += async (_, _) => await ResetCurrentListAsync();

        // NEW: Instantiate previously null UI controls (preferences + items + child creation + filter placeholder)
        _themeLabel = new Label { Text = "Light", VerticalTextAlignment = TextAlignment.Center };
        _themeSwitch = new Switch();
        _themeSwitch.Toggled += async (s, e) => await OnThemeToggledAsync(e.Value);

        _hideCompletedSwitch = new Switch();
        _hideCompletedSwitch.Toggled += async (s, e) => await OnHideCompletedToggledAsync(e.Value);

        _emptyFilteredLabel = new Label { IsVisible = false, TextColor = Colors.Gray, FontSize = 12 };

        _newItemEntry = new Entry { Placeholder = "New item name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _newItemEntry.TextChanged += (s, e) => { if (_addItemButton != null) _addItemButton.IsEnabled = !string.IsNullOrWhiteSpace(_newItemEntry.Text); };
        _addItemButton = new Button { Text = "Add", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _addItemButton.Clicked += async (_, __) => await AddItemAsync();

        _newChildEntry = new Entry { Placeholder = "New child name", Style = (Style)Application.Current!.Resources["FilledEntry"], IsVisible = false };
        _newChildEntry.TextChanged += (s, e) => UpdateChildControls();
        _addChildButton = new Button { Text = "Add Child", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false, IsVisible = false };
        _addChildButton.Clicked += async (_, __) => await AddChildItemAsync();

        _listsView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _listsObservable,
            ItemTemplate = new DataTemplate(() =>
            {
                var border = new Border
                {
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(10,6),
                    Margin = new Thickness(0,4)
                };
                var name = new Label { FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };
                name.SetBinding(Label.TextProperty, nameof(ListRecord.Name));
                var daily = new Border { BackgroundColor = (Color)Application.Current!.Resources["Primary"], StrokeThickness = 0, Padding = new Thickness(6,2), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Content = new Label { Text = "Daily", FontSize = 12, TextColor = Colors.White } };
                daily.SetBinding(IsVisibleProperty, nameof(ListRecord.IsDaily));
                var shareBtn = new Button { Text = "Share", FontSize = 12, Padding = new Thickness(10,4), Style = (Style)Application.Current!.Resources["OutlinedButton"] };
                shareBtn.Clicked += async (s,e)=>{
                    if (border.BindingContext is ListRecord lr)
                    {
                        await OpenShareAsync(lr);
                    }
                };
                var tapSelect = new TapGestureRecognizer();
                tapSelect.Tapped += (s,e)=>{
                    if (border.BindingContext is ListRecord lr)
                    {
                        if (_selectedListId == lr.Id) return;
                        _selectedListId = lr.Id;
                        UpdateAllListVisuals();
                        _ = RefreshItemsAsync();
                        SyncDailyCheckboxWithSelectedList();
                        if (_listsView != null) _listsView.SelectedItem = lr;
                    }
                };
                border.GestureRecognizers.Add(tapSelect);
                border.Content = new HorizontalStackLayout { Spacing = 8, Children = { name, daily, shareBtn } };
                border.BindingContextChanged += (s,e)=> {
                    if (!_listItemBorders.Contains(border)) _listItemBorders.Add(border);
                    ApplyListVisual(border);
                };
                return border;
            })
        };
        _listsView.SelectionChanged += async (s, e) =>
        {
            var lr = e.CurrentSelection.FirstOrDefault() as ListRecord;
            if (lr == null) return;
            if (_selectedListId == lr.Id) return; // already selected
            _selectedListId = lr.Id;
            UpdateAllListVisuals();
            await RefreshItemsAsync();
            SyncDailyCheckboxWithSelectedList();
        };

        var listsHeader = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        listsHeader.Add(new Label { Text = "Lists", Style = (Style)Application.Current!.Resources["SectionTitle"] });
        listsHeader.Add(_completedBadge, 1, 0);

        _listsCard = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = 16,
                Children =
                {
                    listsHeader,
                    _listsView,
                    dailyRow,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton, _deleteListButton, _resetListButton } }
                }
            }
        };
        _listsCard.Style = (Style)Application.Current!.Resources["CardBorder"];

        // ITEMS PANEL (existing code retained) ----------------------
        // Reusable template that binds background/stroke via converters so it tracks theme
        _itemViewTemplate = new DataTemplate(() =>
        {
            var card = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = new Thickness(0) };
            card.SetBinding(Border.OpacityProperty, new Binding(nameof(ItemVm.IsDragging), converter: new BoolToOpacityConverter()));

            Action<ItemVm> applyStyle = vm =>
            {
                var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
                var defaultBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
                var defaultStroke = (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
                if (vm.IsDragging)
                {
                    card.BackgroundColor = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.18f);
                    card.Opacity = 0.75; card.Stroke = (Color)Application.Current!.Resources["Primary"]; return;
                }
                if (vm.IsPreDrag)
                {
                    card.BackgroundColor = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.12f);
                    card.Opacity = 0.9; card.Stroke = defaultStroke; return;
                }
                if (vm.IsSelected)
                {
                    card.BackgroundColor = defaultBg;
                    card.Opacity = 1.0; card.Stroke = (Color)Application.Current!.Resources["Primary"]; return;
                }
                card.BackgroundColor = defaultBg; card.Opacity = 1.0; card.Stroke = defaultStroke;
            };

            Color zoneHover = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.15f);
            double GetInsertionGapHeight()
            {
                try
                {
                    var info = DeviceDisplay.Current.MainDisplayInfo;
                    var widthDp = info.Width / info.Density;
                    var rowH = card.Height; if (rowH <= 0) rowH = 56;
                    double cap = widthDp < 380 ? 20 : (widthDp < 600 ? 28 : 36);
                    double min = 12; double target = Math.Min(rowH * 0.55, cap); if (target < min) target = min; return target;
                }
                catch { return 20; }
            }

            // External gap borders (separate from card border)
            var bottomGap = new Border
            {
                HeightRequest = 4,
                BackgroundColor = Colors.Transparent,
                StrokeThickness = 1,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
                Padding = 0,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var bottomDrop = new DropGestureRecognizer();
            bottomDrop.DragOver += (_, __) =>
            {
                if (_dragItem == null) return;
                bottomGap.HeightRequest = GetInsertionGapHeight();
                bottomGap.BackgroundColor = zoneHover;
                bottomGap.Stroke = (Color)Application.Current!.Resources["Primary"]; // outline
            };
            bottomDrop.DragLeave += (_, __) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; };
            bottomDrop.Drop += async (s, e) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm target) await SafeHandleDropAsync(target, "below"); };
            bottomGap.GestureRecognizers.Add(bottomDrop);

            // Inner item grid
            var grid = new Grid
            {
                Padding = new Thickness(4, 4),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto), // rename/save
                    new ColumnDefinition(GridLength.Auto)  // cancel
                }
            };

            // badge
            var badge = new Label { Margin = new Thickness(4, 0), VerticalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold };
            badge.SetBinding(Label.TextProperty, new Binding(".", converter: new LevelBadgeConverter()));
            badge.SetBinding(Label.TextColorProperty, new Binding(".", converter: new LevelAccentColorConverter()));
            grid.Add(badge, 0, 0);

            void OnDragStarting(object? s, DragStartingEventArgs e)
            {
                if (((BindableObject)card).BindingContext is ItemVm vm)
                {
                    _holdCts?.Cancel();
                    vm.IsPreDrag = false; vm.IsDragging = true; applyStyle(vm);
                    _dragItem = vm; _pendingDragVm = vm; _dragDropCompleted = false;
                    try { e.Data.Properties["ItemId"] = vm.Id; } catch { }
                }
            }
            var drag = new DragGestureRecognizer { CanDrag = true }; drag.DragStarting += OnDragStarting; card.GestureRecognizers.Add(drag);
            var dragOnGrid = new DragGestureRecognizer { CanDrag = true }; dragOnGrid.DragStarting += OnDragStarting; grid.GestureRecognizers.Add(dragOnGrid);

            // expand icon
            var expandIcon = new Label { Text = ">", WidthRequest = 28, HeightRequest = 28, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
            expandIcon.SetBinding(Label.RotationProperty, new Binding(nameof(ItemVm.IsExpanded), converter: new BoolToRotationConverter()));
            expandIcon.SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(ItemVm.IsExpanded), converter: new BoolToAccessibleExpandNameConverter()));
            expandIcon.SetBinding(View.IsVisibleProperty, new Binding(nameof(ItemVm.HasChildren)));
            var expandTap = new TapGestureRecognizer();
            expandTap.Tapped += async (s, e) => {
                if (((BindableObject)s).BindingContext is ItemVm vm) {
                    if (!vm.HasChildren) return;
                    bool collapsing = vm.IsExpanded;
                    vm.IsExpanded = !vm.IsExpanded;
                    if (collapsing && _selectedItem != null && IsUnder(vm, _selectedItem))
                    {
                        SetSingleSelection(vm);
                    }
                    else
                    {
                        if (_selectedItem != null)
                        {
                            var current = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id);
                            if (current != null) SetSingleSelection(current); else ClearSelectionAndUi();
                        }
                        else ClearSelectionAndUi();
                    }
                    if (_userId != null) await _db.SetItemExpandedAsync(_userId.Value, vm.Id, vm.IsExpanded);
                    RebuildVisibleItems();
                }
            };
            expandIcon.GestureRecognizers.Add(expandTap);
            grid.Add(expandIcon, 1, 0);

            // Name label and inline entry
            var nameContainer = new Grid();
            var nameLabel = new Label { VerticalTextAlignment = TextAlignment.Center };
            nameLabel.SetBinding(Label.TextProperty, nameof(ItemVm.Name));
            nameLabel.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameLabel.SetBinding(View.IsVisibleProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new InvertBoolConverter()));
            var nameEntry = new Entry { HeightRequest = 32, FontSize = 14 };
            nameEntry.SetBinding(Entry.TextProperty, nameof(ItemVm.EditableName), BindingMode.TwoWay);
            nameEntry.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameEntry.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            nameContainer.Add(nameLabel); nameContainer.Add(nameEntry);
            var dragOnName = new DragGestureRecognizer { CanDrag = true }; dragOnName.DragStarting += OnDragStarting; nameContainer.GestureRecognizers.Add(dragOnName);
            grid.Add(nameContainer, 2, 0);

            // status + checkbox
            var statusStack = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 4 };
            var check = new CheckBox { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            check.SetBinding(CheckBox.IsCheckedProperty, nameof(ItemVm.IsCompleted));
            check.CheckedChanged += async (s, e) =>
            {
                if (((BindableObject)s).BindingContext is ItemVm ir)
                {
                    var ok = await _db.SetItemCompletedAsync(ir.Id, e.Value);
                    if (!ok)
                    {
                        await DisplayAlert("Blocked", "Parent cannot be completed until all direct children are complete.", "OK");
                        await RefreshItemsAsync();
                        return;
                    }
                    ir.IsCompleted = e.Value; UpdateParentStates(ir); UpdateCompletedBadge();
                    if (_hideCompleted) RebuildVisibleItems();
                }
            };
            var partialIndicator = new Label { TextColor = Colors.Orange, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center, FontSize = 14, WidthRequest = 12 };
            partialIndicator.SetBinding(Label.TextProperty, nameof(ItemVm.PartialGlyph));
            statusStack.Add(check, 0, 0); statusStack.Add(partialIndicator, 1, 0);
            grid.Add(statusStack, 3, 0);

            // delete button
            var deleteBtn = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red, FontSize = 12, Padding = new Thickness(6, 2), MinimumWidthRequest = 52 };
            deleteBtn.Clicked += async (s, e) =>
            {
                if (((BindableObject)s).BindingContext is ItemVm ir)
                {
                    var listId = SelectedListId; if (listId == null) return;
                    var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
                    var result = await _db.DeleteItemAsync(ir.Id, expectedRevision);
                    if (result.Ok) { _allItems.Remove(ir); RebuildVisibleItems(); UpdateCompletedBadge(); }
                    else { await RefreshItemsAsync(); }
                }
            };
            grid.Add(deleteBtn, 4, 0);

            // Inline rename: toggle/save
            var renameBtn = new Button { Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 64 };
            renameBtn.SetBinding(Button.TextProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new BoolToStringConverter { TrueText = "Save", FalseText = "Rename" }));
            renameBtn.Clicked += async (s,e)=>
            {
                if (((BindableObject)s).BindingContext is ItemVm vm)
                {
                    if (!vm.IsRenaming)
                    {
                        vm.EditableName = vm.Name; vm.IsRenaming = true;
                    }
                    else
                    {
                        var newName = vm.EditableName?.Trim();
                        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) { vm.IsRenaming = false; return; }
                        try
                        {
                            var rev = await _db.GetListRevisionAsync(vm.ListId);
                            var res = await _db.RenameItemAsync(vm.Id, newName, rev);
                            if (!res.Ok) { await DisplayAlert("Rename", "Concurrency mismatch; items refreshed.", "OK"); await RefreshItemsAsync(); return; }
                            vm.Name = newName; vm.IsRenaming = false; // local update for immediate UI
                        }
                        catch (Exception ex)
                        { await DisplayAlert("Rename Failed", ex.Message, "OK"); await RefreshItemsAsync(); }
                    }
                }
            };
            grid.Add(renameBtn, 5, 0);

            var cancelBtn = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 56 };
            cancelBtn.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            cancelBtn.Clicked += (s,e)=>
            {
                if (((BindableObject)s).BindingContext is ItemVm vm)
                { vm.IsRenaming = false; vm.EditableName = vm.Name; }
            };
            grid.Add(cancelBtn, 6, 0);

            // Drop into (mid) stays on grid
            var midDrop = new DropGestureRecognizer();
            midDrop.DragOver += (s, e) => { grid.BackgroundColor = zoneHover; if (((BindableObject)card).BindingContext is ItemVm vm && vm.HasChildren && !vm.IsExpanded) { ScheduleHoverExpand(vm); } };
            midDrop.DragLeave += (s, e) => { grid.BackgroundColor = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm vm) CancelHoverExpand(vm); };
            midDrop.Drop += async (s, e) => { grid.BackgroundColor = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm target) { CancelHoverExpand(target.Id); await SafeHandleDropAsync(target, "into"); } };
            grid.GestureRecognizers.Add(midDrop);

            card.Content = grid;
            card.Style = (Style)Application.Current!.Resources["CardBorder"];
            card.BindingContextChanged += (s, e) =>
            {
                if (s is Border b)
                {
                    // Detach previous handler if any
                    if (b.GetValue(TrackedVmProperty) is ItemVm oldVm && b.GetValue(TrackedHandlerProperty) is PropertyChangedEventHandler oldHandler)
                    {
                        try { oldVm.PropertyChanged -= oldHandler; } catch { }
                    }
                    if (b.BindingContext is ItemVm vm)
                    {
                        applyStyle(vm);
                        PropertyChangedEventHandler handler = (sender, args) =>
                        {
                            if (args.PropertyName == nameof(ItemVm.IsDragging) || args.PropertyName == nameof(ItemVm.IsPreDrag) || args.PropertyName == nameof(ItemVm.IsSelected))
                            {
                                applyStyle(vm);
                            }
                        };
                        vm.PropertyChanged += handler;
                        b.SetValue(TrackedVmProperty, vm);
                        b.SetValue(TrackedHandlerProperty, handler);
                    }
                }
            };

            var pointer = new PointerGestureRecognizer(); card.GestureRecognizers.Add(pointer);
            pointer.PointerPressed += (s, e) =>
            {
                if (card.BindingContext is ItemVm vm)
                {
                    SetSingleSelection(vm);
                    _holdCts?.Cancel(); _holdCts = new CancellationTokenSource(); var token = _holdCts.Token; var localVm = vm;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(500, token);
                            if (token.IsCancellationRequested) return;
                            if (_selectedItem == localVm && !localVm.IsDragging)
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    localVm.IsDragging = true; _dragItem = localVm; _pendingDragVm = localVm; _dragDropCompleted = false;
                                });
                            }
                        }
                        catch { }
                    });
                }
            };
            pointer.PointerReleased += (s, e) => { _holdCts?.Cancel(); };
            var tap = new TapGestureRecognizer(); tap.Tapped += (s,e)=>{ /* selection handled by pointer */ }; card.GestureRecognizers.Add(tap);

            // Root layout combines external gaps and card
            var root = new VerticalStackLayout { Spacing = 0, Children = { card, bottomGap } };
            return root;
        });

        _itemsView = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.None,
            ItemTemplate = _itemViewTemplate
        };

        // Add move controls for accessibility
        _moveUpButton = new Button { Text = "Move Up", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveUpButton.Clicked += async (_, __) => await MoveSelectedAsync(-1);
        _moveDownButton = new Button { Text = "Move Down", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveDownButton.Clicked += async (_, __) => await MoveSelectedAsync(1);
        _resetSubtreeButton = new Button { Text = "Reset Subtree", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _resetSubtreeButton.Clicked += async (_, __) => await ResetSelectedSubtreeAsync();
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(400), () =>
        {
            UpdateMoveButtons();
            return true; // keep running
        });
        var itemsHeader = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Items", Style = (Style)Application.Current!.Resources["SectionTitle"] }, _moveUpButton, _moveDownButton, _resetSubtreeButton } };

        _itemsCard = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = 16,
                Children =
                {
                    itemsHeader,
                    _emptyFilteredLabel,
                    _itemsView,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newItemEntry, _addItemButton } },
                    new HorizontalStackLayout { Spacing = 8, Children = { _newChildEntry, _addChildButton } } // child creation row
                }
            }
        };
        _itemsCard.Style = (Style)Application.Current!.Resources["CardBorder"]; // after creation

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
                    new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Hide Completed" }, _hideCompletedSwitch } }
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

    private void ScheduleHoverExpand(ItemVm vm)
    {
        // FIX: use Id overload explicitly
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

    // Overload helpers so existing calls using ItemVm still work
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

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        _lists = await _db.GetListsAsync(_userId.Value);
        _listsObservable.Clear();
        foreach (var l in _lists) _listsObservable.Add(l);
        if (_lists.Count == 0)
        {
            _selectedListId = null; _items.Clear(); _allItems.Clear(); UpdateCompletedBadge();
            if (_listsView != null) _listsView.SelectedItem = null;
        }
        else
        {
            if (_selectedListId == null || !_lists.Any(x => x.Id == _selectedListId))
                _selectedListId = _lists.First().Id;
            if (_listsView != null)
            {
                var sel = _lists.FirstOrDefault(x => x.Id == _selectedListId);
                _listsView.SelectedItem = sel;
            }
            SyncDailyCheckboxWithSelectedList();
            await RefreshItemsAsync();
        }
        UpdateAllListVisuals();
    }

    private void SyncDailyCheckboxWithSelectedList()
    {
        var id = SelectedListId; _suppressDailyEvent = true;
        if (id == null) _dailyCheck.IsChecked = false; else { var lr = _lists.FirstOrDefault(x => x.Id == id); _dailyCheck.IsChecked = lr?.IsDaily ?? false; }
        _suppressDailyEvent = false;
    }

    private async Task OnDailyToggledAsync(bool isChecked)
    { if (_suppressDailyEvent) return; var id = SelectedListId; if (id == null) return; await _db.SetListDailyAsync(id.Value, isChecked); var lr = _lists.FirstOrDefault(x => x.Id == id.Value); if (lr != null) { var m = _lists.ToList(); var idx = m.FindIndex(x => x.Id == id.Value); if (idx >= 0) { m[idx] = new ListRecord(lr.Id, lr.Name, isChecked); _lists = m; } } }

    private async Task CreateListAsync()
    { if (_userId == null) return; var name = _newListEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked); _newListEntry.Text = string.Empty; _dailyCheck.IsChecked = false; await RefreshListsAsync(); }

    private async Task DeleteCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel"); if (!confirm) return; if (await _db.DeleteListAsync(id.Value)) { await RefreshListsAsync(); _items.Clear(); _allItems.Clear(); UpdateCompletedBadge(); } }

    private async Task ResetCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; await _db.ResetListAsync(id.Value); await RefreshItemsAsync(); UpdateCompletedBadge(); }

    private async Task RefreshItemsAsync()
    {
        if (_isRefreshing) return; _isRefreshing = true;
        _items.Clear(); _allItems.Clear(); var listId = SelectedListId; if (listId == null) { UpdateCompletedBadge(); _isRefreshing = false; UpdateChildControls(); return; }
        if (_userId != null)
            _expandedStates = (await _db.GetExpandedStatesAsync(_userId.Value, listId.Value)).ToDictionary(k => k.Key, v => v.Value);
        var items = await _db.GetItemsAsync(listId.Value);
        int? selectedId = _selectedItem?.Id;
        foreach (var i in items)
        {
            var isExpanded = _expandedStates.TryGetValue(i.Id, out var ex) ? ex : true;
            var vm = new ItemVm(i.Id, i.ListId, i.Name, i.IsCompleted, i.ParentItemId, i.HasChildren, i.ChildrenCount, i.IncompleteChildrenCount, i.Level, isExpanded, i.Order, i.SortKey);
            _allItems.Add(vm);
        }
        var byId = _allItems.ToDictionary(x => x.Id);
        foreach (var vm in _allItems)
        {
            if (vm.ParentId != null)
            {
                if (byId.TryGetValue(vm.ParentId.Value, out var p))
                {
                    p.Children.Add(vm);
                }
            }
        }
        foreach (var vm in _allItems) vm.RecalcState();
        _selectedItem = selectedId != null ? _allItems.FirstOrDefault(x => x.Id == selectedId.Value) : null;
        await LoadHideCompletedPreferenceForSelectedListAsync();
        RebuildVisibleItems(); UpdateCompletedBadge();
        _lastRevision = await _db.GetListRevisionAsync(listId.Value);
        _isRefreshing = false;
        UpdateChildControls();
#if WINDOWS
        RestorePageFocus();
#endif
    }

    private void RebuildVisibleItems()
    {
        _items.Clear(); var visible = new List<ItemVm>();
        foreach (var root in _allItems.Where(x => x.ParentId == null).OrderBy(x => x.SortKey))
        {
            if (_hideCompleted) AddWithDescendantsFiltered(root, visible);
            else AddWithDescendants(root, visible);
        }
        foreach (var v in visible) _items.Add(v);

        // Update placeholder visibility
        if (_emptyFilteredLabel != null)
        {
            _emptyFilteredLabel.IsVisible = _hideCompleted && visible.Count == 0 && _allItems.Any();
            if (_emptyFilteredLabel.IsVisible)
            {
                var hiddenCount = _allItems.Count(i => i.IsCompleted);
                _emptyFilteredLabel.Text = hiddenCount > 0
                    ? $"All {hiddenCount} items are completed and hidden."
                    : "All items are completed and hidden.";
            }
        }

        if (_selectedItem != null)
        {
            // If selected item is hidden by filter, clear selection
            if (!visible.Any(x => x.Id == _selectedItem.Id))
            {
                ClearSelectionAndUi();
            }
            else
            {
                var current = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id);
                if (current != null)
                    SetSingleSelection(current);
                else
                    ClearSelectionAndUi();
            }
        }
        else ClearSelectionAndUi();
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

    private async Task AddItemAsync()
    { var listId = SelectedListId; if (listId == null) return; var name = _newItemEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.AddItemAsync(listId.Value, name); _newItemEntry.Text = string.Empty; await RefreshItemsAsync(); }

    private async Task AddChildItemAsync()
    {
        if (_selectedItem == null) return; var listId = SelectedListId; if (listId == null) return; var name = _newChildEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return;
        if (_selectedItem.Level >= 3) { await DisplayAlert("Depth Limit", "Cannot add a child beyond level 3.", "OK"); return; }
        long expectedRevision = await _db.GetListRevisionAsync(listId.Value);
        try { await _db.AddChildItemAsync(listId.Value, name, _selectedItem.Id, expectedRevision); }
        catch (Exception ex) { await DisplayAlert("Add Child Failed", ex.Message, "OK"); return; }
        _newChildEntry.Text = string.Empty; await RefreshItemsAsync(); var parent = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id); if (parent != null) { parent.IsExpanded = true; RebuildVisibleItems(); }
        UpdateChildControls();
    }

    private void UpdateChildControls()
    {
        if (_newChildEntry == null || _addChildButton == null) return;
        if (_selectedItem == null) { _newChildEntry.IsVisible = false; _addChildButton.IsVisible = false; _addChildButton.IsEnabled = false; return; }
        bool canAdd = _selectedItem.Level < 3; _newChildEntry.IsVisible = canAdd; _addChildButton.IsVisible = canAdd; _addChildButton.IsEnabled = canAdd && !string.IsNullOrWhiteSpace(_newChildEntry.Text);
    }

    private void UpdateParentStates(ItemVm changed)
    { var current = changed; while (current.ParentId != null) { var parent = _allItems.FirstOrDefault(x => x.Id == current.ParentId.Value); if (parent == null) break; parent.RecalcState(); current = parent; } for (int i = 0; i < _items.Count; i++) _items[i].RecalcState(); }

    private void UpdateCompletedBadge()
    { if (_allItems.Count == 0) { _completedBadge.IsVisible = false; return; } _completedBadge.IsVisible = _allItems.All(i => i.IsCompleted); }

    private Button? _moveUpButton; private Button? _moveDownButton; private Button? _resetSubtreeButton;
    private void UpdateMoveButtons()
    {
        if (_moveUpButton == null || _moveDownButton == null || _resetSubtreeButton == null) return;
        bool hasSelection = _selectedItem != null;
        _moveUpButton.IsEnabled = hasSelection;
        _moveDownButton.IsEnabled = hasSelection;
        _resetSubtreeButton.IsEnabled = hasSelection;
        UpdateChildControls();
    }

    private async Task MoveSelectedAsync(int delta)
    {
        if (_selectedItem == null) return; var item = _selectedItem; var listId = SelectedListId; if (listId == null) return;
        var siblings = _allItems.Where(x => x.ParentId == item.ParentId).OrderBy(x => x.Order).ThenBy(x => x.Id).ToList(); var idx = siblings.FindIndex(x => x.Id == item.Id); if (idx < 0) return;
        var newIdx = idx + delta; if (newIdx < 0 || newIdx >= siblings.Count) return; var reordered = siblings.Where(x => x.Id != item.Id).ToList(); reordered.Insert(newIdx, item);
        ItemVm? prev = newIdx - 1 >= 0 ? reordered[newIdx - 1] : null; ItemVm? next = newIdx + 1 < reordered.Count ? reordered[newIdx + 1] : null; int newOrder = ComputeBetweenOrder(prev?.Order, next?.Order);
        long expectedRevision = await _db.GetListRevisionAsync(listId.Value); var ordered = await _db.SetItemOrderAsync(item.Id, newOrder, expectedRevision); if (!ordered.Ok) { await RefreshItemsAsync(); return; }
        await RefreshItemsAsync(); _selectedItem = _allItems.FirstOrDefault(x => x.Id == item.Id); if (_selectedItem != null) SetSingleSelection(_selectedItem);
    }

    private async Task ResetSelectedSubtreeAsync()
    {
        if (_selectedItem == null) return; var listId = SelectedListId; if (listId == null) return;
        var confirm = await DisplayAlert("Reset Subtree", $"Reset '{_selectedItem.Name}' and all its descendants to incomplete?", "Reset", "Cancel");
        if (!confirm) return;
        try
        {
            var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
            var (ok, newRev, affected) = await _db.ResetSubtreeAsync(_selectedItem.Id, expectedRevision);
            if (!ok)
            {
                await DisplayAlert("Concurrency", "List changed; items refreshed.", "OK");
                await RefreshItemsAsync();
                return;
            }
            await RefreshItemsAsync();
        }
        catch (Exception ex)
        { await DisplayAlert("Reset Failed", ex.Message, "OK"); }
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
                        // Navigate to parent if exists
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
                        // Move into first child
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

    // Attached properties for recycled item view tracking (ensure style refresh correctly)
    private static readonly BindableProperty TrackedVmProperty = BindableProperty.CreateAttached("TrackedVm", typeof(ItemVm), typeof(DashboardPage), null);
    private static readonly BindableProperty TrackedHandlerProperty = BindableProperty.CreateAttached("TrackedHandler", typeof(PropertyChangedEventHandler), typeof(DashboardPage), null);

    // Single-selection helpers
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

    // Converters for inline rename (added if missing)
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

    private async Task LoadHideCompletedPreferenceForSelectedListAsync()
    {
        var listId = SelectedListId; if (listId == null || _userId == null) return;
        bool? server = null;
        try { server = await _db.GetListHideCompletedAsync(_userId.Value, listId.Value); } catch { }
        bool value = server ?? Preferences.Get($"LIST_HIDE_COMPLETED_{listId.Value}", false);
        _suppressHideCompletedEvent = true;
        _hideCompleted = value;
        if (_hideCompletedSwitch != null) _hideCompletedSwitch.IsToggled = value;
        _suppressHideCompletedEvent = false;
    }

    private async Task OnHideCompletedToggledAsync(bool hide)
    {
        if (_suppressHideCompletedEvent) return;
        _hideCompleted = hide;
        var listId = SelectedListId;
        if (listId != null)
        {
            Preferences.Set($"LIST_HIDE_COMPLETED_{listId.Value}", hide);
            if (_userId != null)
            {
                try { await _db.SetListHideCompletedAsync(_userId.Value, listId.Value, hide); } catch { }
            }
        }
        RebuildVisibleItems();
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
            // Use new constructor with db and user
            var page = new ShareListPage(_db, lr, _userId.Value);
            await Navigation.PushModalAsync(page);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Share", $"Unable to open share dialog: {ex.Message}", "OK");
        }
    }
} // end DashboardPage class
