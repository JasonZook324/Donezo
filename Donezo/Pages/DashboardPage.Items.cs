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

    // Rebuild visible (flat list) respecting hide-completed & expansion
    private void RebuildVisibleItems()
    {
        var visible = new List<ItemVm>();
        foreach (var root in _allItems.Where(x => x.ParentId == null).OrderBy(x => x.SortKey))
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
    { visited ??= new(); if (node==null) return; if (!visited.Add(node.Id) || depth>64) return; target.Add(node); if (!node.IsExpanded) return; foreach(var c in node.Children.OrderBy(c=>c.SortKey)) TraceAdd(c,target,visited,depth+1); }
    private void TraceAddFiltered(ItemVm node, List<ItemVm> target, HashSet<int>? visited=null, int depth=0)
    { visited ??= new(); if (node==null) return; if (!visited.Add(node.Id) || depth>64) return; if (_hideCompleted && node.IsCompleted) return; target.Add(node); if (!node.IsExpanded) return; foreach(var c in node.Children.OrderBy(c=>c.SortKey)) TraceAddFiltered(c,target,visited,depth+1); }

    // Build card with controls
    private Border BuildItemsCard()
    {
        _emptyFilteredLabel ??= new Label { IsVisible=false, TextColor=Colors.Gray, FontSize=12 };
        _statsLabel ??= new Label { FontSize=12, TextColor=(Color)Application.Current!.Resources[Application.Current!.RequestedTheme==AppTheme.Dark?"Gray300":"Gray600"], HorizontalTextAlignment=TextAlignment.End };
        _itemViewTemplate = CreateItemTemplate();
        _itemsView = new CollectionView { ItemsSource=_items, SelectionMode=SelectionMode.None, ItemTemplate=_itemViewTemplate, ItemsUpdatingScrollMode=ItemsUpdatingScrollMode.KeepScrollOffset };
        _moveUpButton = new Button { Text="Move Up", Style=(Style)Application.Current!.Resources["OutlinedButton"], FontSize=12, IsEnabled=false };
        _moveUpButton.Clicked += async (_,__) => { if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } await MoveSelectedAsync(-1); };
        _moveDownButton = new Button { Text="Move Down", Style=(Style)Application.Current!.Resources["OutlinedButton"], FontSize=12, IsEnabled=false };
        _moveDownButton.Clicked += async (_,__) => { if (!CanReorderItems()) { await ShowViewerBlockedAsync("reordering items"); return; } await MoveSelectedAsync(1); };
        _resetSubtreeButton = new Button { Text="Reset Subtree", Style=(Style)Application.Current!.Resources["OutlinedButton"], FontSize=12, IsEnabled=false };
        _resetSubtreeButton.Clicked += async (_,__) => { if (!CanResetSubtree()) { await ShowViewerBlockedAsync("resetting subtree"); return; } await ResetSelectedSubtreeAsync(); };
        _hideCompletedSwitch = new Switch { IsToggled=_hideCompleted }; _hideCompletedSwitch.Toggled += async (_,e)=> await OnHideCompletedToggledAsync(e.Value);
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(400), () => { UpdateMoveButtons(); return true; });
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing=8 };
        header.Add(new Label { Text="Items", Style=(Style)Application.Current!.Resources["SectionTitle"], VerticalTextAlignment=TextAlignment.Center },0,0);
        header.Add(_statsLabel,1,0); header.Add(_moveUpButton,2,0); header.Add(_moveDownButton,3,0); header.Add(_resetSubtreeButton,4,0);
        var filterRow = new HorizontalStackLayout { Spacing=8, Children={ new Label { Text="Hide Completed" }, _hideCompletedSwitch } };
        _openNewItemButton = new Button { Text = "+ New Item", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _openNewItemButton.Clicked += (_,__) => ShowNewItemOverlay();
        var newButtonRow = new HorizontalStackLayout { Children = { _openNewItemButton }, HorizontalOptions = LayoutOptions.Start };
        var card = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) }, Padding = new Thickness(6,0,4,0), Content = new VerticalStackLayout { Spacing=12, Padding=16, Children = { newButtonRow, header, filterRow, _emptyFilteredLabel, _itemsView } } };
        card.Style = (Style)Application.Current!.Resources["CardBorder"]; return card;
    }

    private DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var card = new Border { StrokeThickness=1, StrokeShape=new RoundRectangle { CornerRadius=new CornerRadius(10)}, Padding=new Thickness(6,0,4,0)};
            card.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelBorderGapConverter()));
            var grid = new Grid { Padding=new Thickness(4,4), ColumnDefinitions={ new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(28)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
            var badge = new Label { Margin=new Thickness(4,0), VerticalTextAlignment=TextAlignment.Center, FontAttributes=FontAttributes.Bold };
            badge.SetBinding(Label.TextProperty, new Binding(".", converter:new LevelBadgeConverter()));
            badge.SetBinding(Label.TextColorProperty, new Binding(".", converter:new LevelAccentColorConverter())); grid.Add(badge,0,0);
            void OnDragStarting(object? s, DragStartingEventArgs e){ if (!CanDragItems()) return; if (card.BindingContext is ItemVm vm){ _holdCts?.Cancel(); vm.IsPreDrag=false; vm.IsDragging=true; ApplyItemCardStyle(card,vm); _dragItem=vm; _pendingDragVm=vm; try{ e.Data.Properties["ItemId"] = vm.Id;}catch{} } }
            var drag = new DragGestureRecognizer{ CanDrag=true }; drag.DragStarting += OnDragStarting; card.GestureRecognizers.Add(drag);
            // Always reserve expand column space; hide chevrons when no children instead of collapsing column.
            var expandContainer = new Grid { WidthRequest=28, HeightRequest=28 };
            var chevronRight = new Microsoft.Maui.Controls.Shapes.Path { Stroke=new SolidColorBrush((Color)Application.Current!.Resources["Primary"]), StrokeThickness=2, HorizontalOptions=LayoutOptions.Center, VerticalOptions=LayoutOptions.Center, Data=(Geometry)new PathGeometryConverter().ConvertFromInvariantString("M8 6 L16 12 L8 18") };
            chevronRight.SetBinding(IsVisibleProperty, new MultiBinding { Bindings = { new Binding("HasChildren"), new Binding("IsExpanded") }, Converter = new ChevronRightVisibilityConverter() });
            var chevronDown = new Microsoft.Maui.Controls.Shapes.Path { Stroke=new SolidColorBrush((Color)Application.Current!.Resources["Primary"]), StrokeThickness=2, HorizontalOptions=LayoutOptions.Center, VerticalOptions=LayoutOptions.Center, Data=(Geometry)new PathGeometryConverter().ConvertFromInvariantString("M6 9 L12 15 L18 9") };
            chevronDown.SetBinding(IsVisibleProperty, new MultiBinding { Bindings = { new Binding("HasChildren"), new Binding("IsExpanded") }, Converter = new ChevronDownVisibilityConverter() });
            expandContainer.Children.Add(chevronRight); expandContainer.Children.Add(chevronDown);
            var expandTap = new TapGestureRecognizer(); expandTap.Tapped += async (_,__) => { if (card.BindingContext is ItemVm vm){ if (!vm.HasChildren) return; bool collapsing = vm.IsExpanded; if (collapsing) { CollapseIncremental(vm); if (_selectedItem!=null && IsUnder(vm,_selectedItem)) SetSingleSelection(vm); } else { ExpandIncremental(vm); } _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3); _recentLocalMutationUtc = DateTime.UtcNow; if (_userId!=null) await _db.SetItemExpandedAsync(_userId.Value, vm.Id, vm.IsExpanded); } }; expandContainer.GestureRecognizers.Add(expandTap); grid.Add(expandContainer,1,0);
            var nameLabel = new Label { VerticalTextAlignment=TextAlignment.Center }; nameLabel.SetBinding(Label.TextProperty, "Name"); nameLabel.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelIndentConverter())); nameLabel.SetBinding(IsVisibleProperty, new Binding("IsRenaming", converter:new InvertBoolConverter()));
            var nameEntry = new Entry { HeightRequest=32, FontSize=14 }; nameEntry.SetBinding(Entry.TextProperty, "EditableName", BindingMode.TwoWay); nameEntry.SetBinding(View.MarginProperty, new Binding("Level", converter:new LevelIndentConverter())); nameEntry.SetBinding(IsVisibleProperty, "IsRenaming");
            var nameContainer = new HorizontalStackLayout { Spacing=4, VerticalOptions=LayoutOptions.Center, Children={ nameLabel, nameEntry } }; grid.Add(nameContainer,2,0);
            var statusStack = new HorizontalStackLayout { Spacing=4, VerticalOptions=LayoutOptions.Center };
            var completedInfoLabel = new Label { FontSize=11, TextColor=Color.FromArgb("#008A2E"), FontAttributes=FontAttributes.Italic, LineBreakMode=LineBreakMode.TailTruncation, VerticalTextAlignment=TextAlignment.Center }; completedInfoLabel.SetBinding(Label.TextProperty,"CompletedInfo"); completedInfoLabel.SetBinding(Label.IsVisibleProperty,"ShowCompletedInfo");
            var check = new CheckBox(); check.SetBinding(CheckBox.IsCheckedProperty,"IsCompleted"); check.CheckedChanged += async (_,e)=>{ if (_suppressCompletionEvent) return; if (!CanCompleteItems()){ _suppressCompletionEvent=true; if (check.BindingContext is ItemVm vmPrior){ var prior=!e.Value; vmPrior.IsCompleted=prior; check.IsChecked=prior; } _suppressCompletionEvent=false; await ShowViewerBlockedAsync("changing completion state"); return; } if (check.BindingContext is ItemVm vmC) await ToggleItemCompletionInlineAsync(vmC, e.Value); }; check.IsEnabled = CanCompleteItems();
            var partialIndicator = new Label { FontAttributes=FontAttributes.Bold, TextColor=Colors.Orange, FontSize=14, WidthRequest=12, HorizontalTextAlignment=TextAlignment.Center, VerticalTextAlignment=TextAlignment.Center }; partialIndicator.SetBinding(Label.TextProperty,"PartialGlyph");
            statusStack.Children.Add(completedInfoLabel); statusStack.Children.Add(check); statusStack.Children.Add(partialIndicator); grid.Add(statusStack,3,0);
            var deleteBtn = new Button { Text="Delete", Style=(Style)Application.Current!.Resources["OutlinedButton"], TextColor=Colors.Red, FontSize=12, Padding=new Thickness(6,2) }; deleteBtn.Clicked += async (_,__) => { if (!CanDeleteItems()){ await ShowViewerBlockedAsync("deleting items"); return; } if (deleteBtn.BindingContext is ItemVm vmD) await DeleteItemInlineAsync(vmD); }; deleteBtn.IsEnabled = CanDeleteItems(); grid.Add(deleteBtn,4,0);
            var renameBtn = new Button { Style=(Style)Application.Current!.Resources["OutlinedButton"], FontSize=12, Padding=new Thickness(6,2) }; renameBtn.SetBinding(Button.TextProperty, new Binding("IsRenaming", converter:new BoolToStringConverter { TrueText="Save", FalseText="Rename" })); renameBtn.Clicked += async (_,__) => { if (!CanRenameItems()){ await ShowViewerBlockedAsync("renaming items"); return; } if (renameBtn.BindingContext is ItemVm vmR){ if (!vmR.IsRenaming){ vmR.EditableName = vmR.Name; vmR.IsRenaming=true; } else { var newName = vmR.EditableName?.Trim(); if (string.IsNullOrWhiteSpace(newName) || newName==vmR.Name){ vmR.IsRenaming=false; return; } try { var rev = await _db.GetListRevisionAsync(vmR.ListId); var res = await _db.RenameItemAsync(vmR.Id,newName,rev); if (!res.Ok){ await DisplayAlert("Rename","Concurrency mismatch; items refreshed.","OK"); await RefreshItemsAsync(true); return; } vmR.Name=newName; vmR.IsRenaming=false; vmR.RecalcState(); RebuildVisibleItems(); _lastRevision=res.NewRevision; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} catch(Exception ex){ await DisplayAlert("Rename Failed", ex.Message, "OK"); await RefreshItemsAsync(true);} } } }; renameBtn.IsEnabled = CanRenameItems(); grid.Add(renameBtn,5,0);
            var cancelBtn = new Button { Text="Cancel", Style=(Style)Application.Current!.Resources["OutlinedButton"], FontSize=12, Padding=new Thickness(6,2) }; cancelBtn.SetBinding(IsVisibleProperty,"IsRenaming"); cancelBtn.Clicked += (_,__) => { if (cancelBtn.BindingContext is ItemVm vmCxl){ vmCxl.IsRenaming=false; vmCxl.EditableName=vmCxl.Name; } }; grid.Add(cancelBtn,6,0);
            card.Content = grid; card.Style=(Style)Application.Current!.Resources["CardBorder"]; card.BindingContextChanged += (_,__) => { if (card.BindingContext is ItemVm vm) ApplyItemCardStyle(card,vm); };
            return new VerticalStackLayout { Spacing=0, Children={ card, new BoxView { HeightRequest=4, Opacity=0 } } };
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
            foreach (var c in n.Children.OrderBy(c=>c.SortKey))
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
    private async Task AddItemAsync(){ var listId=_selectedListId; if(listId==null) return; var name=_newItemEntry.Text?.Trim(); if(string.IsNullOrWhiteSpace(name)) return; int newId; try { newId=await _db.AddItemAsync(listId.Value,name); } catch { return; } _newItemEntry.Text=string.Empty; try{ var rec=await _db.GetItemAsync(newId); if(rec!=null){ var vm=CreateVmFromRecord(rec); // DB already assigns max sparse order placing item at bottom
                _allItems.Add(vm); vm.RecalcState(); RebuildVisibleItems();
                // If newly added root item appears at top of visible list, update DB order to reflect that position.
                if (vm.ParentId==null && _items.Count>0 && ReferenceEquals(_items[0], vm))
                {
                    // Determine minimum current order among other root items (excluding new one)
                    var otherRoots = _allItems.Where(r=>r.ParentId==null && r.Id!=vm.Id).ToList();
                    if (otherRoots.Count>0)
                    {
                        var minOrder = otherRoots.Min(r=>r.Order);
                        if (vm.Order >= minOrder) // move it before the previous first root
                        {
                            try {
                                var expected = await _db.GetListRevisionAsync(listId.Value);
                                int newOrder = minOrder - 1; // simple step earlier than first
                                var res = await _db.SetItemOrderAsync(vm.Id,newOrder,expected);
                                if (res.Ok)
                                {
                                    vm.Order = newOrder;
                                    // Resort in-memory roots and rebuild visible list
                                    var parent = vm.ParentId==null ? null : _allItems.FirstOrDefault(x=>x.Id==vm.ParentId);
                                    if (parent==null)
                                    {
                                        // resort root children by Order
                                        foreach(var root in _allItems.Where(r=>r.ParentId==null))
                                            root.Children.Sort((a,b)=>a.Order.CompareTo(b.Order));
                                    }
                                    RebuildVisibleItems();
                                    _lastRevision = res.NewRevision;
                                    _skipAutoRefreshUntil = DateTime.UtcNow.AddSeconds(3);
                                }
                            } catch { /* ignore and keep optimistic ordering */ }
                        }
                    }
                }
                UpdateCompletedSummary(); _recentLocalMutationUtc=DateTime.UtcNow; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(5);} else { /* fallback full refresh */ await RefreshItemsAsync(true);} } catch{ await RefreshItemsAsync(true);} HideNewItemOverlay(clear:true); }

    // Child item creation removed per requirement.
    private async Task DeleteItemInlineAsync(ItemVm vm){ var listId=_selectedListId; if(listId==null) return; var expected=await _db.GetListRevisionAsync(listId.Value); var result=await _db.DeleteItemAsync(vm.Id,expected); if(result.Ok){ RemoveLocalSubtree(vm); RebuildVisibleItems(); UpdateCompletedSummary(); _recentLocalMutationUtc=DateTime.UtcNow; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} else await RefreshItemsAsync(true); }
    private async Task MoveSelectedAsync(int delta){ if(_selectedItem==null) return; var listId=_selectedListId; if(listId==null) return; var siblings=_allItems.Where(x=>x.ParentId==_selectedItem.ParentId).OrderBy(x=>x.Order).ThenBy(x=>x.Id).ToList(); int idx=siblings.FindIndex(x=>x.Id==_selectedItem.Id); if(idx<0) return; int target=idx+delta; if(target<0||target>=siblings.Count) return; int newOrder = delta<0 ? siblings[target].Order-1 : siblings[target].Order+1; try{ var expected=await _db.GetListRevisionAsync(listId.Value); var res=await _db.SetItemOrderAsync(_selectedItem.Id,newOrder,expected); if(!res.Ok){ await RefreshItemsAsync(true); return;} _selectedItem.Order=newOrder; if(_selectedItem.ParentId!=null){ var parent=_allItems.FirstOrDefault(x=>x.Id==_selectedItem.ParentId.Value); parent?.Children.Sort((a,b)=>a.Order.CompareTo(b.Order)); } RebuildVisibleItems(); _lastRevision=res.NewRevision; _skipAutoRefreshUntil=DateTime.UtcNow.AddSeconds(3);} catch{ await RefreshItemsAsync(true);} UpdateMoveButtons(); }
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
}
