using System.Collections.ObjectModel;
using Donezo.Services;
using Microsoft.Maui.Controls.Shapes;
using System.Linq;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;
using Microsoft.Maui;
using System.ComponentModel;

namespace Donezo.Pages;

public partial class DashboardPage
{
    private bool _pendingUiSync;
    private bool _suppressListRevisionCheck;

    // Helper moved from main partial: returns true if candidate is within descendant chain of parent
    private bool IsUnder(ItemVm parent, ItemVm candidate)
    {
        if (parent == null || candidate == null) return false;
        if (parent.Id == candidate.Id) return false;
        var current = candidate; int guard = 0;
        while (current.ParentId != null && guard++ < 256)
        {
            var p = _allItems.FirstOrDefault(x => x.Id == current.ParentId);
            if (p == null) break;
            if (p.Id == parent.Id) return true;
            current = p;
        }
        return false;
    }

    // Canonical RebuildVisibleItems lives here. Ensure no other partial defines it.
    // (No code changes needed beyond comment clarification.)
    private void RebuildVisibleItems()
    {
        Trace("RebuildVisibleItems begin");
        try
        {
            var visible = new List<ItemVm>();
            var roots = _allItems.Where(x => x.ParentId == null).OrderBy(x => x.SortKey).ToList();
            Trace($"RebuildVisibleItems roots={roots.Count}");
            foreach (var root in roots)
            {
                if (_hideCompleted) TraceAddFiltered(root, visible);
                else TraceAdd(root, visible);
            }
            Trace($"RebuildVisibleItems visible after traversal={visible.Count}");
            // Replace observable rather than Clear/Add for performance
            if (_items.Count != visible.Count || !_items.SequenceEqual(visible))
            {
                Trace($"RebuildVisibleItems diff apply oldCount={_items.Count} newCount={visible.Count}");
                _items = new ObservableCollection<ItemVm>(visible);
                if (_itemsView != null) _itemsView.ItemsSource = _items;
            }
            if (_emptyFilteredLabel != null)
            {
                _emptyFilteredLabel.IsVisible = _hideCompleted && visible.Count == 0 && _allItems.Any();
                if (_emptyFilteredLabel.IsVisible)
                {
                    var hiddenCount = _allItems.Count(i => i.IsCompleted);
                    _emptyFilteredLabel.Text = hiddenCount > 0 ? $"All {hiddenCount} items are completed and hidden." : "All items are completed and hidden.";
                }
            }
            if (_selectedItem != null)
            {
                if (!visible.Any(x => x.Id == _selectedItem.Id)) { Trace("RebuildVisibleItems selected item not visible; clearing"); ClearSelectionAndUi(); }
                else { var current = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id); if (current != null) { SetSingleSelection(current); } else { ClearSelectionAndUi(); } }
            }
            RefreshItemCardStyles();
            Trace("RebuildVisibleItems end success");
        }
        catch (Exception ex)
        {
            Trace($"RebuildVisibleItems exception {ex.Message}");
        }
    }

    private async Task LoadHideCompletedPreferenceForSelectedListAsync()
    {
        // No-op if user/list context missing
        if (_userId == null || SelectedListId == null) return;
        try
        {
            var pref = await _db.GetListHideCompletedAsync(_userId.Value, SelectedListId.Value);
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
        if (_userId != null && SelectedListId != null)
        {
            try { await _db.SetListHideCompletedAsync(_userId.Value, SelectedListId.Value, value); } catch { }
        }
        RebuildVisibleItems();
    }

    // Attached properties used to track VM and handler for item card borders
    private static readonly BindableProperty TrackedVmProperty = BindableProperty.CreateAttached("TrackedVm", typeof(ItemVm), typeof(DashboardPage), null);
    private static readonly BindableProperty TrackedHandlerProperty = BindableProperty.CreateAttached("TrackedHandler", typeof(PropertyChangedEventHandler), typeof(DashboardPage), null);

    private Border BuildItemsCard()
    {
        _emptyFilteredLabel ??= new Label { IsVisible = false, TextColor = Colors.Gray, FontSize = 12 };
        _newItemEntry = new Entry { Placeholder = "New item name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _newItemEntry.TextChanged += (s, e) => { if (_addItemButton != null) _addItemButton.IsEnabled = CanAddItems() && !string.IsNullOrWhiteSpace(_newItemEntry.Text); };
        _addItemButton = new Button { Text = "Add", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _addItemButton.Clicked += async (_, __) => { if (!CanAddItems()) { await ShowViewerBlockedAsync("adding items"); return; } await AddItemAsync(); };
        _newChildEntry = new Entry { Placeholder = "New child name", Style = (Style)Application.Current!.Resources["FilledEntry"], IsVisible = false };
        _newChildEntry.TextChanged += (s, e) => UpdateChildControls();
        _addChildButton = new Button { Text = "Add Child", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false, IsVisible = false };
        _addChildButton.Clicked += async (_, __) => { if (!CanAddItems()) { await ShowViewerBlockedAsync("adding child items"); return; } await AddChildItemAsync(); };
        _itemViewTemplate = CreateItemTemplate();
        _itemsView = new CollectionView { ItemsSource = _items, SelectionMode = SelectionMode.None, ItemTemplate = _itemViewTemplate };
        _moveUpButton = new Button { Text = "Move Up", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveUpButton.Clicked += async (_, __) => { if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } await MoveSelectedAsync(-1); };
        _moveDownButton = new Button { Text = "Move Down", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveDownButton.Clicked += async (_, __) => { if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } await MoveSelectedAsync(1); };
        _resetSubtreeButton = new Button { Text = "Reset Subtree", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _resetSubtreeButton.Clicked += async (_, __) => { if (!CanResetSubtree()) { await ShowViewerBlockedAsync("resetting subtree"); return; } await ResetSelectedSubtreeAsync(); };
        _hideCompletedSwitch = new Switch { IsToggled = _hideCompleted };
        _hideCompletedSwitch.Toggled += async (s, e) => await OnHideCompletedToggledAsync(e.Value);
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(400), () => { UpdateMoveButtons(); return true; });
        var itemsHeader = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Items", Style = (Style)Application.Current!.Resources["SectionTitle"] }, _moveUpButton, _moveDownButton, _resetSubtreeButton } };
        var filterRow = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Hide Completed" }, _hideCompletedSwitch } };
        var card = new Border
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
                    filterRow,
                    _emptyFilteredLabel,
                    _itemsView,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newItemEntry, _addItemButton } },
                    new HorizontalStackLayout { Spacing = 8, Children = { _newChildEntry, _addChildButton } }
                }
            }
        };
        card.Style = (Style)Application.Current!.Resources["CardBorder"];
        return card;
    }

    private DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var card = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }, Padding = new Thickness(0) };
            card.SetBinding(Border.OpacityProperty, new Binding(nameof(ItemVm.IsDragging), converter: new BoolToOpacityConverter()));
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
            var bottomGap = new Border { HeightRequest = 4, BackgroundColor = Colors.Transparent, StrokeThickness = 1, Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Padding = 0, Margin = new Thickness(0, 2, 0, 0) };
            var bottomDrop = new DropGestureRecognizer();
            bottomDrop.DragOver += (_, __) => { if (_dragItem == null || !CanReorderItems()) return; bottomGap.HeightRequest = GetInsertionGapHeight(); bottomGap.BackgroundColor = zoneHover; bottomGap.Stroke = (Color)Application.Current!.Resources["Primary"]; };
            bottomDrop.DragLeave += (_, __) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; };
            bottomDrop.Drop += async (s, e) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } if (((BindableObject)card).BindingContext is ItemVm target) await SafeHandleDropAsync(target, "below"); };
            bottomGap.GestureRecognizers.Add(bottomDrop);
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
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            var badge = new Label { Margin = new Thickness(4, 0), VerticalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold };
            badge.SetBinding(Label.TextProperty, new Binding(".", converter: new LevelBadgeConverter()));
            badge.SetBinding(Label.TextColorProperty, new Binding(".", converter: new LevelAccentColorConverter()));
            grid.Add(badge, 0, 0);
            void OnDragStarting(object? s, DragStartingEventArgs e)
            {
                if (!CanDragItems()) { return; }
                if (((BindableObject)card).BindingContext is ItemVm vm)
                {
                    _holdCts?.Cancel();
                    vm.IsPreDrag = false; vm.IsDragging = true; ApplyItemCardStyle(card, vm);
                    _dragItem = vm; _pendingDragVm = vm; _dragDropCompleted = false;
                    try { e.Data.Properties["ItemId"] = vm.Id; } catch { }
                }
            }
            var drag = new DragGestureRecognizer { CanDrag = true }; drag.DragStarting += OnDragStarting; card.GestureRecognizers.Add(drag);
            var dragOnGrid = new DragGestureRecognizer { CanDrag = true }; dragOnGrid.DragStarting += OnDragStarting; grid.GestureRecognizers.Add(dragOnGrid);
            var expandIcon = new Label { Text = ">", WidthRequest = 28, HeightRequest = 28, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
            expandIcon.SetBinding(Label.RotationProperty, new Binding(nameof(ItemVm.IsExpanded), converter: new BoolToRotationConverter()));
            expandIcon.SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(ItemVm.IsExpanded), converter: new BoolToAccessibleExpandNameConverter()));
            expandIcon.SetBinding(View.IsVisibleProperty, new Binding(nameof(ItemVm.HasChildren)));
            var expandTap = new TapGestureRecognizer();
            expandTap.Tapped += async (s, e) =>
            {
                if (((BindableObject)s).BindingContext is ItemVm vm)
                {
                    if (!vm.HasChildren) return;
                    bool collapsing = vm.IsExpanded;
                    vm.IsExpanded = !vm.IsExpanded;
                    if (collapsing && _selectedItem != null && IsUnder(vm, _selectedItem)) { SetSingleSelection(vm); }
                    else { if (_selectedItem != null) { var current = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id); if (current != null) SetSingleSelection(current); else ClearSelectionAndUi(); } else ClearSelectionAndUi(); }
                    if (_userId != null) await _db.SetItemExpandedAsync(_userId.Value, vm.Id, vm.IsExpanded);
                    RebuildVisibleItems();
                }
            };
            expandIcon.GestureRecognizers.Add(expandTap);
            grid.Add(expandIcon, 1, 0);
            var nameContainer = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
            var nameLabel = new Label { VerticalTextAlignment = TextAlignment.Center };
            nameLabel.SetBinding(Label.TextProperty, nameof(ItemVm.Name));
            nameLabel.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameLabel.SetBinding(View.IsVisibleProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new InvertBoolConverter()));
            var userLabel = new Label { VerticalTextAlignment = TextAlignment.Center, FontSize = 12, FontAttributes = FontAttributes.Italic };
            userLabel.SetBinding(Label.TextProperty, new Binding("CompletedByUsername"));
            userLabel.SetBinding(Label.TextColorProperty, new Binding(nameof(ItemVm.IsCompleted), converter: new BoolToStringConverter { TrueText = "#008A2E", FalseText = "#008A2E" }));
            userLabel.SetBinding(Label.IsVisibleProperty, new Binding("ShowCompletedUser"));
            var nameEntry = new Entry { HeightRequest = 32, FontSize = 14 };
            nameEntry.SetBinding(Entry.TextProperty, nameof(ItemVm.EditableName), BindingMode.TwoWay);
            nameEntry.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameEntry.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            nameContainer.Children.Add(nameLabel); nameContainer.Children.Add(userLabel); nameContainer.Children.Add(nameEntry);
            var dragOnName = new DragGestureRecognizer { CanDrag = true }; dragOnName.DragStarting += OnDragStarting; nameContainer.GestureRecognizers.Add(dragOnName);
            grid.Add(nameContainer, 2, 0);
            var statusStack = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 4 };
            var check = new CheckBox { HorizontalOptions = LayoutOptions.Center };
            check.SetBinding(CheckBox.IsCheckedProperty, nameof(ItemVm.IsCompleted));
            // Updated handler with suppression logic to avoid repeated alerts
            check.CheckedChanged += async (s, e) =>
            {
                if (_suppressCompletionEvent) return; // global suppression guard
                if (!CanCompleteItems())
                {
                    try
                    {
                        _suppressCompletionEvent = true;
                        if (s is CheckBox cb && cb.BindingContext is ItemVm vm2)
                        {
                            // Revert model & UI to prior state (assume prior = !e.Value)
                            var prior = !e.Value;
                            vm2.IsCompleted = prior; // revert underlying model
                            cb.IsChecked = prior;     // ensure UI matches without triggering loop (suppressed)
                        }
                        await ShowViewerBlockedAsync("changing completion state");
                    }
                    finally { _suppressCompletionEvent = false; }
                    return;
                }
                if (((BindableObject)s).BindingContext is ItemVm ir)
                {
                    await ToggleItemCompletionInlineAsync(ir, e.Value);
                }
            };
            check.IsEnabled = CanCompleteItems();
            var partialIndicator = new Label { TextColor = Colors.Orange, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center, FontSize = 14, WidthRequest = 12 };
            partialIndicator.SetBinding(Label.TextProperty, nameof(ItemVm.PartialGlyph));
            statusStack.Add(check, 0, 0); statusStack.Add(partialIndicator, 1, 0);
            grid.Add(statusStack, 3, 0);
            var deleteBtn = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red, FontSize = 12, Padding = new Thickness(6, 2), MinimumWidthRequest = 52 };
            deleteBtn.Clicked += async (s, e) =>
            {
                if (!CanDeleteItems()) { await ShowViewerBlockedAsync("deleting items"); return; }
                if (((BindableObject)s).BindingContext is ItemVm ir)
                {
                    var listId = SelectedListId; if (listId == null) return;
                    var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
                    var result = await _db.DeleteItemAsync(ir.Id, expectedRevision);
                    if (result.Ok) { _allItems.Remove(ir); RebuildVisibleItems(); UpdateCompletedBadge(); }
                    else { await RefreshItemsAsync(); }
                }
            };
            deleteBtn.IsEnabled = CanDeleteItems();
            grid.Add(deleteBtn, 4, 0);
            var renameBtn = new Button { Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 64 };
            renameBtn.SetBinding(Button.TextProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new BoolToStringConverter { TrueText = "Save", FalseText = "Rename" }));
            renameBtn.Clicked += async (s,e)=>
            {
                if (!CanRenameItems()) { await ShowViewerBlockedAsync("renaming items"); return; }
                if (((BindableObject)s).BindingContext is ItemVm vm)
                {
                    if (!vm.IsRenaming) { vm.EditableName = vm.Name; vm.IsRenaming = true; }
                    else
                    {
                        var newName = vm.EditableName?.Trim();
                        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) { vm.IsRenaming = false; return; }
                        try
                        {
                            var rev = await _db.GetListRevisionAsync(vm.ListId);
                            var res = await _db.RenameItemAsync(vm.Id, newName, rev);
                            if (!res.Ok) { await DisplayAlert("Rename", "Concurrency mismatch; items refreshed.", "OK"); await RefreshItemsAsync(); return; }
                            vm.Name = newName; vm.IsRenaming = false;
                        }
                        catch (Exception ex) { await DisplayAlert("Rename Failed", ex.Message, "OK"); await RefreshItemsAsync(); }
                    }
                }
            };
            renameBtn.IsEnabled = CanRenameItems();
            grid.Add(renameBtn, 5, 0);
            var cancelBtn = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 56 };
            cancelBtn.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            cancelBtn.Clicked += (s,e)=> { if (((BindableObject)s).BindingContext is ItemVm vm) { vm.IsRenaming = false; vm.EditableName = vm.Name; } };
            grid.Add(cancelBtn, 6, 0);
            var midDrop = new DropGestureRecognizer();
            midDrop.DragOver += (s, e) => { if (!CanReorderItems()) return; grid.BackgroundColor = zoneHover; if (((BindableObject)card).BindingContext is ItemVm vm && vm.HasChildren && !vm.IsExpanded) { ScheduleHoverExpand(vm); } };
            midDrop.DragLeave += (s, e) => { grid.BackgroundColor = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm vm) CancelHoverExpand(vm); };
            midDrop.Drop += async (s, e) => { grid.BackgroundColor = Colors.Transparent; if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } if (((BindableObject)card).BindingContext is ItemVm target) { CancelHoverExpand(target.Id); await SafeHandleDropAsync(target, "into"); } };
            grid.GestureRecognizers.Add(midDrop);
            card.Content = grid; card.Style = (Style)Application.Current!.Resources["CardBorder"];
            card.BindingContextChanged += (s, e) =>
            {
                if (s is Border b)
                {
                    if (!_itemCardBorders.Contains(b)) _itemCardBorders.Add(b);
                    if (b.GetValue(TrackedVmProperty) is ItemVm oldVm && b.GetValue(TrackedHandlerProperty) is PropertyChangedEventHandler oldHandler) { try { oldVm.PropertyChanged -= oldHandler; } catch { } }
                    if (b.BindingContext is ItemVm vm)
                    {
                        ApplyItemCardStyle(b, vm);
                        PropertyChangedEventHandler handler = (sender, args) =>
                        {
                            if (args.PropertyName == nameof(ItemVm.IsDragging) || args.PropertyName == nameof(ItemVm.IsPreDrag) || args.PropertyName == nameof(ItemVm.IsSelected)) { ApplyItemCardStyle(b, vm); }
                        };
                        vm.PropertyChanged += handler; b.SetValue(TrackedVmProperty, vm); b.SetValue(TrackedHandlerProperty, handler);
                      }
                    // Apply enabled/visible states based on current role whenever binding context changes
                    renameBtn.IsEnabled = CanRenameItems();
                    deleteBtn.IsEnabled = CanDeleteItems();
                    check.IsEnabled = CanCompleteItems();
                }
            };
            var pointer = new PointerGestureRecognizer(); card.GestureRecognizers.Add(pointer);
            pointer.PointerPressed += (s, e) =>
            {
                if (!CanDragItems()) { if (card.BindingContext is ItemVm vmBlocked) { SetSingleSelection(vmBlocked); } return; }
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
                            if (_selectedItem == localVm && !localVm.IsDragging && CanDragItems())
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                { localVm.IsDragging = true; _dragItem = localVm; _pendingDragVm = localVm; _dragDropCompleted = false; });
                            }
                        }
                        catch { }
                    });
                }
            };
            pointer.PointerReleased += (s, e) => { _holdCts?.Cancel(); };
            var tap = new TapGestureRecognizer(); tap.Tapped += (s,e)=>{ }; card.GestureRecognizers.Add(tap);
            var root = new VerticalStackLayout { Spacing = 0, Children = { card, bottomGap } };
            return root;
        });
    }

    // Instrumented recursive helpers (renamed to avoid duplicate clash)
    private void TraceAdd(ItemVm node, List<ItemVm> target, HashSet<int>? visited = null, int depth = 0)
    {
        visited ??= new HashSet<int>();
        if (node == null) return;
        if (!visited.Add(node.Id)) { Trace($"TraceAdd cycle skip id={node.Id}"); return; }
        if (depth > 64) { Trace($"TraceAdd depth cap id={node.Id}"); return; }
        target.Add(node);
        if (!node.IsExpanded) { Trace($"TraceAdd not expanded id={node.Id}"); return; }
        foreach (var child in node.Children.OrderBy(c => c.SortKey))
            TraceAdd(child, target, visited, depth + 1);
    }
    private void TraceAddFiltered(ItemVm node, List<ItemVm> target, HashSet<int>? visited = null, int depth = 0)
    {
        visited ??= new HashSet<int>();
        if (node == null) return;
        if (!visited.Add(node.Id)) { Trace($"TraceAddFiltered cycle skip id={node.Id}"); return; }
        if (depth > 64) { Trace($"TraceAddFiltered depth cap id={node.Id}"); return; }
        if (_hideCompleted && node.IsCompleted) { Trace($"TraceAddFiltered skip completed id={node.Id}"); return; }
        target.Add(node);
        if (!node.IsExpanded) { Trace($"TraceAddFiltered not expanded id={node.Id}"); return; }
        foreach (var child in node.Children.OrderBy(c => c.SortKey))
            TraceAddFiltered(child, target, visited, depth + 1);
    }

    private DateTime _lastItemsRefreshStartUtc = DateTime.MinValue;
    private DateTime _lastItemsRefreshEndUtc = DateTime.MinValue;
    private static readonly TimeSpan ItemsRefreshMinInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ItemsAutoRefreshCooldown = TimeSpan.FromSeconds(2);

    private async Task RefreshItemsAsync(bool userInitiated = false)
    {
        var now = DateTime.UtcNow;
        if (_isRefreshing)
        {
            Trace("RefreshItemsAsync skipped: already refreshing");
            return;
        }
        if (!userInitiated && (now - _lastItemsRefreshEndUtc) < ItemsAutoRefreshCooldown)
        {
            Trace($"RefreshItemsAsync skipped: auto cooldown {(now - _lastItemsRefreshEndUtc).TotalMilliseconds:F0}ms");
            return;
        }
        if ((now - _lastItemsRefreshStartUtc) < ItemsRefreshMinInterval)
        {
            Trace($"RefreshItemsAsync skipped: short interval {(now - _lastItemsRefreshStartUtc).TotalMilliseconds:F0}ms");
            return;
        }
        _lastItemsRefreshStartUtc = now;
        _isRefreshing = true;
        _suppressListRevisionCheck = true; // pause revision polling during active refresh
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var listId = SelectedListId;
            if (listId == null)
            {
                Trace("RefreshItemsAsync abort: no SelectedListId");
                _items.Clear(); _allItems.Clear(); UpdateCompletedBadge(); UpdateChildControls(); return; }

            Trace($"RefreshItemsAsync begin listId={listId} userInitiated={userInitiated}");
            _allItems.Clear();

            async Task<T> WithTimeout<T>(Func<Task<T>> op, string name, int ms = 3000)
            {
                var cts = new CancellationTokenSource(ms);
                var task = op();
                var completed = await Task.WhenAny(task, Task.Delay(ms, cts.Token));
                if (completed != task)
                {
                    Trace($"RefreshItemsAsync timeout waiting for {name} after {ms}ms");
                    return default!; }
                var result = await task;
                Trace($"RefreshItemsAsync step {name} ok");
                return result;
            }

            if (_userId != null)
            {
                try
                {
                    var states = await WithTimeout(async () => await _db.GetExpandedStatesAsync(_userId.Value, listId.Value), "GetExpandedStates");
                    if (states != null)
                        _expandedStates = states.ToDictionary(k => k.Key, v => v.Value);
                }
                catch (Exception ex) { Trace($"RefreshItemsAsync GetExpandedStates error: {ex.Message}"); }
            }

            IReadOnlyList<ItemRecord>? items = null;
            try { items = await WithTimeout(async () => await _db.GetItemsAsync(listId.Value), "GetItems", 5000); }
            catch (Exception ex) { Trace($"RefreshItemsAsync GetItems error: {ex.Message}"); }
            if (items == null)
            { Trace("RefreshItemsAsync items null - abort"); UpdateCompletedBadge(); UpdateChildControls(); return; }

            int? selectedId = _selectedItem?.Id;
            foreach (var i in items)
            {
                var isExpanded = _expandedStates.TryGetValue(i.Id, out var ex) ? ex : true;
                var vm = new ItemVm(i.Id, i.ListId, i.Name, i.IsCompleted, i.ParentItemId, i.HasChildren, i.ChildrenCount, i.IncompleteChildrenCount, i.Level, isExpanded, i.Order, i.SortKey)
                { CompletedByUsername = i.CompletedByUsername };
                _allItems.Add(vm);
            }
            Trace($"RefreshItemsAsync populated vm count={_allItems.Count}");

            var byId = _allItems.ToDictionary(x => x.Id);
            foreach (var vm in _allItems)
            {
                if (vm.ParentId != null && byId.TryGetValue(vm.ParentId.Value, out var p)) p.Children.Add(vm);
            }
            foreach (var vm in _allItems) vm.RecalcState();
            _selectedItem = selectedId != null ? _allItems.FirstOrDefault(x => x.Id == selectedId.Value) : null;

            try { await LoadHideCompletedPreferenceForSelectedListAsync(); Trace("RefreshItemsAsync HideCompletedPref loaded"); }
            catch (Exception ex) { Trace($"RefreshItemsAsync HideCompletedPref error: {ex.Message}"); }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                RebuildVisibleItems();
                UpdateCompletedBadge();
            });
            Trace("RefreshItemsAsync post rebuild badge updated");

            // Only fetch revision if user initiated OR last revision fetch older than cooldown window
            if (userInitiated || (DateTime.UtcNow - _lastItemsRefreshEndUtc) > ItemsAutoRefreshCooldown)
            {
                try
                {
                    var rev = await WithTimeout(async () => await _db.GetListRevisionAsync(listId.Value), "GetListRevision", 3000);
                    _lastRevision = rev;
                }
                catch (Exception ex) { Trace($"RefreshItemsAsync GetListRevision error: {ex.Message}"); }
            }
        }
        finally
        {
            _isRefreshing = false;
            _suppressListRevisionCheck = false;
            _lastItemsRefreshEndUtc = DateTime.UtcNow;
            UpdateChildControls();
            swTotal.Stop();
            Trace($"RefreshItemsAsync end elapsed={swTotal.ElapsedMilliseconds}ms");
#if WINDOWS
            RestorePageFocus();
#endif
            RefreshItemCardStyles();
        }
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
        bool canAdd = _selectedItem.Level < 3 && CanAddItems(); _newChildEntry.IsVisible = canAdd; _addChildButton.IsVisible = canAdd; _addChildButton.IsEnabled = canAdd && !string.IsNullOrWhiteSpace(_newChildEntry.Text);
    }

    private void UpdateMoveButtons()
    {
        if (_moveUpButton == null || _moveDownButton == null || _resetSubtreeButton == null) return;
        bool hasSelection = _selectedItem != null; bool canReorder = hasSelection && CanReorderItems();
        _moveUpButton.IsEnabled = canReorder; _moveDownButton.IsEnabled = canReorder; _resetSubtreeButton.IsEnabled = hasSelection && CanResetSubtree(); UpdateChildControls();
    }

    private async Task MoveSelectedAsync(int delta)
    {
        if (_selectedItem == null) return;
        var listId = SelectedListId; if (listId == null) return;
        var siblings = _allItems.Where(x => x.ParentId == _selectedItem.ParentId).OrderBy(x => x.Order).ThenBy(x => x.Id).ToList();
        var idx = siblings.FindIndex(x => x.Id == _selectedItem.Id); if (idx < 0) return;
        var targetIdx = idx + delta; if (targetIdx < 0 || targetIdx >= siblings.Count) return; // out of bounds
        // Determine new order relative to adjacent sibling; simple heuristic then server will re-space.
        int newOrder;
        if (delta < 0)
        {
            var prevSibling = siblings[targetIdx]; // item we will appear before after move
            newOrder = prevSibling.Order - 1; // position before prevSibling
        }
        else
        {
            var nextSibling = siblings[targetIdx]; // item we will appear after after move
            newOrder = nextSibling.Order + 1; // position after nextSibling
        }
        try
        {
            var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
            var result = await _db.SetItemOrderAsync(_selectedItem.Id, newOrder, expectedRevision);
            if (!result.Ok)
            {
                await RefreshItemsAsync();
                return;
            }
            await RefreshItemsAsync();
            // Restore selection
            var sel = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id);
            if (sel != null) SetSingleSelection(sel);
        }
        catch { await RefreshItemsAsync(); }
    }

    private async Task ResetSelectedSubtreeAsync()
    {
        if (_selectedItem == null) return; var listId = SelectedListId; if (listId == null) return;
        try
        {
            var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
            var (ok, _, affected) = await _db.ResetSubtreeAsync(_selectedItem.Id, expectedRevision);
            if (!ok)
            {
                await DisplayAlert("Reset", "Concurrency mismatch; items refreshed.", "OK");
                await RefreshItemsAsync();
                return;
            }
            await RefreshItemsAsync();
            UpdateCompletedBadge();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Reset Failed", ex.Message, "OK");
            await RefreshItemsAsync();
        }
    }

    private async Task ToggleItemCompletionInlineAsync(ItemVm vm, bool completed)
    {
        if (SelectedListId == null) return;
        if (_userId == null) { await DisplayAlert("Completion", "User context missing.", "OK"); return; }
        try
        {
            var ok = await _db.SetItemCompletedByUserAsync(vm.Id, _userId.Value, completed);
            if (!ok)
            {
                // revert visual state
                vm.IsCompleted = !completed;
                await DisplayAlert("Completion", "Cannot complete item yet (children incomplete)", "OK");
                return;
            }
            vm.IsCompleted = completed;
            // Refresh overall states to update parents/partials
            await RefreshItemsAsync();
            UpdateCompletedBadge();
        }
        catch (Exception ex)
        {
            vm.IsCompleted = !completed;
            await DisplayAlert("Completion Error", ex.Message, "OK");
            await RefreshItemsAsync();
        }
    }
}
