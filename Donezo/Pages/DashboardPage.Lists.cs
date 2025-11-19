using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using System.Linq; // LINQ
using Microsoft.Maui; // Colors
using Microsoft.Maui.Storage; // Preferences

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

    private Switch _hideCompletedSwitch = null!;
    private Label _emptyFilteredLabel = null!;

    // Redeem share code controls
    private Entry _redeemCodeEntry = null!;
    private Button _redeemCodeButton = null!;

    private Border BuildListsCard()
    {
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
        _listsView.SelectionChanged += async (s, e) => HandleSelectionChanged(e.CurrentSelection.FirstOrDefault());

        _sharedListsView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _sharedListsObservable,
            ItemTemplate = new DataTemplate(() => CreateListItemTemplateBorder(true))
        };
        _sharedListsView.SelectionChanged += async (s, e) => HandleSelectionChanged(e.CurrentSelection.FirstOrDefault());

        _sharedHeading = new Label { Text = "Shared With Me", Style = (Style)Application.Current!.Resources["SectionSubTitle"], Margin = new Thickness(0, 12, 0, 0), IsVisible = false };

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
        border.BindingContextChanged += (_, __) =>
        {
            if (border.BindingContext is SharedListRecord slr)
            {
                nameLabel.Text = slr.Name; roleLabel.Text = slr.Role; roleLabel.IsVisible = true;
            }
            else if (border.BindingContext is ListRecord lr)
            {
                nameLabel.Text = lr.Name; roleLabel.IsVisible = false;
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
        var shareBtn = new Button { Text = "Share", FontSize = 12, Padding = new Thickness(10, 4), Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        shareBtn.Clicked += async (s, e) =>
        {
            if (border.BindingContext is ListRecord lr) await OpenShareAsync(lr);
            else if (border.BindingContext is SharedListRecord sl) await OpenShareAsync(new ListRecord(sl.Id, sl.Name, sl.IsDaily));
        };
        var tapSelect = new TapGestureRecognizer();
        tapSelect.Tapped += (s, e) => HandleSelectionChanged(border.BindingContext);
        border.GestureRecognizers.Add(tapSelect);
        border.Content = new HorizontalStackLayout { Spacing = 8, Children = { nameLabel, roleLabel, daily, shareBtn } };
        return border;
    }

    private async void HandleSelectionChanged(object? context)
    {
        if (context is ListRecord lr)
        {
            if (_selectedListId == lr.Id) return;
            _selectedListId = lr.Id;
        }
        else if (context is SharedListRecord slr)
        {
            if (_selectedListId == slr.Id) return;
            _selectedListId = slr.Id;
        }
        else return;
        UpdateAllListSelectionVisuals();
        await RefreshItemsAsync();
        SyncDailyCheckboxWithSelectedList();
    }

    private async Task RedeemShareCodeAsync()
    {
        var code = _redeemCodeEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(code) || _userId == null) return;
        try
        {
            var (ok, listId, list, membership) = await _db.RedeemShareCodeByCodeAsync(_userId.Value, code);
            if (!ok || listId == null || list == null || membership == null)
            { await DisplayAlert("Redeem", "Invalid or unusable code.", "OK"); return; }
            _redeemCodeEntry.Text = string.Empty;
            await RefreshListsAsync();
            _selectedListId = listId;
            UpdateAllListSelectionVisuals();
            await RefreshItemsAsync();
        }
        catch (Exception ex)
        { await DisplayAlert("Redeem", ex.Message, "OK"); }
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

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        _ownedLists = await _db.GetOwnedListsAsync(_userId.Value);
        _sharedLists = await _db.GetSharedListsAsync(_userId.Value);
        _listsObservable.Clear();
        _sharedListsObservable.Clear();
        foreach (var o in _ownedLists) _listsObservable.Add(o);
        foreach (var s in _sharedLists) _sharedListsObservable.Add(s);
        _sharedHeading.IsVisible = _sharedListsObservable.Count > 0;
        _sharedListsView.IsVisible = _sharedListsObservable.Count > 0;
        var allIds = _ownedLists.Select(x => x.Id).Concat(_sharedLists.Select(x => x.Id)).ToHashSet();
        if (_selectedListId == null || !allIds.Contains(_selectedListId.Value)) _selectedListId = _ownedLists.FirstOrDefault()?.Id ?? _sharedLists.FirstOrDefault()?.Id;
        await RefreshItemsAsync();
        UpdateAllListSelectionVisuals();
    }

    private void SyncDailyCheckboxWithSelectedList()
    {
        var id = SelectedListId; _suppressDailyEvent = true;
        if (id == null) _dailyCheck.IsChecked = false; else { var lr = _ownedLists.FirstOrDefault(x => x.Id == id) != null ? _ownedLists.First(x => x.Id == id) : (ListRecord?)_sharedLists.Where(x => x.Id == id).Select(x => new ListRecord(x.Id,x.Name,x.IsDaily)).FirstOrDefault(); _dailyCheck.IsChecked = lr?.IsDaily ?? false; }
        _suppressDailyEvent = false;
    }

    private async Task OnDailyToggledAsync(bool isChecked)
    { if (_suppressDailyEvent) return; var id = SelectedListId; if (id == null) return; await _db.SetListDailyAsync(id.Value, isChecked); await RefreshListsAsync(); }

    private async Task CreateListAsync()
    { if (_userId == null) return; var name = _newListEntry.Text?.Trim(); if (string.IsNullOrWhiteSpace(name)) return; await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked); _newListEntry.Text = string.Empty; _dailyCheck.IsChecked = false; await RefreshListsAsync(); }

    private async Task DeleteCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel"); if (!confirm) return; if (await _db.DeleteListAsync(id.Value)) { await RefreshListsAsync(); _items.Clear(); _allItems.Clear(); UpdateCompletedBadge(); } }

    private async Task ResetCurrentListAsync()
    { var id = SelectedListId; if (id == null) return; await _db.ResetListAsync(id.Value); await RefreshItemsAsync(); UpdateCompletedBadge(); }

    private void UpdateCompletedBadge()
    { if (_allItems.Count == 0) { _completedBadge.IsVisible = false; return; } _completedBadge.IsVisible = _allItems.All(i => i.IsCompleted); }

    private View BuildHideCompletedPreferenceRow()
    { _hideCompletedSwitch = new Switch(); _hideCompletedSwitch.Toggled += async (s, e) => await OnHideCompletedToggledAsync(e.Value); return new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Hide Completed" }, _hideCompletedSwitch } }; }
}
