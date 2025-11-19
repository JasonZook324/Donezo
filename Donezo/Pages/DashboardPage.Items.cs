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
    private readonly ObservableCollection<ItemVm> _items = new();
    private List<ItemVm> _allItems = new();
    private Dictionary<int, bool> _expandedStates = new();
    private CollectionView _itemsView = null!;
    private DataTemplate? _itemViewTemplate;
    private Entry _newItemEntry = null!;
    private Button _addItemButton = null!;
    private Entry _newChildEntry = null!;
    private Button _addChildButton = null!;
    private Button? _moveUpButton; private Button? _moveDownButton; private Button? _resetSubtreeButton;

    private Border BuildItemsCard()
    {
        _emptyFilteredLabel ??= new Label { IsVisible = false, TextColor = Colors.Gray, FontSize = 12 };
        _newItemEntry = new Entry { Placeholder = "New item name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _newItemEntry.TextChanged += (s, e) => { if (_addItemButton != null) _addItemButton.IsEnabled = !string.IsNullOrWhiteSpace(_newItemEntry.Text); };
        _addItemButton = new Button { Text = "Add", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _addItemButton.Clicked += async (_, __) => await AddItemAsync();
        _newChildEntry = new Entry { Placeholder = "New child name", Style = (Style)Application.Current!.Resources["FilledEntry"], IsVisible = false };
        _newChildEntry.TextChanged += (s, e) => UpdateChildControls();
        _addChildButton = new Button { Text = "Add Child", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false, IsVisible = false };
        _addChildButton.Clicked += async (_, __) => await AddChildItemAsync();
        _itemViewTemplate = CreateItemTemplate();
        _itemsView = new CollectionView { ItemsSource = _items, SelectionMode = SelectionMode.None, ItemTemplate = _itemViewTemplate };
        _moveUpButton = new Button { Text = "Move Up", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveUpButton.Clicked += async (_, __) => await MoveSelectedAsync(-1);
        _moveDownButton = new Button { Text = "Move Down", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _moveDownButton.Clicked += async (_, __) => await MoveSelectedAsync(1);
        _resetSubtreeButton = new Button { Text = "Reset Subtree", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, IsEnabled = false };
        _resetSubtreeButton.Clicked += async (_, __) => await ResetSelectedSubtreeAsync();
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(400), () => { UpdateMoveButtons(); return true; });
        var itemsHeader = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Items", Style = (Style)Application.Current!.Resources["SectionTitle"] }, _moveUpButton, _moveDownButton, _resetSubtreeButton } };
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
            Action<ItemVm> applyStyle = vm =>
            {
                var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
                var defaultBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"];
                var defaultStroke = (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"];
                if (vm.IsDragging) { card.BackgroundColor = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.18f); card.Opacity = 0.75; card.Stroke = (Color)Application.Current!.Resources["Primary"]; return; }
                if (vm.IsPreDrag) { card.BackgroundColor = ((Color)Application.Current!.Resources["Primary"]).WithAlpha(0.12f); card.Opacity = 0.9; card.Stroke = defaultStroke; return; }
                if (vm.IsSelected) { card.BackgroundColor = defaultBg; card.Opacity = 1.0; card.Stroke = (Color)Application.Current!.Resources["Primary"]; return; }
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
            var bottomGap = new Border { HeightRequest = 4, BackgroundColor = Colors.Transparent, StrokeThickness = 1, Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Padding = 0, Margin = new Thickness(0, 2, 0, 0) };
            var bottomDrop = new DropGestureRecognizer();
            bottomDrop.DragOver += (_, __) => { if (_dragItem == null) return; bottomGap.HeightRequest = GetInsertionGapHeight(); bottomGap.BackgroundColor = zoneHover; bottomGap.Stroke = (Color)Application.Current!.Resources["Primary"]; };
            bottomDrop.DragLeave += (_, __) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; };
            bottomDrop.Drop += async (s, e) => { bottomGap.HeightRequest = 4; bottomGap.BackgroundColor = Colors.Transparent; bottomGap.Stroke = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm target) await SafeHandleDropAsync(target, "below"); };
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
            var nameContainer = new Grid();
            var nameLabel = new Label { VerticalTextAlignment = TextAlignment.Center };
            nameLabel.SetBinding(Label.TextProperty, nameof(ItemVm.Name));
            nameLabel.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameLabel.SetBinding(View.IsVisibleProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new InvertBoolConverter()));
            var userLabel = new Label { VerticalTextAlignment = TextAlignment.Center, FontSize = 12, Margin = new Thickness(6,0,0,0) };
            userLabel.SetBinding(Label.TextProperty, new Binding("LastActionUsername", stringFormat:"({0})"));
            userLabel.SetBinding(Label.TextColorProperty, new Binding(nameof(ItemVm.IsCompleted), converter: new BoolToStringConverter { TrueText = "#008A2E", FalseText = "#C62828" }));
            userLabel.SetBinding(Label.IsVisibleProperty, new Binding("LastActionUsername", converter: new BoolToAccessibleExpandNameConverter()));
            var nameEntry = new Entry { HeightRequest = 32, FontSize = 14 };
            nameEntry.SetBinding(Entry.TextProperty, nameof(ItemVm.EditableName), BindingMode.TwoWay);
            nameEntry.SetBinding(View.MarginProperty, new Binding(nameof(ItemVm.Level), converter: new LevelIndentConverter()));
            nameEntry.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            nameContainer.Add(nameLabel); nameContainer.Add(nameEntry); nameContainer.Add(userLabel);
            var dragOnName = new DragGestureRecognizer { CanDrag = true }; dragOnName.DragStarting += OnDragStarting; nameContainer.GestureRecognizers.Add(dragOnName);
            grid.Add(nameContainer, 2, 0);
            var statusStack = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 4 };
            var check = new CheckBox { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            check.SetBinding(CheckBox.IsCheckedProperty, nameof(ItemVm.IsCompleted));
            check.CheckedChanged += async (s, e) =>
            {
                if (((BindableObject)s).BindingContext is ItemVm ir)
                {
                    var ok = await _db.SetItemCompletedAsync(ir.Id, e.Value);
                    if (!ok) { await DisplayAlert("Blocked", "Parent cannot be completed until all direct children are complete.", "OK"); await RefreshItemsAsync(); return; }
                    ir.IsCompleted = e.Value; UpdateParentStates(ir); UpdateCompletedBadge(); if (_hideCompleted) RebuildVisibleItems();
                }
            };
            var partialIndicator = new Label { TextColor = Colors.Orange, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center, FontSize = 14, WidthRequest = 12 };
            partialIndicator.SetBinding(Label.TextProperty, nameof(ItemVm.PartialGlyph));
            statusStack.Add(check, 0, 0); statusStack.Add(partialIndicator, 1, 0);
            grid.Add(statusStack, 3, 0);
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
            var renameBtn = new Button { Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 64 };
            renameBtn.SetBinding(Button.TextProperty, new Binding(nameof(ItemVm.IsRenaming), converter: new BoolToStringConverter { TrueText = "Save", FalseText = "Rename" }));
            renameBtn.Clicked += async (s,e)=>
            {
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
            grid.Add(renameBtn, 5, 0);
            var cancelBtn = new Button { Text = "Cancel", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(6,2), MinimumWidthRequest = 56 };
            cancelBtn.SetBinding(View.IsVisibleProperty, nameof(ItemVm.IsRenaming));
            cancelBtn.Clicked += (s,e)=> { if (((BindableObject)s).BindingContext is ItemVm vm) { vm.IsRenaming = false; vm.EditableName = vm.Name; } };
            grid.Add(cancelBtn, 6, 0);
            var midDrop = new DropGestureRecognizer();
            midDrop.DragOver += (s, e) => { grid.BackgroundColor = zoneHover; if (((BindableObject)card).BindingContext is ItemVm vm && vm.HasChildren && !vm.IsExpanded) { ScheduleHoverExpand(vm); } };
            midDrop.DragLeave += (s, e) => { grid.BackgroundColor = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm vm) CancelHoverExpand(vm); };
            midDrop.Drop += async (s, e) => { grid.BackgroundColor = Colors.Transparent; if (((BindableObject)card).BindingContext is ItemVm target) { CancelHoverExpand(target.Id); await SafeHandleDropAsync(target, "into"); } };
            grid.GestureRecognizers.Add(midDrop);
            card.Content = grid; card.Style = (Style)Application.Current!.Resources["CardBorder"];
            card.BindingContextChanged += (s, e) =>
            {
                if (s is Border b)
                {
                    if (b.GetValue(TrackedVmProperty) is ItemVm oldVm && b.GetValue(TrackedHandlerProperty) is PropertyChangedEventHandler oldHandler) { try { oldVm.PropertyChanged -= oldHandler; } catch { } }
                    if (b.BindingContext is ItemVm vm)
                    {
                        applyStyle(vm);
                        PropertyChangedEventHandler handler = (sender, args) =>
                        {
                            if (args.PropertyName == nameof(ItemVm.IsDragging) || args.PropertyName == nameof(ItemVm.IsPreDrag) || args.PropertyName == nameof(ItemVm.IsSelected)) { applyStyle(vm); }
                        };
                        vm.PropertyChanged += handler; b.SetValue(TrackedVmProperty, vm); b.SetValue(TrackedHandlerProperty, handler);
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

    private async Task RefreshItemsAsync()
    {
        if (_isRefreshing) return; _isRefreshing = true;
        _items.Clear(); _allItems.Clear(); var listId = SelectedListId; if (listId == null) { UpdateCompletedBadge(); _isRefreshing = false; UpdateChildControls(); return; }
        if (_userId != null) _expandedStates = (await _db.GetExpandedStatesAsync(_userId.Value, listId.Value)).ToDictionary(k => k.Key, v => v.Value);
        var items = await _db.GetItemsAsync(listId.Value);
        int? selectedId = _selectedItem?.Id;
        foreach (var i in items)
        {
            var isExpanded = _expandedStates.TryGetValue(i.Id, out var ex) ? ex : true;
            var vm = new ItemVm(i.Id, i.ListId, i.Name, i.IsCompleted, i.ParentItemId, i.HasChildren, i.ChildrenCount, i.IncompleteChildrenCount, i.Level, isExpanded, i.Order, i.SortKey) { LastActionUsername = i.LastActionUsername }; // assumes ItemVm has settable LastActionUsername
            _allItems.Add(vm);
        }
        var byId = _allItems.ToDictionary(x => x.Id);
        foreach (var vm in _allItems) if (vm.ParentId != null && byId.TryGetValue(vm.ParentId.Value, out var p)) p.Children.Add(vm);
        foreach (var vm in _allItems) vm.RecalcState();
        _selectedItem = selectedId != null ? _allItems.FirstOrDefault(x => x.Id == selectedId.Value) : null;
        await LoadHideCompletedPreferenceForSelectedListAsync();
        RebuildVisibleItems(); UpdateCompletedBadge();
        _lastRevision = await _db.GetListRevisionAsync(listId.Value);
        _isRefreshing = false; UpdateChildControls();
#if WINDOWS
        RestorePageFocus();
#endif
    }

    private void RebuildVisibleItems()
    {
        _items.Clear(); var visible = new List<ItemVm>();
        foreach (var root in _allItems.Where(x => x.ParentId == null).OrderBy(x => x.SortKey)) { if (_hideCompleted) AddWithDescendantsFiltered(root, visible); else AddWithDescendants(root, visible); }
        foreach (var v in visible) _items.Add(v);
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
            if (!visible.Any(x => x.Id == _selectedItem.Id)) { ClearSelectionAndUi(); }
            else { var current = _allItems.FirstOrDefault(x => x.Id == _selectedItem.Id); if (current != null) SetSingleSelection(current); else ClearSelectionAndUi(); }
        }
        else ClearSelectionAndUi();
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
        var confirm = await DisplayAlert("Reset Subtree", $"Reset '{_selectedItem.Name}' and all its descendants to incomplete?", "Reset", "Cancel"); if (!confirm) return;
        try
        {
            var expectedRevision = await _db.GetListRevisionAsync(listId.Value);
            var (ok, newRev, affected) = await _db.ResetSubtreeAsync(_selectedItem.Id, expectedRevision);
            if (!ok) { await DisplayAlert("Concurrency", "List changed; items refreshed.", "OK"); await RefreshItemsAsync(); return; }
            await RefreshItemsAsync();
        }
        catch (Exception ex) { await DisplayAlert("Reset Failed", ex.Message, "OK"); }
    }

    private void UpdateMoveButtons()
    {
        if (_moveUpButton == null || _moveDownButton == null || _resetSubtreeButton == null) return;
        bool hasSelection = _selectedItem != null; _moveUpButton.IsEnabled = hasSelection; _moveDownButton.IsEnabled = hasSelection; _resetSubtreeButton.IsEnabled = hasSelection; UpdateChildControls();
    }

    private async Task LoadHideCompletedPreferenceForSelectedListAsync()
    {
        var listId = SelectedListId; if (listId == null || _userId == null) return;
        bool? server = null; try { server = await _db.GetListHideCompletedAsync(_userId.Value, listId.Value); } catch { }
        bool value = server ?? Preferences.Get($"LIST_HIDE_COMPLETED_{listId.Value}", false);
        _suppressHideCompletedEvent = true; _hideCompleted = value; if (_hideCompletedSwitch != null) _hideCompletedSwitch.IsToggled = value; _suppressHideCompletedEvent = false;
    }

    private async Task OnHideCompletedToggledAsync(bool hide)
    {
        if (_suppressHideCompletedEvent) return; _hideCompleted = hide; var listId = SelectedListId; if (listId != null) { Preferences.Set($"LIST_HIDE_COMPLETED_{listId.Value}", hide); if (_userId != null) { try { await _db.SetListHideCompletedAsync(_userId.Value, listId.Value, hide); } catch { } } }
        RebuildVisibleItems();
    }
}
