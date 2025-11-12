using Donezo.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;

namespace Donezo.Pages;

public class DashboardPage : ContentPage
{
    private readonly INeonDbService _db;
    private readonly string _username;
    private int? _userId;
    private Picker _listsPicker = null!;
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

    private readonly ObservableCollection<ItemRecord> _items = new();
    private IReadOnlyList<ListRecord> _lists = Array.Empty<ListRecord>();

    private readonly Label _completedBadge = new() { Text = "Completed", BackgroundColor = Colors.Green, TextColor = Colors.White, Padding = new Thickness(8,2), IsVisible = false, FontAttributes = FontAttributes.Bold };

    private bool _suppressDailyEvent;
    private bool _suppressThemeEvent;
    private int? SelectedListId => _listsPicker.SelectedItem is ListRecord lr ? lr.Id : null;

    public DashboardPage(INeonDbService db, string username)
    {
        _db = db;
        _username = username;
        Title = "Dashboard";
        BuildUi();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private View Header()
    {
        var grid = new Grid
        {
            Padding = new Thickness(20, 30, 20, 10),
            BackgroundColor = (Color)Application.Current!.Resources["Primary"],
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var title = new Label
        {
            Text = $"Welcome, {_username}",
            TextColor = Colors.White,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold
        };
        var subtitle = new Label
        {
            Text = "Manage your lists",
            TextColor = Colors.White,
            Opacity = 0.85
        };

        grid.Add(title);
        grid.Add(subtitle, 0, 1);
        return grid;
    }

    private void BuildUi()
    {
        _listsPicker = new Picker { Title = "Select List" };
        _listsPicker.ItemDisplayBinding = new Binding("Name");
        _listsPicker.SelectedIndexChanged += async (_, _) =>
        {
            await RefreshItemsAsync();
            SyncDailyCheckboxWithSelectedList();
        };

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

        _newItemEntry = new Entry { Placeholder = "New item name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _addItemButton = new Button { Text = "+ Add", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _addItemButton.Clicked += async (_, _) => await AddItemAsync();

        _themeLabel = new Label { Text = "Light", VerticalTextAlignment = TextAlignment.Center };
        _themeSwitch = new Switch();
        _themeSwitch.Toggled += async (s, e) => await OnThemeToggledAsync(e.Value);
        var themeRow = new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Theme" }, _themeLabel, _themeSwitch } };

        _itemsView = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var card = new Frame { Style = (Style)Application.Current!.Resources["CardFrame"] };
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto)
                    }
                };

                var nameLabel = new Label { VerticalTextAlignment = TextAlignment.Center };
                nameLabel.SetBinding(Label.TextProperty, nameof(ItemRecord.Name));

                var check = new CheckBox { HorizontalOptions = LayoutOptions.End };
                check.SetBinding(CheckBox.IsCheckedProperty, nameof(ItemRecord.IsCompleted));
                check.CheckedChanged += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ItemRecord ir)
                    {
                        await _db.SetItemCompletedAsync(ir.Id, e.Value);
                        ir.IsCompleted = e.Value;
                        UpdateCompletedBadge();
                    }
                };

                var deleteBtn = new Button
                {
                    Text = "Delete",
                    Style = (Style)Application.Current!.Resources["OutlinedButton"],
                    TextColor = Colors.Red,
                    FontSize = 12,
                    Padding = new Thickness(6,2),
                    MinimumWidthRequest = 52
                };
                deleteBtn.Clicked += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ItemRecord ir)
                    {
                        if (await _db.DeleteItemAsync(ir.Id))
                        {
                            _items.Remove(ir);
                            UpdateCompletedBadge();
                        }
                    }
                };

                grid.Add(nameLabel);
                grid.Add(check, 1, 0);
                grid.Add(deleteBtn, 2, 0);
                card.Content = grid;
                return card;
            })
        };

        var listsHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        listsHeader.Add(new Label { Text = "Lists", Style = (Style)Application.Current!.Resources["SectionTitle"] });
        listsHeader.Add(_completedBadge, 1, 0);

        var listsCard = new Frame
        {
            Style = (Style)Application.Current!.Resources["CardFrame"],
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    listsHeader,
                    _listsPicker,
                    dailyRow,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton, _deleteListButton, _resetListButton } }
                }
            }
        };

        var itemsCard = new Frame
        {
            Style = (Style)Application.Current!.Resources["CardFrame"],
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Items", Style = (Style)Application.Current!.Resources["SectionTitle"] },
                    _itemsView,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newItemEntry, _addItemButton } }
                }
            }
        };

        var prefsCard = new Frame
        {
            Style = (Style)Application.Current!.Resources["CardFrame"],
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Preferences", Style = (Style)Application.Current!.Resources["SectionTitle"] },
                    themeRow
                }
            }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        root.Add(Header(), 0, 0);
        root.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 10),
                Spacing = 16,
                Children = { prefsCard, listsCard, itemsCard }
            }
        }, 0, 1);

        Content = root;
    }

    private async Task InitializeAsync()
    {
        _userId = await _db.GetUserIdAsync(_username);
        if (_userId == null)
        {
            await DisplayAlert("Error", "User not found.", "OK");
            return;
        }
        await LoadThemePreferenceAsync();
        await RefreshListsAsync();
    }

    private async Task LoadThemePreferenceAsync()
    {
        if (_userId == null) return;
        var dark = await _db.GetUserThemeDarkAsync(_userId.Value);
        _suppressThemeEvent = true;
        _themeSwitch.IsToggled = dark ?? false;
        ApplyTheme(_themeSwitch.IsToggled);
        _suppressThemeEvent = false;
    }

    private async Task OnThemeToggledAsync(bool dark)
    {
        if (_suppressThemeEvent) return;
        ApplyTheme(dark);
        if (_userId != null)
        {
            await _db.SetUserThemeDarkAsync(_userId.Value, dark);
        }
    }

    private void ApplyTheme(bool dark)
    {
        _themeLabel.Text = dark ? "Dark" : "Light";
        if (Application.Current is App app)
        {
            app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        }
    }

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        _lists = await _db.GetListsAsync(_userId.Value);
        _listsPicker.ItemsSource = _lists.Count == 0 ? new List<ListRecord>() : _lists.ToList();
        _listsPicker.SelectedIndex = _lists.Count > 0 ? 0 : -1;
        SyncDailyCheckboxWithSelectedList();
    }

    private void SyncDailyCheckboxWithSelectedList()
    {
        var id = SelectedListId;
        _suppressDailyEvent = true;
        if (id == null)
        {
            _dailyCheck.IsChecked = false;
        }
        else
        {
            var lr = _lists.FirstOrDefault(x => x.Id == id);
            _dailyCheck.IsChecked = lr?.IsDaily ?? false;
        }
        _suppressDailyEvent = false;
    }

    private async Task OnDailyToggledAsync(bool isChecked)
    {
        if (_suppressDailyEvent) return;
        var id = SelectedListId;
        if (id == null) return; // not tied to a list yet
        await _db.SetListDailyAsync(id.Value, isChecked);
        // Optionally update local cache so the value stays in sync without full refresh
        var lr = _lists.FirstOrDefault(x => x.Id == id.Value);
        if (lr != null)
        {
            var m = _lists.ToList();
            var idx = m.FindIndex(x => x.Id == id.Value);
            if (idx >= 0)
            {
                m[idx] = new ListRecord(lr.Id, lr.Name, isChecked);
                _lists = m;
            }
        }
    }

    private async Task CreateListAsync()
    {
        if (_userId == null) return;
        var name = _newListEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        await _db.CreateListAsync(_userId.Value, name, _dailyCheck.IsChecked);
        _newListEntry.Text = string.Empty;
        _dailyCheck.IsChecked = false;
        await RefreshListsAsync();
    }

    private async Task DeleteCurrentListAsync()
    {
        var id = SelectedListId;
        if (id == null) return;
        var confirm = await DisplayAlert("Delete List", "Are you sure? This will remove all items.", "Delete", "Cancel");
        if (!confirm) return;
        if (await _db.DeleteListAsync(id.Value))
        {
            await RefreshListsAsync();
            _items.Clear();
            UpdateCompletedBadge();
        }
    }

    private async Task ResetCurrentListAsync()
    {
        var id = SelectedListId;
        if (id == null) return;
        await _db.ResetListAsync(id.Value);
        await RefreshItemsAsync();
        UpdateCompletedBadge();
    }

    private async Task RefreshItemsAsync()
    {
        _items.Clear();
        var listId = SelectedListId;
        if (listId == null)
        {
            UpdateCompletedBadge();
            return;
        }
        var items = await _db.GetItemsAsync(listId.Value);
        foreach (var i in items)
            _items.Add(new ItemRecord(i.Id, i.Name, i.IsCompleted));
        UpdateCompletedBadge();
    }

    private async Task AddItemAsync()
    {
        var listId = SelectedListId;
        if (listId == null) return;
        var name = _newItemEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        await _db.AddItemAsync(listId.Value, name);
        _newItemEntry.Text = string.Empty;
        await RefreshItemsAsync();
    }

    private void UpdateCompletedBadge()
    {
        if (_items.Count == 0)
        {
            _completedBadge.IsVisible = false;
            return;
        }
        _completedBadge.IsVisible = _items.All(i => i.IsCompleted);
    }
}

public partial record ItemRecord(int Id, string Name, bool IsCompleted)
{
    public bool IsCompleted { get; set; } = IsCompleted;
}
