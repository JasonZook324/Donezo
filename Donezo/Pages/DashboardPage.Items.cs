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
    // State flags
    private bool _pendingUiSync; private bool _suppressListRevisionCheck;

    // Track the VM bound to a Border so we can detach/ re-attach handlers safely
    private static readonly BindableProperty ItemVmTrackerProperty = BindableProperty.CreateAttached(
        "ItemVmTracker", typeof(ItemVm), typeof(DashboardPage), null);

    private void OnItemVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ItemVm vm) return;
        if (e.PropertyName != nameof(ItemVm.IsSelected) && e.PropertyName != nameof(ItemVm.IsDragging)) return;
        // Update all borders currently bound to this VM
        foreach (var b in _itemCardBorders.ToList())
        {
            if (b.BindingContext == vm)
                ApplyItemCardStyle(b, vm);
        }
    }

    // Helper to invoke per-item menu actions
    private async Task ShowItemMenuAsync(ItemVm vm, VisualElement anchor)
    {
        // Compute availability
        bool canReorder = CanReorderItems();
        var siblings = _allItems.Where(x => x.ParentId == vm.ParentId).OrderBy(x => x.Order).ThenBy(x => x.Id).ToList();
        int idx = siblings.FindIndex(x => x.Id == vm.Id);
        bool isTop = idx <= 0;
        bool isBottom = idx >= siblings.Count - 1;
        bool hasSubtree = vm.HasChildren || vm.ChildrenCount > 0;

        var cancel = vm.IsRenaming ? "Cancel" : null;
        var actions = new List<string>();
        actions.Add(vm.IsRenaming ? "Save" : "Rename");
        actions.Add("Delete");
        if (canReorder && !isTop) actions.Add("Move Up");
        if (canReorder && !isBottom) actions.Add("Move Down");
        if (CanResetSubtree() && hasSubtree) actions.Add("Reset Subtree");

        string? choice = null;
        try { choice = await DisplayActionSheet("Item", cancel, null, actions.ToArray()); } catch { }
        if (string.IsNullOrEmpty(choice)) return;

        if (choice == "Delete")
        {
            if (!CanDeleteItems()) { await ShowViewerBlockedAsync("deleting items"); return; }
            await DeleteItemInlineAsync(vm);
            return;
        }
        if (choice == "Rename" || choice == "Save")
        {
            if (!CanRenameItems()) { await ShowViewerBlockedAsync("renaming items"); return; }
            if (!vm.IsRenaming)
            {
                vm.EditableName = vm.Name; vm.IsRenaming = true; // inline buttons will appear
            }
            else
            {
                await CommitRenameAsync(vm);
            }
            return;
        }
        if (choice == "Move Up")
        {
            if (!CanReorderItems() || isTop) { await ShowViewerBlockedAsync("reordering items"); return; }
            SetSingleSelection(vm);
            await MoveSelectedAsync(-1);
            return;
        }
        if (choice == "Move Down")
        {
            if (!CanReorderItems() || isBottom) { await ShowViewerBlockedAsync("reordering items"); return; }
            SetSingleSelection(vm);
            await MoveSelectedAsync(1);
            return;
        }
        if (choice == "Reset Subtree")
        {
            if (!CanResetSubtree() || !hasSubtree) { await ShowViewerBlockedAsync("resetting subtree"); return; }
            SetSingleSelection(vm);
            await ResetSelectedSubtreeAsync();
            return;
        }
    }

    private async Task CommitRenameAsync(ItemVm vm)
    {
        if (!CanRenameItems()) { await ShowViewerBlockedAsync("renaming items"); return; }
        var newName = vm.EditableName?.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name)
        {
            vm.IsRenaming = false; vm.EditableName = vm.Name; return;
        }
        try
        {
            var rev = await _db.GetListRevisionAsync(vm.ListId);
            var res = await _db.RenameItemAsync(vm.Id, newName!, rev);
            if (!res.Ok)
            {
                await DisplayAlert("Rename", "Concurrency mismatch; items refreshed.", "OK");
                await RefreshItemsAsync(true); return;
            }
            vm.Name = newName!; vm.IsRenaming = false; vm.RecalcState(); RebuildVisibleItems(); _lastRevision = res.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Rename Failed", ex.Message, "OK"); await RefreshItemsAsync(true);
        }
    }

    // Rebuild visible (flat list) respecting hide-completed & expansion
    private void RebuildVisibleItems()
    {
        var visible = new List<ItemVm>();
        foreach (var root in _allItems.Where(x => x.ParentId == null).OrderBy(x => x.Order)) // use Order instead of SortKey so in-memory order changes take immediate effect
        { if (_hideCompleted) TraceAddFiltered(root, visible); else TraceAdd(root, visible); }
        // In-place diff
        for (int i=0;i<visible.Count;i++)
        {
            if (i >= _items.Count) _items.Add(visible[i]);
            else if (!ReferenceEquals(_items[i], visible[i]))
            {
                int existing = _items.IndexOf(visible[i]);
                if (existing >= 0 && existing != i) { _items.RemoveAt(existing); _items.Insert(i, visible[i]); }
                else if (existing < 0) _items.Insert(i, visible[i]);
            }
        }
        while (_items.Count > visible.Count) _items.RemoveAt(_items.Count - 1);
        UpdateFilteredEmptyLabel(visibleCount: visible.Count);
        if (_selectedItem != null && !visible.Any(v => v.Id == _selectedItem.Id)) ClearSelectionAndUi();
        RefreshItemCardStyles(); UpdateStats();
    }

    private void UpdateFilteredEmptyLabel(int? visibleCount = null)
    {
        if (_emptyFilteredLabel != null)
        {
            int vis = visibleCount ?? _items.Count;
            _emptyFilteredLabel.IsVisible = _hideCompleted && vis == 0 && _allItems.Any();
            if (_emptyFilteredLabel.IsVisible)
            { var hidden = _allItems.Count(i => i.IsCompleted); _emptyFilteredLabel.Text = hidden > 0 ? $"All {hidden} items are completed and hidden." : "All items are completed and hidden."; }
        }
    }

    private void TraceAdd(ItemVm node, List<ItemVm> target, HashSet<int>? visited=null, int depth=0)
    { visited ??= new(); if (node==null) return; if (!visited.Add(node.Id) || depth>64) return; target.Add(node); if (!node.IsExpanded) return; foreach(var c in node.Children.OrderBy(c=>c.Order)) TraceAdd(c,target,visited,depth+1); }
    private void TraceAddFiltered(ItemVm node, List<ItemVm> target, HashSet<int>? visited=null, int depth=0)
    { visited ??= new(); if (node==null) return; if (!visited.Add(node.Id) || depth>64) return; if (_hideCompleted && node.IsCompleted) return; target.Add(node); if (!node.IsExpanded) return; foreach(var c in node.Children.OrderBy(c=>c.Order)) TraceAddFiltered(c,target,visited,depth+1); }

    // Build card with controls
    private Border BuildItemsCard()
    {
        _emptyFilteredLabel ??= new Label { IsVisible=false, TextColor=Colors.Gray, FontSize=12 };
        _statsLabel ??= new Label { FontSize=12, TextColor=(Color)Application.Current!.Resources[Application.Current!.RequestedTheme==AppTheme.Dark?"Gray300":"Gray600"], HorizontalTextAlignment=TextAlignment.End };
        _itemViewTemplate = CreateItemTemplate();
        _itemsView = new CollectionView { ItemsSource=_items, SelectionMode=SelectionMode.None, ItemTemplate=_itemViewTemplate, ItemsUpdatingScrollMode=ItemsUpdatingScrollMode.KeepScrollOffset };
        // Remove header action buttons; actions are now in item menu
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing=8 };
        header.Add(new Label { Text="Items", Style=(Style)Application.Current!.Resources["SectionTitle"], VerticalTextAlignment=TextAlignment.Center },0,0);
        header.Add(_statsLabel,1,0);
        // Initialize switch before adding to filter row
        _hideCompletedSwitch = new Switch { IsToggled=_hideCompleted };
        _hideCompletedSwitch.Toggled += async (_,e)=> await OnHideCompletedToggledAsync(e.Value);
        var filterRow = new HorizontalStackLayout { Spacing=8, Children={ new Label { Text="Hide Completed" }, _hideCompletedSwitch } };
        _openNewItemButton = new Button { Text = "+ New Item", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _openNewItemButton.Clicked += (_,__) => ShowNewItemOverlay();
        var newButtonRow = new HorizontalStackLayout { Children = { _openNewItemButton }, HorizontalOptions = LayoutOptions.Start };
        // New: List selection row inside card
        var listRow = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "List", VerticalTextAlignment = TextAlignment.Center }, _listPicker } };
        var card = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) }, Padding = new Thickness(6,0,4,0), Content = new VerticalStackLayout { Spacing=12, Padding=16, Children = { newButtonRow, listRow, header, filterRow, _emptyFilteredLabel, _itemsView } } };
        card.Style = (Style)Application.Current!.Resources["CardBorder"]; return card;
    }

    private DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var card = new Border { StrokeThickness=1, StrokeShape=new RoundRectangle { CornerRadius=new CornerRadius(10)}, Padding=new Thickness(6,0,4,0)};
            card.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelBorderGapConverter()));

            // Grid now has 3 rows: top indicator, content, bottom indicator
            var grid = new Grid
            {
                Padding=new Thickness(4,4),
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                ColumnDefinitions={ new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(28)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
            };

            // Drop position indicators
            var topIndicator = new BoxView { HeightRequest = 2, BackgroundColor = (Color)Application.Current!.Resources["Primary"], Opacity = 0.9, IsVisible = false };
            var bottomIndicator = new BoxView { HeightRequest = 2, BackgroundColor = (Color)Application.Current!.Resources["Primary"], Opacity = 0.9, IsVisible = false };
            Grid.SetColumnSpan(topIndicator, 7); grid.Add(topIndicator, 0, 0);
            Grid.SetColumnSpan(bottomIndicator, 7); grid.Add(bottomIndicator, 0, 2);

            // Transparent hit areas to detect drag-over (kept visible to receive drag events)
            var topHit = new BoxView { HeightRequest = 18, BackgroundColor = Colors.Transparent, Opacity = 0.01, InputTransparent = false };
            var bottomHit = new BoxView { HeightRequest = 18, BackgroundColor = Colors.Transparent, Opacity = 0.01, InputTransparent = false };
            Grid.SetColumnSpan(topHit, 7); grid.Add(topHit, 0, 0);
            Grid.SetColumnSpan(bottomHit, 7); grid.Add(bottomHit, 0, 2);

            // Content row container (so existing layout stays the same)
            var content = new Grid { ColumnDefinitions={ new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(28)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };

            // Collapse expand column width for leaf nodes so name shifts left
            var expandContainer = new Grid { WidthRequest=28, HeightRequest=28 };
            expandContainer.Triggers.Add(new DataTrigger(typeof(Grid))
            {
                Binding = new Binding("HasChildren"),
                Value = false,
                Setters = { new Setter { Property = Grid.WidthRequestProperty, Value = 0 } }
            });
            var badge = new Label { Margin=new Thickness(4,0), VerticalTextAlignment=TextAlignment.Center, FontAttributes=FontAttributes.Bold };
            badge.SetBinding(Label.TextProperty, new Binding(".", converter:new LevelBadgeConverter()));
            badge.SetBinding(Label.TextColorProperty, new Binding(".", converter:new LevelAccentColorConverter())); content.Add(badge,0,0);

            // Drag start handler also enforces single selection
            void OnDragStarting(object? s, DragStartingEventArgs e){ if (!CanDragItems()) return; if (card.BindingContext is ItemVm vm){ _holdCts?.Cancel(); vm.IsPreDrag=false; SetSingleSelection(vm); _dragGestureActive=true; vm.IsDragging=true; ApplyItemCardStyle(card,vm); _dragItem=vm; _pendingDragVm=vm; try{ e.Data.Properties["ItemId"] = vm.Id;}catch{} } }
            var drag = new DragGestureRecognizer{ CanDrag=true }; drag.DragStarting += OnDragStarting; drag.DropCompleted += (_, __) => { 
                if (card.BindingContext is ItemVm vmDone) { vmDone.IsDragging=false; ApplyItemCardStyle(card, vmDone); }
                _dragItem=null; _pendingDragVm=null; _dragDropCompleted=true; _dragGestureActive=false; 
                topIndicator.IsVisible=false; bottomIndicator.IsVisible=false; 
                if (card.BindingContext is ItemVm vmRestore)
                {
                    ApplyItemCardStyle(card, vmRestore);
                }
            } ; card.GestureRecognizers.Add(drag);

            // Full-card hover highlight while dragging over content
            var contentDrop = new DropGestureRecognizer { AllowDrop = true };
            bool contentHovering = false;
            CancellationTokenSource? hoverExpandCts = null;
            contentDrop.DragOver += (s, e) =>
            {
                if (!CanDragItems()) return; if (_dragItem == null) return;
                contentHovering = true;
                if (card.BindingContext is ItemVm target)
                {
                    // Depth restriction visual
                    bool prohibited = WouldExceedDepth(_dragItem, target.Level + 1);
                    var primary = (Color)Application.Current!.Resources["Primary"];
                    var danger = Colors.Red;
                    card.BackgroundColor = prohibited ? danger.WithAlpha(0.10f) : primary.WithAlpha(0.10f);
                    card.Stroke = prohibited ? danger : primary;
                }

                // Auto-expand collapsed items after a short hover while dragging
                if (card.BindingContext is ItemVm target2 && target2.HasChildren && !target2.IsExpanded)
                {
                    hoverExpandCts?.Cancel();
                    hoverExpandCts = new CancellationTokenSource();
                    var cts = hoverExpandCts;
                    Device.StartTimer(TimeSpan.FromMilliseconds(220), () =>
                    {
                        if (cts!.IsCancellationRequested) return false;
                        if (!contentHovering) return false;
                        // Expand and persist state
                        target2.IsExpanded = true;
                        ExpandIncremental(target2);
                        _expandedStates[target2.Id] = true;
                        return false;
                    });
                }
            };
            contentDrop.DragLeave += (s, e) =>
            {
                contentHovering = false;
                hoverExpandCts?.Cancel();
                if (card.BindingContext is ItemVm vmLeave)
                {
                    ApplyItemCardStyle(card, vmLeave);
                }
            };
            contentDrop.Drop += async (s, e) =>
            {
                // Drop ON the card: make dragged item a child of this target (if depth limit permits)
                if (!CanDragItems()) return; if (_selectedListId == null) return;
                var dragItem = _dragItem; // capture to avoid race
                if (dragItem == null) return;
                if (card.BindingContext is not ItemVm target) return;
                if (target.Id == dragItem.Id) return; // ignore self
                if (WouldExceedDepth(dragItem, target.Level + 1))
                {
                    // visual flash to indicate prohibited
                    await card.FadeTo(0.6, 90);
                    await card.FadeTo(1.0, 90);
                    return;
                }
                // Compute new order as last among target's children
                int newOrder = 0;
                var targetChildren = _allItems.Where(x => x.ParentId == target.Id).OrderBy(x => x.Order).ToList();
                if (targetChildren.Count > 0)
                    newOrder = targetChildren.Last().Order + 1;
                else
                    newOrder = 1; // first child baseline
                try
                {
                    long expected = await _db.GetListRevisionAsync(_selectedListId.Value);
                    // Move under target as parent
                    var moveRes = await _db.MoveItemAsync(dragItem.Id, target.Id, expected);
                    if (!moveRes.Ok) { await RefreshItemsAsync(true); return; }
                    expected = moveRes.NewRevision;
                    // Set order to end of children
                    var orderRes = await _db.SetItemOrderAsync(dragItem.Id, newOrder, expected);
                    if (!orderRes.Ok) { await RefreshItemsAsync(true); return; }
                    _lastRevision = orderRes.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                    await RefreshItemsAsync(true);
                }
                catch { await RefreshItemsAsync(true); }
            };
            // No drop action for content (order is decided by top/bottom zones)
            grid.GestureRecognizers.Add(contentDrop);

            // Top drop zone
            var topDrop = new DropGestureRecognizer { AllowDrop = true };
            topDrop.DragOver += (s, e) =>
            {
                if (!CanDragItems()) return; if (_dragItem == null) { topIndicator.IsVisible=false; return; }
                if (card.BindingContext is not ItemVm target) { topIndicator.IsVisible=false; return; }
                if (target.Id == _dragItem.Id) { topIndicator.IsVisible=false; return; }
                if (IsUnder(_dragItem, target)) { topIndicator.IsVisible=false; return; }
                topIndicator.IsVisible = true; bottomIndicator.IsVisible = false;
                // also subtle card hover
                var primary = (Color)Application.Current!.Resources["Primary"]; card.BackgroundColor = primary.WithAlpha(0.06f);
            };
            topDrop.DragLeave += (s, e) => { topIndicator.IsVisible=false; if (card.BindingContext is ItemVm vmLeave2) ApplyItemCardStyle(card, vmLeave2); };
            topDrop.Drop += async (s, e) =>
            {
                topIndicator.IsVisible=false; bottomIndicator.IsVisible=false;
                if (!CanDragItems()) return; if (_selectedListId == null) return;
                var dragItem = _dragItem; // capture to avoid race with DropCompleted
                if (dragItem == null) return;
                if (card.BindingContext is not ItemVm target) return; if (target.Id == dragItem.Id) return; if (IsUnder(dragItem, target)) return;
                int newOrder = target.Order - 1;
                try
                {
                    long expected = await _db.GetListRevisionAsync(_selectedListId.Value);
                    if (dragItem.ParentId != target.ParentId)
                    {
                        var moveRes = await _db.MoveItemAsync(dragItem.Id, target.ParentId, expected);
                        if (!moveRes.Ok) { await RefreshItemsAsync(true); return; }
                        expected = moveRes.NewRevision;
                        // Parent change affects hierarchy; perform a refresh for consistency
                        await RefreshItemsAsync(true);
                        _lastRevision = expected; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                        return;
                    }
                    var orderRes = await _db.SetItemOrderAsync(dragItem.Id, newOrder, expected);
                    if (!orderRes.Ok) { await RefreshItemsAsync(true); return; }
                    dragItem.Order = newOrder;
                    // Incremental visible move when possible
                    var currentVisIdx = _items.IndexOf(dragItem);
                    var targetVisIdx = _items.IndexOf(target);
                    if(currentVisIdx>=0 && targetVisIdx>=0){
                        int newVisIdx = Math.Max(0, targetVisIdx);
                        if(currentVisIdx!=newVisIdx){ _items.RemoveAt(currentVisIdx); _items.Insert(newVisIdx, dragItem); }
                        RefreshItemCardStyles(); UpdateStats();
                    } else {
                        await RefreshItemsAsync(true);
                    }
                    _lastRevision = orderRes.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                }
                catch { await RefreshItemsAsync(true); }
            };
            topHit.GestureRecognizers.Add(topDrop);

            // Bottom drop zone
            var bottomDrop = new DropGestureRecognizer { AllowDrop = true };
            bottomDrop.DragOver += (s, e) =>
            {
                if (!CanDragItems()) return; if (_dragItem == null) { bottomIndicator.IsVisible=false; return; }
                if (card.BindingContext is not ItemVm target) { bottomIndicator.IsVisible=false; return; }
                if (target.Id == _dragItem.Id) { bottomIndicator.IsVisible=false; return; }
                if (IsUnder(_dragItem, target)) { bottomIndicator.IsVisible=false; return; }
                topIndicator.IsVisible = false; bottomIndicator.IsVisible = true;
                var primary = (Color)Application.Current!.Resources["Primary"]; card.BackgroundColor = primary.WithAlpha(0.06f);
            };
            bottomDrop.DragLeave += (s, e) => { bottomIndicator.IsVisible=false; if (card.BindingContext is ItemVm vmLeave3) ApplyItemCardStyle(card, vmLeave3); };
            bottomDrop.Drop += async (s, e) =>
            {
                topIndicator.IsVisible=false; bottomIndicator.IsVisible=false;
                if (!CanDragItems()) return; if (_selectedListId == null) return;
                var dragItem = _dragItem; // capture to avoid race with DropCompleted
                if (dragItem == null) return;
                if (card.BindingContext is not ItemVm target) return; if (target.Id == dragItem.Id) return; if (IsUnder(dragItem, target)) return;
                int newOrder = target.Order + 1;
                try
                {
                    long expected = await _db.GetListRevisionAsync(_selectedListId.Value);
                    if (dragItem.ParentId != target.ParentId)
                    {
                        var moveRes = await _db.MoveItemAsync(dragItem.Id, target.ParentId, expected);
                        if (!moveRes.Ok) { await RefreshItemsAsync(true); return; }
                        expected = moveRes.NewRevision;
                        // Parent change affects hierarchy; perform a refresh for consistency
                        await RefreshItemsAsync(true);
                        _lastRevision = expected; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                        return;
                    }
                    var orderRes = await _db.SetItemOrderAsync(dragItem.Id, newOrder, expected);
                    if (!orderRes.Ok) { await RefreshItemsAsync(true); return; }
                    dragItem.Order = newOrder;
                    var currentVisIdx = _items.IndexOf(dragItem);
                    var targetVisIdx = _items.IndexOf(target);
                    if(currentVisIdx>=0 && targetVisIdx>=0){
                        int newVisIdx = Math.Min(_items.Count-1, targetVisIdx+1);
                        if(currentVisIdx!=newVisIdx){ _items.RemoveAt(currentVisIdx); _items.Insert(newVisIdx, dragItem); }
                        RefreshItemCardStyles(); UpdateStats();
                    } else {
                        await RefreshItemsAsync(true);
                    }
                    _lastRevision = orderRes.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                }
                catch { await RefreshItemsAsync(true); }
            };
            bottomHit.GestureRecognizers.Add(bottomDrop);

            // Press-and-hold pre-drag feedback using Pointer events
            var pointer = new PointerGestureRecognizer();
            pointer.PointerPressed += (_, __) =>
            {
                if (card.BindingContext is not ItemVm vm || !CanDragItems()) return;
                _holdCts?.Cancel();
                _holdItem = vm;
                var cts = new CancellationTokenSource();
                _holdCts = cts;
                // Start a short hold threshold to avoid flicker on simple clicks
                Device.StartTimer(TimeSpan.FromMilliseconds(160), () =>
                {
                    if (cts.IsCancellationRequested) return false;
                    // If a real drag already started, skip predrag
                    if (_dragGestureActive) return false;
                    // Apply selection and drag visuals
                    SetSingleSelection(vm);
                    vm.IsPreDrag = true;
                    vm.IsDragging = true;
                    ApplyItemCardStyle(card, vm);
                    return false;
                });
            };
            pointer.PointerReleased += (_, __) =>
            {
                if (card.BindingContext is not ItemVm vm) return;
                _holdCts?.Cancel();
                // If no drag actually started, clear drag visuals but keep selection
                if (!_dragGestureActive && vm.IsDragging)
                {
                    vm.IsDragging = false;
                    vm.IsPreDrag = false;
                    ApplyItemCardStyle(card, vm);
                }
            };
            card.GestureRecognizers.Add(pointer);

            // Selection tap (single selection enforced)
            var selectTap = new TapGestureRecognizer();
            selectTap.Tapped += (_, __) => { if (card.BindingContext is ItemVm vmSel) SetSingleSelection(vmSel); };
            card.GestureRecognizers.Add(selectTap);

            var chevronRight = new Microsoft.Maui.Controls.Shapes.Path { Stroke=new SolidColorBrush((Color)Application.Current!.Resources["Primary"]), StrokeThickness=2, HorizontalOptions=LayoutOptions.Center, VerticalOptions=LayoutOptions.Center, Data=(Geometry)new PathGeometryConverter().ConvertFromInvariantString("M8 6 L16 12 L8 18") };
            chevronRight.SetBinding(IsVisibleProperty, new MultiBinding { Bindings = { new Binding("HasChildren"), new Binding("IsExpanded") }, Converter = new ChevronRightVisibilityConverter() });
            var chevronDown = new Microsoft.Maui.Controls.Shapes.Path { Stroke=new SolidColorBrush((Color)Application.Current!.Resources["Primary"]), StrokeThickness=2, HorizontalOptions=LayoutOptions.Center, VerticalOptions=LayoutOptions.Center, Data=(Geometry)new PathGeometryConverter().ConvertFromInvariantString("M6 9 L12 15 L18 9") };
            chevronDown.SetBinding(IsVisibleProperty, new MultiBinding { Bindings = { new Binding("HasChildren"), new Binding("IsExpanded") }, Converter = new ChevronDownVisibilityConverter() });
            expandContainer.Children.Add(chevronRight); expandContainer.Children.Add(chevronDown); content.Add(expandContainer,1,0);
            // Add tap gesture to toggle expand/collapse
            var expandTap = new TapGestureRecognizer();
            expandTap.Tapped += (_, __) => {
                if (card.BindingContext is ItemVm vm && vm.HasChildren)
                {
                    if (vm.IsExpanded) { CollapseIncremental(vm); }
                    else { ExpandIncremental(vm); }
                    _expandedStates[vm.Id] = vm.IsExpanded; // persist
                }
            };
            expandContainer.GestureRecognizers.Add(expandTap);

            var nameLabel = new Label { VerticalTextAlignment=TextAlignment.Center }; nameLabel.SetBinding(Label.TextProperty, "Name"); nameLabel.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelIndentConverter())); nameLabel.SetBinding(IsVisibleProperty, new Binding("IsRenaming", converter:new InvertBoolConverter()));
            var nameEntry = new Entry { HeightRequest=32, FontSize=14 }; nameEntry.SetBinding(Entry.TextProperty, "EditableName", BindingMode.TwoWay); nameEntry.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelIndentConverter())); nameEntry.SetBinding(IsVisibleProperty, "IsRenaming");
            nameEntry.Completed += async (_, __) => { if (card.BindingContext is ItemVm vmComp && vmComp.IsRenaming) await CommitRenameAsync(vmComp); };
            nameEntry.Unfocused += async (_, __) => { if (card.BindingContext is ItemVm vmUnf && vmUnf.IsRenaming) await CommitRenameAsync(vmUnf); };
            nameEntry.PropertyChanged += (_, pe) => { if (pe.PropertyName == nameof(Entry.IsVisible) && nameEntry.IsVisible) Device.BeginInvokeOnMainThread(() => nameEntry.Focus()); };
            // Inline rename action buttons
            var inlineSaveBtn = new Button { Text = "Save", FontSize=12, Padding=new Thickness(8,2), Style=(Style)Application.Current!.Resources["OutlinedButton"] };
            inlineSaveBtn.SetBinding(IsVisibleProperty, "IsRenaming");
            inlineSaveBtn.Clicked += async (_, __) => { if (inlineSaveBtn.BindingContext is ItemVm vmSave && vmSave.IsRenaming) await CommitRenameAsync(vmSave); };
            var inlineCancelBtn = new Button { Text = "Cancel", FontSize=12, Padding=new Thickness(8,2), Style=(Style)Application.Current!.Resources["OutlinedButton"] };
            inlineCancelBtn.SetBinding(IsVisibleProperty, "IsRenaming");
            inlineCancelBtn.Clicked += (_, __) => { if (inlineCancelBtn.BindingContext is ItemVm vmCancel) { vmCancel.IsRenaming = false; vmCancel.EditableName = vmCancel.Name; } };
            var nameContainer = new HorizontalStackLayout { Spacing=4, VerticalOptions=LayoutOptions.Center, Children={ nameLabel, nameEntry, inlineSaveBtn, inlineCancelBtn } };
            // Leaf items: shift name left by width of missing chevron column (approx 20px)
            nameContainer.Triggers.Add(new DataTrigger(typeof(HorizontalStackLayout))
            {
                Binding = new Binding("HasChildren"),
                Value = false,
                Setters = { new Setter { Property = View.TranslationXProperty, Value = -20 } }
            });
            content.Add(nameContainer,2,0);
            var statusStack = new HorizontalStackLayout { Spacing=4, VerticalOptions=LayoutOptions.Center };
            var completedInfoLabel = new Label { FontSize=11, TextColor=Color.FromArgb("#008A2E"), FontAttributes=FontAttributes.Italic, LineBreakMode=LineBreakMode.TailTruncation, VerticalTextAlignment=TextAlignment.Center }; completedInfoLabel.SetBinding(Label.TextProperty,"CompletedInfo"); completedInfoLabel.SetBinding(Label.IsVisibleProperty,"ShowCompletedInfo");
            var check = new CheckBox(); check.SetBinding(CheckBox.IsCheckedProperty,"IsCompleted"); check.CheckedChanged += async (_,e)=>{ if (_suppressCompletionEvent) return; if (!CanCompleteItems()){ _suppressCompletionEvent=true; if (check.BindingContext is ItemVm vmPrior){ var prior=!e.Value; vmPrior.IsCompleted=prior; check.IsChecked=prior; } _suppressCompletionEvent=false; await ShowViewerBlockedAsync("changing completion state"); return; } if (check.BindingContext is ItemVm vmC) await ToggleItemCompletionInlineAsync(vmC, e.Value); }; check.IsEnabled = CanCompleteItems();
            var partialIndicator = new Label { FontAttributes=FontAttributes.Bold, TextColor=Colors.Orange, FontSize=14, WidthRequest=12, HorizontalTextAlignment=TextAlignment.Center, VerticalTextAlignment=TextAlignment.Center }; partialIndicator.SetBinding(Label.TextProperty,"PartialGlyph");
            statusStack.Children.Add(completedInfoLabel); statusStack.Children.Add(check); statusStack.Children.Add(partialIndicator); content.Add(statusStack,3,0);

            // Three-dot menu button
            var menuBtn = new Button { Text = "?", FontSize = 16, Padding = new Thickness(6,2), Style = (Style)Application.Current!.Resources["OutlinedButton"], HorizontalOptions = LayoutOptions.End };
            menuBtn.Clicked += async (s, e) => { if (menuBtn.BindingContext is ItemVm vmMenu) await ShowItemMenuAsync(vmMenu, menuBtn); };
            content.Add(menuBtn,4,0);

            // Put content in middle row of outer grid
            Grid.SetRow(content, 1);
            Grid.SetColumnSpan(content, 7);
            grid.Add(content);

            // Track this card + subscribe to VM changes for live visual updates
            card.BindingContextChanged += (_,__) =>
            {
                if (!_itemCardBorders.Contains(card)) _itemCardBorders.Add(card);
                var oldVm = card.GetValue(ItemVmTrackerProperty) as ItemVm;
                if (oldVm != null) oldVm.PropertyChanged -= OnItemVmPropertyChanged;
                if (card.BindingContext is ItemVm newVm)
                {
                    card.SetValue(ItemVmTrackerProperty, newVm);
                    newVm.PropertyChanged += OnItemVmPropertyChanged;
                    ApplyItemCardStyle(card, newVm);
                }
            };
            card.Unloaded += (_,__) =>
            {
                var vm = card.GetValue(ItemVmTrackerProperty) as ItemVm;
                if (vm != null) vm.PropertyChanged -= OnItemVmPropertyChanged;
                _itemCardBorders.Remove(card);
            };

            card.Content = grid; card.Style=(Style)Application.Current!.Resources["CardBorder"];

            // Spacer between items that also acts as a drop zone
            var spacer = new BoxView { HeightRequest = 6, Opacity = 0, BackgroundColor = Colors.Transparent };
            var spacerDrop = new DropGestureRecognizer { AllowDrop = true };
            spacerDrop.DragOver += (s, e) =>
            {
                if (!CanDragItems()) return; if (_dragItem == null) return;
                var primary = (Color)Application.Current!.Resources["Primary"]; spacer.BackgroundColor = primary; spacer.Opacity = 0.5;
            };
            spacerDrop.DragLeave += (s, e) => { spacer.Opacity = 0; spacer.BackgroundColor = Colors.Transparent; };
            spacerDrop.Drop += async (s, e) =>
            {
                spacer.Opacity = 0; spacer.BackgroundColor = Colors.Transparent;
                if (!CanDragItems()) return; if (_selectedListId == null) return;
                var dragItem = _dragItem; // capture to avoid race with DropCompleted
                if (dragItem == null) return;
                if (card.BindingContext is not ItemVm current) return;
                // Find next visible item to compute in-between order
                var idx2 = _items.IndexOf(current);
                ItemVm? next = (idx2 >= 0 && idx2 + 1 < _items.Count) ? _items[idx2 + 1] : null;
                int newOrder = current.Order + 1;
                if (next != null)
                { newOrder = next.Order - 1; }
                try
                {
                    long expected = await _db.GetListRevisionAsync(_selectedListId.Value);
                    int? targetParent = current.ParentId; // keep under same parent as current
                    if (dragItem.ParentId != targetParent)
                    {
                        var moveRes = await _db.MoveItemAsync(dragItem.Id, targetParent, expected);
                        if (!moveRes.Ok) { await RefreshItemsAsync(true); return; }
                        expected = moveRes.NewRevision;
                        // Parent change affects hierarchy; perform a refresh for consistency
                        await RefreshItemsAsync(true);
                        _lastRevision = expected; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                        return;
                    }
                    var orderRes = await _db.SetItemOrderAsync(dragItem.Id, newOrder, expected);
                    if (!orderRes.Ok) { await RefreshItemsAsync(true); return; }
                    dragItem.Order = newOrder;
                    // Incremental visible move after current
                    var curVisIdx = _items.IndexOf(dragItem);
                    var afterVisIdx = idx2>=0 ? Math.Min(_items.Count-1, idx2+1) : -1;
                    if(curVisIdx>=0 && afterVisIdx>=0){ _items.RemoveAt(curVisIdx); _items.Insert(afterVisIdx, dragItem); RefreshItemCardStyles(); UpdateStats(); }
                    else { await RefreshItemsAsync(true); }
                    _lastRevision = orderRes.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                }
                catch { await RefreshItemsAsync(true); }
            };
            spacer.GestureRecognizers.Add(spacerDrop);

            return new VerticalStackLayout { Spacing=0, Children={ card, spacer } };
        });
    }
// Converter classes for multi-binding chevron visibility
public class ChevronRightVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => (values.Length==2 && values[0] is bool has && values[1] is bool expanded && has && !expanded);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => Array.Empty<object>();
}
public class ChevronDownVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => (values.Length==2 && values[0] is bool has && values[1] is bool expanded && has && expanded);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => Array.Empty<object>();
}

    // Incremental expand: insert visible descendants after parent without rebuilding whole list
    private void ExpandIncremental(ItemVm parent)
    {
        if (parent == null) return; parent.IsExpanded = true;
        var insertIndex = _items.IndexOf(parent);
        if (insertIndex < 0) return;
        var toInsert = new List<ItemVm>();
        void Trace(ItemVm n)
        {
            foreach (var c in n.Children.OrderBy(c=>c.Order))
            {
                if (_hideCompleted && c.IsCompleted) continue;
                toInsert.Add(c);
                if (c.IsExpanded) Trace(c);
            }
        }
        Trace(parent);
        // Only insert those not already present immediately after parent chain
        int offset = 1;
        foreach (var vm in toInsert)
        {
            if (_items.Contains(vm)) continue; // already visible (avoid move flicker)
            _items.Insert(insertIndex + offset, vm);
            offset++;
        }
        RefreshItemCardStyles(); UpdateStats();
    }
    // Incremental collapse: remove all descendant items currently visible
    private void CollapseIncremental(ItemVm parent)
    {
        if (parent == null) return; parent.IsExpanded = false;
        var visibleDesc = new HashSet<int>();
        void Collect(ItemVm n)
        {
            foreach (var c in n.Children)
            {
                visibleDesc.Add(c.Id);
                if (c.IsExpanded) Collect(c);
            }
        }
        Collect(parent);
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].ParentId != null && visibleDesc.Contains(_items[i].Id))
                _items.RemoveAt(i);
        }
        RefreshItemCardStyles(); UpdateStats();
    }

    // Data helpers
    private ItemVm CreateVmFromRecord(ItemRecord rec)
    { var isExpanded = _expandedStates.TryGetValue(rec.Id, out var ex) ? ex : true; var vm = new ItemVm(rec.Id, rec.ListId, rec.Name, rec.IsCompleted, rec.ParentItemId, rec.HasChildren, rec.ChildrenCount, rec.IncompleteChildrenCount, rec.Level, isExpanded, rec.Order, rec.SortKey) { CompletedByUsername = rec.CompletedByUsername, CompletedAtUtc = rec.CompletedAtUtc }; return vm; }
    private void RemoveLocalSubtree(ItemVm root){ if (root==null) return; var remove=new HashSet<int>(); void Collect(ItemVm n){ if(!remove.Add(n.Id)) return; foreach(var c in n.Children) Collect(c);} Collect(root); _allItems.RemoveAll(a=>remove.Contains(a.Id)); foreach(var vm in _allItems) vm.Children.RemoveAll(c=>remove.Contains(c.Id)); }

    // CRUD operations (optimistic)
    private async Task AddItemAsync(){ var listId=_selectedListId; if(listId==null) return; var name=_newItemEntry.Text?.Trim(); if(string.IsNullOrWhiteSpace(name)) return; int newId; try { newId=await _db.AddItemAsync(listId.Value,name); } catch { return; } _newItemEntry.Text=string.Empty; try{ var rec=await _db.GetItemAsync(newId); if(rec!=null){ var vm=CreateVmFromRecord(rec); // DB assigns max sparse order placing item at bottom initially
                _allItems.Add(vm); vm.RecalcState();
                // Compute top insertion order immediately (smaller than any existing root order)
                if (vm.ParentId==null)
                {
                    var otherRoots = _allItems.Where(r=>r.ParentId==null && r.Id!=vm.Id).ToList();
                    if (otherRoots.Count>0)
                    {
                        var minOrder = otherRoots.Min(r=>r.Order);
                        int newOrder = minOrder - 1; // place before current first
                        vm.Order = newOrder; // optimistic local set
                        // Persist new order (best effort)
                        try {
                            var expected = await _db.GetListRevisionAsync(listId.Value);
                            var res = await _db.SetItemOrderAsync(vm.Id,newOrder,expected);
                            if (res.Ok){ _lastRevision = res.NewRevision; _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);} else { /* will be corrected on refresh */ }
                        } catch { /* ignore transient failures; next refresh will reconcile */ }
                    }
                }
                RebuildVisibleItems();
                UpdateCompletedSummary(); _recentLocalMutationUtc=DateTime.UtcNow; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(5);} else { /* fallback full refresh */ await RefreshItemsAsync(true);} } catch{ await RefreshItemsAsync(true);} HideNewItemOverlay(clear:true); }

    // Child item creation removed per requirement.
    private async Task DeleteItemInlineAsync(ItemVm vm){ var listId=_selectedListId; if(listId==null) return; var expected=await _db.GetListRevisionAsync(listId.Value); var result=await _db.DeleteItemAsync(vm.Id,expected); if(result.Ok){ RemoveLocalSubtree(vm); RebuildVisibleItems(); UpdateCompletedSummary(); _recentLocalMutationUtc=DateTime.UtcNow; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} else await RefreshItemsAsync(true); }
    private async Task MoveSelectedAsync(int delta){ if(_selectedItem==null) return; var listId=_selectedListId; if(listId==null) return; var siblings=_allItems.Where(x=>x.ParentId==_selectedItem.ParentId).OrderBy(x=>x.Order).ThenBy(x=>x.Id).ToList(); int idx=siblings.FindIndex(x=>x.Id==_selectedItem.Id); if(idx<0) return; int target=idx+delta; if(target<0||target>=siblings.Count) return; int newOrder = delta<0 ? siblings[target].Order-1 : siblings[target].Order+1; try{ var expected=await _db.GetListRevisionAsync(listId.Value); var res=await _db.SetItemOrderAsync(_selectedItem.Id,newOrder,expected); if(!res.Ok){ await RefreshItemsAsync(true); return;} // Update local order
            _selectedItem.Order=newOrder;
            // Keep children list ordered
            if(_selectedItem.ParentId!=null){ var parent=_allItems.FirstOrDefault(x=>x.Id==_selectedItem.ParentId.Value); parent?.Children.Sort((a,b)=>a.Order.CompareTo(b.Order)); }
            // Incremental move in visible list when both positions are visible and same parent
            var oldVisIdx = _items.IndexOf(_selectedItem);
            if(oldVisIdx>=0){ // find neighbor to compute new visible index
                // Determine target visible index by locating the sibling at 'target' if visible
                var targetSibling = siblings[target];
                var targetVisIdx = _items.IndexOf(targetSibling);
                if(targetVisIdx>=0){
                    // Move selected item before or after target based on delta
                    int newVisIdx = delta<0 ? targetVisIdx : targetVisIdx+1;
                    // Clamp within bounds
                    newVisIdx = Math.Max(0, Math.Min(newVisIdx, _items.Count-1));
                    if(newVisIdx!=oldVisIdx){ _items.RemoveAt(oldVisIdx); _items.Insert(newVisIdx, _selectedItem); }
                } else {
                    // If target sibling not visible (e.g., filtered or collapsed), do minimal list refresh for affected region
                    // Fallback: leave position; styles and stats still update
                }
            }
            RefreshItemCardStyles(); UpdateStats(); _lastRevision=res.NewRevision; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} catch{ await RefreshItemsAsync(true);} UpdateMoveButtons(); }
    private async Task ResetSelectedSubtreeAsync(){ if(_selectedItem==null) return; var listId=_selectedListId; if(listId==null) return; try{ var expected=await _db.GetListRevisionAsync(listId.Value); var (ok,newRev,affected)=await _db.ResetSubtreeAsync(_selectedItem.Id,expected); if(!ok){ await DisplayAlert("Reset","Concurrency mismatch; items refreshed.","OK"); await RefreshItemsAsync(true); return;} void Mark(ItemVm n){ n.IsCompleted=false; n.CompletedAtUtc=null; n.CompletedByUsername=null; foreach(var c in n.Children) Mark(c); n.RecalcState(); } Mark(_selectedItem); RebuildVisibleItems(); UpdateCompletedSummary(); _lastRevision=newRev; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} catch(Exception ex){ await DisplayAlert("Reset Failed", ex.Message, "OK"); await RefreshItemsAsync(true);} }
    private async Task ToggleItemCompletionInlineAsync(ItemVm vm,bool completed){ if(_selectedListId==null || _userId==null) return; try{ var ok=await _db.SetItemCompletedByUserAsync(vm.Id,_userId.Value,completed); if(!ok){ _suppressCompletionEvent=true; vm.IsCompleted=!completed; _suppressCompletionEvent=false; await DisplayAlert("Completion","Cannot complete item yet (children incomplete)","OK"); return;} _suppressCompletionEvent=true; vm.IsCompleted=completed; vm.CompletedAtUtc=completed?DateTime.UtcNow:null; vm.CompletedByUsername=completed? _username : null; vm.RecalcState(); _suppressCompletionEvent=false; if(completed){ var pid=vm.ParentId; while(pid!=null){ var parent=_allItems.FirstOrDefault(x=>x.Id==pid.Value); if(parent==null) break; if(parent.Children.All(c=>c.IsCompleted)){ parent.IsCompleted=true; parent.CompletedAtUtc=DateTime.UtcNow; parent.CompletedByUsername=_username; parent.RecalcState(); pid=parent.ParentId; } else break; } } else { var pid=vm.ParentId; while(pid!=null){ var parent=_allItems.FirstOrDefault(x=>x.Id==pid.Value); if(parent==null) break; if(parent.IsCompleted){ parent.IsCompleted=false; parent.CompletedAtUtc=null; parent.CompletedByUsername=null; parent.RecalcState(); } pid=parent.ParentId; } } if(_hideCompleted && completed){ // incremental removal instead of full rebuild
            for(int i=_items.Count-1;i>=0;i--){ if(_items[i].IsCompleted) _items.RemoveAt(i); }
            if(_selectedItem!=null && _selectedItem.IsCompleted) ClearSelectionAndUi();
            UpdateFilteredEmptyLabel(); UpdateCompletedSummary(); RefreshItemCardStyles(); _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3); }
        else { RebuildVisibleItems(); UpdateCompletedSummary(); _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} } catch(Exception ex){ _suppressCompletionEvent=true; vm.IsCompleted=!completed; _suppressCompletionEvent=false; await DisplayAlert("Completion Error", ex.Message, "OK"); await RefreshItemsAsync(true);} }

    // Refresh items from DB
    private async Task RefreshItemsAsync(bool userInitiated)
    {
        if (_selectedListId==null){ _items.Clear(); _allItems.Clear(); UpdateCompletedSummary(); return; }
        if (_isRefreshing) return; _isRefreshing=true; _suppressListRevisionCheck=true;
        try
        {
            IReadOnlyList<ItemRecord>? records = null; try { records = await _db.GetItemsAsync(_selectedListId.Value); } catch { }
            if (records==null){ // do NOT clear existing items on transient failure
                return; }
            _allItems.Clear();
            foreach(var r in records){ var vm=CreateVmFromRecord(r); _allItems.Add(vm); }
            var byId=_allItems.ToDictionary(x=>x.Id); foreach(var vm in _allItems){ if(vm.ParentId!=null && byId.TryGetValue(vm.ParentId.Value,out var p)) p.Children.Add(vm); }
            foreach(var vm in _allItems) vm.RecalcState();
            await LoadHideCompletedPreferenceForSelectedListAsync();
            RebuildVisibleItems(); UpdateCompletedSummary();
            // Set baseline revision after successful full refresh to prevent immediate duplicate polling refresh
            try { _lastRevision = await _db.GetListRevisionAsync(_selectedListId.Value); } catch { /* ignore */ }
        }
        finally { _isRefreshing=false; _suppressListRevisionCheck=false; }
    }

    private void UpdateCompletedSummary() { UpdateStats(); }

    private const int MaxDepthUi = 3; // mirror of service MaxDepth for client-side indication

    private int GetSubtreeDepth(ItemVm root)
    {
        if (root == null) return 0;
        int maxChild = 0;
        foreach (var c in _allItems.Where(x => x.ParentId == root.Id))
        {
            maxChild = Math.Max(maxChild, GetSubtreeDepth(c));
        }
        return 1 + maxChild;
    }
    private bool WouldExceedDepth(ItemVm dragItem, int newParentLevel)
    {
        try
        {
            var subtree = GetSubtreeDepth(dragItem); // depth including the item itself
            // Resulting deepest level = newParentLevel (for the item itself) + (subtree - 1) for its deepest descendant
            var resultingMaxLevel = newParentLevel + Math.Max(0, subtree - 1);
            return resultingMaxLevel > MaxDepthUi;
        }
        catch { return false; }
    }
}
