using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using System.Linq; // LINQ
using Microsoft.Maui; // Colors
using Microsoft.Maui.Storage; // Preferences
using System.Diagnostics;

namespace Donezo.Pages;

public partial class DashboardPage
{
    private readonly ObservableCollection<ListRecord> _listsObservable = new(); // owned lists only
    private readonly ObservableCollection<SharedListRecord> _sharedListsObservable = new(); // shared lists only
    private IReadOnlyList<ListRecord> _ownedLists = Array.Empty<ListRecord>();
    private IReadOnlyList<SharedListRecord> _sharedLists = Array.Empty<SharedListRecord>();
    private CollectionView _listsView = null!; // owned lists view
    private CollectionView _sharedListsView = null!; // shared lists view
    private Label _sharedHeading = null!; // heading label
    private Entry _newListEntry = null!;
    private CheckBox _dailyCheck = null!;
    private Button _createListButton = null!;
    private Button _deleteListButton = null!;
    private Button _resetListButton = null!;
    private readonly List<Border> _listItemBorders = new();
    private Label _completedBadge = new() { Text = "Completed", BackgroundColor = Colors.Green, TextColor = Colors.White, Padding = new Thickness(8, 2), IsVisible = false, FontAttributes = FontAttributes.Bold };

    // Redeem share code controls
    private Entry _redeemCodeEntry = null!;
    private Button _redeemCodeButton = null!;
    private Label _sharedEmptyLabel = null!; // placeholder label when no shared lists

    // Helper: ownership of specific list (independent of current selection)
    private bool IsOwnerOfList(int listId) => _ownedLists.Any(o => o.Id == listId);

    private Border BuildListsCard()
    {
        _newListEntry = new Entry { Placeholder = "New list name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _dailyCheck = new CheckBox { VerticalOptions = LayoutOptions.Center };
        _dailyCheck.CheckedChanged += async (s, e) =>
        {
            // Ignore programmatic changes
            if (_suppressDailyEvent) return;
            if (!IsOwnerRole())
            {
                // Suppress recursive event firing while reverting
                _suppressDailyEvent = true;
                // Revert to previous value without triggering another popup
                _dailyCheck.IsChecked = !e.Value;
                _suppressDailyEvent = false;
                await ShowViewerBlockedAsync("changing daily setting");
                return;
            }
            await OnDailyToggledAsync(e.Value);
        };
        var dailyRow = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Daily" }, _dailyCheck } };
        _createListButton = new Button { Text = "Create", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _createListButton.Clicked += async (_, _) => {
            if (_userId == null) return; // creation allowed always for own new list
            var role = GetCurrentListRole();
            // Creating new list is independent of current selection; always allowed for authenticated user.
            await CreateListAsync();
        };
        _deleteListButton = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red };
        _deleteListButton.Clicked += async (_, _) => { if (!IsOwnerRole()) { await ShowViewerBlockedAsync("deleting list"); return; } await DeleteCurrentListAsync(); };
        _resetListButton = new Button { Text = "Reset", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _resetListButton.Clicked += async (_, _) => { if (!IsOwnerRole()) { await ShowViewerBlockedAsync("resetting list"); return; } await ResetCurrentListAsync(); };

        _redeemCodeEntry = new Entry { Placeholder = "Redeem share code", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _redeemCodeEntry.TextChanged += (_, __) => _redeemCodeButton.IsEnabled = !string.IsNullOrWhiteSpace(_redeemCodeEntry.Text);
        _redeemCodeButton = new Button { Text = "Redeem", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = false };
        _redeemCodeButton.Clicked += async (_, __) => await RedeemShareCodeAsync();

        _listsView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _listsObservable,
            ItemTemplate = new DataTemplate(() => CreateListItemTemplateBorder())
        };
        _sharedListsView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _sharedListsObservable,
            ItemTemplate = new DataTemplate(() => CreateListItemTemplateBorder(true))
        };
        WireListSelectionHandlers();

        _sharedHeading = new Label { Text = "Shared with me", Style = (Style)Application.Current!.Resources["SectionSubTitle"], Margin = new Thickness(0, 12, 0, 0), IsVisible = true };
        _sharedEmptyLabel = new Label { Text = "No shared lists yet.", FontSize = 12, TextColor = Colors.Gray, IsVisible = false };

        var listsHeader = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        listsHeader.Add(new Label { Text = "Lists", Style = (Style)Application.Current!.Resources["SectionTitle"] });
        listsHeader.Add(_completedBadge, 1, 0);

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
                    listsHeader,
                    _listsView,
                    _sharedHeading,
                    _sharedEmptyLabel,
                    _sharedListsView,
                    new Label { Text = "Redeem Code", FontAttributes = FontAttributes.Bold },
                    new HorizontalStackLayout { Spacing = 8, Children = { _redeemCodeEntry, _redeemCodeButton } },
                    dailyRow,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton, _deleteListButton, _resetListButton } }
                }
            }
        };
        card.Style = (Style)Application.Current!.Resources["CardBorder"];
        return card;
    }

    private Border CreateListItemTemplateBorder(bool isShared = false)
    {
        var border = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 4)
        };
        var nameLabel = new Label { FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };
        var roleLabel = new Label { FontSize = 12, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center, IsVisible = false };
        var shareBtn = new Button { Text = "Share", FontSize = 12, Padding = new Thickness(10, 4), Style = (Style)Application.Current!.Resources["OutlinedButton"], IsVisible = !isShared };
        shareBtn.Clicked += async (s, e) =>
        {
            // Determine target list from binding context (not relying on current selection)
            if (border.BindingContext is ListRecord lr)
            {
                if (!IsOwnerOfList(lr.Id)) { await ShowViewerBlockedAsync("opening share options"); return; }
                await OpenShareAsync(lr); return;
            }
            if (border.BindingContext is SharedListRecord sl)
            {
                // Shared list: only allow if user is actual owner (should not happen since shared lists exclude owner role)
                if (!IsOwnerOfList(sl.Id)) { await ShowViewerBlockedAsync("opening share options"); return; }
                await OpenShareAsync(new ListRecord(sl.Id, sl.Name, sl.IsDaily));
            }
        };
        border.BindingContextChanged += (_, __) =>
        {
            if (border.BindingContext is SharedListRecord slr)
            {
                nameLabel.Text = slr.Name; roleLabel.Text = slr.Role; roleLabel.IsVisible = true; shareBtn.IsEnabled = false; // cannot share non-owned list
            }
            else if (border.BindingContext is ListRecord lr)
            {
                nameLabel.Text = lr.Name; roleLabel.IsVisible = false; shareBtn.IsEnabled = IsOwnerOfList(lr.Id);
            }
            if (!_listItemBorders.Contains(border)) _listItemBorders.Add(border);
            ApplyListVisual(border);
        };
        var daily = new Border
        {
            BackgroundColor = (Color)Application.Current!.Resources["Primary"], StrokeThickness = 0, Padding = new Thickness(6, 2),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Content = new Label { Text = "Daily", FontSize = 12, TextColor = Colors.White }
        };
        daily.SetBinding(IsVisibleProperty, nameof(ListRecord.IsDaily));
        var tapSelect = new TapGestureRecognizer();
        tapSelect.Tapped += async (s, e) =>
        {
            // Use the binding context and invoke unified handler
            await HandleSelectionChangedAsync(border.BindingContext);
        };
        border.GestureRecognizers.Add(tapSelect);
        border.Content = new HorizontalStackLayout { Spacing = 8, Children = { nameLabel, roleLabel, daily, shareBtn } };
        return border;
    }

    private void WireListSelectionHandlers()
    {
        if (_listsView != null)
            _listsView.SelectionChanged += async (s, e) => await HandleSelectionChangedAsync(e.CurrentSelection.FirstOrDefault());
        if (_sharedListsView != null)
            _sharedListsView.SelectionChanged += async (s, e) => await HandleSelectionChangedAsync(e.CurrentSelection.FirstOrDefault());
    }

    private static bool _traceEnabled = true; // toggle for instrumentation
    private void Trace(string msg)
    {
        if (!_traceEnabled) return;
        try { Debug.WriteLine($"[DashTrace] {DateTime.UtcNow:HH:mm:ss.fff} {msg}"); } catch { }
    }

    private async Task RedeemShareCodeAsync()
    {
        var code = _redeemCodeEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code) || _userId == null) return;
        Trace($"RedeemShareCodeAsync start code={code}");
        try
        {
            var (ok, listId, list, membership) = await _db.RedeemShareCodeByCodeAsync(_userId.Value, code);
            Trace($"RedeemShareCodeAsync result ok={ok} listId={listId} role={membership?.Role}");
            if (!ok || listId == null || list == null || membership == null)
            {
                await DisplayAlert("Redeem", "Invalid or unusable code.", "OK");
                return;
            }
            _redeemCodeEntry.Text = string.Empty;
            _selectedListId = listId;
            await RefreshListsAsync();
            UpdateAllListSelectionVisuals();
        }
        catch (Exception ex)
        {
            Trace($"RedeemShareCodeAsync exception {ex.Message}");
            await DisplayAlert("Redeem", ex.Message, "OK");
        }
        finally { Trace("RedeemShareCodeAsync end"); }
    }

    private bool _isRefreshingLists; // guard against concurrent list refreshes
    private async Task RefreshListsAsync()
    {
        if (_userId == null) { Trace("RefreshListsAsync aborted: no userId"); return; }
        if (_isRefreshingLists) { Trace("RefreshListsAsync skipped: already refreshing"); return; }
        _isRefreshingLists = true;
        var sw = Stopwatch.StartNew();
        Trace("RefreshListsAsync begin");
        try
        {
            var legacyOwned = await _db.GetOwnedListsAsync(_userId.Value);
            Trace($"Owned lists fetched count={legacyOwned.Count}");
            var actuallyOwned = new List<ListRecord>();
            foreach (var lr in legacyOwned)
            {
                try
                {
                    var ownerId = await _db.GetListOwnerUserIdAsync(lr.Id);
                    if (ownerId != null && ownerId.Value == _userId.Value) actuallyOwned.Add(lr);
                }
                catch { }
            }
            _ownedLists = actuallyOwned;
            _sharedLists = await _db.GetSharedListsAsync(_userId.Value);
            Trace($"Shared lists fetched count={_sharedLists.Count}");

            _listsObservable.Clear(); _sharedListsObservable.Clear();
            foreach (var o in _ownedLists) _listsObservable.Add(o);
            foreach (var s in _sharedLists) _sharedListsObservable.Add(s);
            Trace($"Observable counts owned={_listsObservable.Count} shared={_sharedListsObservable.Count}");

            var allIds = _ownedLists.Select(x => x.Id).Concat(_sharedLists.Select(x => x.Id)).ToHashSet();
            if (_selectedListId == null || !allIds.Contains(_selectedListId.Value))
            {
                _selectedListId = _ownedLists.FirstOrDefault()?.Id ?? _sharedLists.FirstOrDefault()?.Id;
                Trace($"SelectedListId adjusted to {_selectedListId}");
            }

            await RefreshItemsAsync();
            UpdateAllListSelectionVisuals();
            SyncDailyCheckboxWithSelectedList();
            ApplyRoleUiRestrictions();
        }
        finally
        {
            sw.Stop();
            Trace($"RefreshListsAsync end elapsed={sw.ElapsedMilliseconds}ms selected={_selectedListId} viewerRole={GetCurrentListRole()}");
            _isRefreshingLists = false;
        }
    }

    private void ApplyRoleUiRestrictions()
    {
        var role = GetCurrentListRole();
        bool isViewer = string.Equals(role, "Viewer", System.StringComparison.OrdinalIgnoreCase);
        // Disable modification controls for viewer
        _deleteListButton.IsEnabled = !isViewer && IsOwnerRole();
        _resetListButton.IsEnabled = !isViewer && IsOwnerRole();
        _dailyCheck.IsEnabled = !isViewer && IsOwnerRole();
        _createListButton.IsEnabled = _userId != null; // creation allowed always
        // Item-level buttons handled in items template via Can* methods.
    }

    private bool _isHandlingSelection; // guard for overlapping selection processing
    // Replace previous async void method with Task-based implementation
    private async Task HandleSelectionChangedAsync(object? context)
    {
        if (_isRefreshingLists) return; // still refreshing lists
        if (_isHandlingSelection) { Trace("HandleSelectionChangedAsync skipped: already handling"); return; }
        _isHandlingSelection = true;
        try
        {
            Trace($"HandleSelectionChangedAsync contextType={context?.GetType().Name}");
            int? previous = _selectedListId;
            if (context is ListRecord lr)
            {
                if (_selectedListId == lr.Id) { Trace("Selection unchanged (owned)"); return; }
                _selectedListId = lr.Id;
            }
            else if (context is SharedListRecord slr)
            {
                if (_selectedListId == slr.Id) { Trace("Selection unchanged (shared)"); return; }
                _selectedListId = slr.Id;
            }
            else { Trace("Selection ignored: unknown context"); return; }
            Trace($"HandleSelectionChangedAsync newSelected={_selectedListId} prev={previous}");
            UpdateAllListSelectionVisuals();
            var sw = Stopwatch.StartNew();
            await RefreshItemsAsync(userInitiated: true);
            sw.Stop();
            Trace($"HandleSelectionChangedAsync RefreshItemsAsync elapsed={sw.ElapsedMilliseconds}ms items={_allItems.Count}");
            SyncDailyCheckboxWithSelectedList();
            ApplyRoleUiRestrictions();
        }
        finally { _isHandlingSelection = false; }
    }

    private async void HandleSelectionChanged(object? context)
    {
        // Deprecated: now using HandleSelectionChangedAsync unified handler
        await HandleSelectionChangedAsync(context);
    }

    private void ApplyListVisual(Border b)
    {
        if (b.BindingContext is ListRecord lr)
        {
            var primary = (Color)Application.Current!.Resources["Primary"]; var dark = Application.Current?.RequestedTheme == AppTheme.Dark; var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"]; bool selected = _selectedListId == lr.Id; b.BackgroundColor = selected ? primary.WithAlpha(0.12f) : baseBg; b.Stroke = selected ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"]; return;
        }
        if (b.BindingContext is SharedListRecord slr)
        {
            var primary = (Color)Application.Current!.Resources["Primary"]; var dark = Application.Current?.RequestedTheme == AppTheme.Dark; var baseBg = (Color)Application.Current!.Resources[dark ? "OffBlack" : "White"]; bool selected = _selectedListId == slr.Id; b.BackgroundColor = selected ? primary.WithAlpha(0.12f) : baseBg; b.Stroke = selected ? primary : (Color)Application.Current!.Resources[dark ? "Gray600" : "Gray100"]; return;
        }
    }
    private void UpdateAllListSelectionVisuals() { foreach (var b in _listItemBorders) ApplyListVisual(b); }

    private void SyncDailyCheckboxWithSelectedList()
    {
        var id = SelectedListId; _suppressDailyEvent = true;
        if (id == null) _dailyCheck.IsChecked = false; else { var lr = _ownedLists.FirstOrDefault(x => x.Id == id) != null ? _ownedLists.First(x => x.Id == id) : (ListRecord?)_sharedLists.Where(x => x.Id == id).Select(x => new ListRecord(x.Id,x.Name,x.IsDaily)).FirstOrDefault(); _dailyCheck.IsChecked = lr?.IsDaily ?? false; }
        _suppressDailyEvent = false;
    }

    private async Task OnDailyToggledAsync(bool isChecked)
    { if (_suppressDailyEvent) return; var id = SelectedListId; if (id == null) return; if (!IsOwnerRole()) { await ShowViewerBlockedAsync("changing daily setting"); return; } await _db.SetListDailyAsync(id.Value, isChecked); await RefreshListsAsync(); }

    private async Task CreateListAsync()
    { if (_userId == null) return; var name = _newListEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked); _newListEntry.Text = string.Empty; _dailyCheck.IsChecked = false; await RefreshListsAsync(); }

    private async Task DeleteCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; if (!IsOwnerRole()) { await ShowViewerBlockedAsync("deleting list"); return; } var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel"); if (!confirm) return; if (await _db.DeleteListAsync(id.Value)) { await RefreshListsAsync(); _items.Clear(); _allItems.Clear(); UpdateCompletedBadge(); } }

    private async Task ResetCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; if (!IsOwnerRole()) { await ShowViewerBlockedAsync("resetting list"); return; } await _db.ResetListAsync(id.Value); await RefreshItemsAsync(); UpdateCompletedBadge(); }

    private void UpdateCompletedBadge()
    { if (_allItems.Count == 0) { _completedBadge.IsVisible = false; return; } _completedBadge.IsVisible = _allItems.All(i => i.IsCompleted); }

    private View BuildHideCompletedPreferenceRow()
    { _hideCompletedSwitch = new Microsoft.Maui.Controls.Switch(); _hideCompletedSwitch.Toggled += async (s, e) => await OnHideCompletedToggledAsync(e.Value); return new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Hide Completed" }, _hideCompletedSwitch } }; }

    // Removed erroneous file-scope MessagingCenter subscription that caused build errors.
    // Ownership transfer handling is defined in DashboardPage.OnAppearing in DashboardPage.cs.
}
