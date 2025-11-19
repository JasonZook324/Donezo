using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using System.Linq; // LINQ
using Microsoft.Maui; // Colors
using Microsoft.Maui.Storage; // Preferences

namespace Donezo.Pages;

public partial class DashboardPage
{
    // Forward declarations for cross-partial fields (defined in Items partial)
    // These are declared in Items partial: _items, _allItems, UpdateCompletedBadge(), RefreshItemsAsync(), etc.
    // Ensure compiler sees signatures here via partial methods if needed.

    // LISTS STATE -------------------------------------------------------
    private readonly ObservableCollection<ListRecord> _listsObservable = new();
    private IReadOnlyList<ListRecord> _lists = Array.Empty<ListRecord>();
    private CollectionView _listsView = null!; // selection view
    private Entry _newListEntry = null!;
    private CheckBox _dailyCheck = null!;
    private Button _createListButton = null!;
    private Button _deleteListButton = null!;
    private Button _resetListButton = null!;
    private readonly List<Border> _listItemBorders = new(); // visual tracking
    private Label _completedBadge = new() { Text = "Completed", BackgroundColor = Colors.Green, TextColor = Colors.White, Padding = new Thickness(8, 2), IsVisible = false, FontAttributes = FontAttributes.Bold };

    // Hide Completed preference controls (shared with items filtering)
    private Switch _hideCompletedSwitch = null!;
    private Label _emptyFilteredLabel = null!; // message placeholder (populated in items partial)

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
                    if (border.BindingContext is ListRecord lr) await OpenShareAsync(lr);
                };
                var tapSelect = new TapGestureRecognizer();
                tapSelect.Tapped += (s,e)=>{
                    if (border.BindingContext is ListRecord lr)
                    {
                        if (_selectedListId == lr.Id) return;
                        _selectedListId = lr.Id;
                        UpdateAllListSelectionVisuals();
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
            if (_selectedListId == lr.Id) return;
            _selectedListId = lr.Id;
            UpdateAllListSelectionVisuals();
            await RefreshItemsAsync();
            SyncDailyCheckboxWithSelectedList();
        };

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
                    dailyRow,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton, _deleteListButton, _resetListButton } }
                }
            }
        };
        card.Style = (Style)Application.Current!.Resources["CardBorder"];
        return card;
    }

    // Visual helpers reused after selection changes
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
        foreach (var b in _listItemBorders) ApplyListVisual(b);
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
        UpdateAllListSelectionVisuals();
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

    private void UpdateCompletedBadge()
    { if (_allItems.Count == 0) { _completedBadge.IsVisible = false; return; } _completedBadge.IsVisible = _allItems.All(i => i.IsCompleted); }

    // Preference row is in preferences card but we build content here to keep related logic local
    private View BuildHideCompletedPreferenceRow()
    {
        _hideCompletedSwitch = new Switch();
        _hideCompletedSwitch.Toggled += async (s, e) => await OnHideCompletedToggledAsync(e.Value);
        return new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Hide Completed" }, _hideCompletedSwitch } };
    }
}
