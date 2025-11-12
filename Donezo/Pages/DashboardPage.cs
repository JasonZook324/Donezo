using Donezo.Services;
using System.Collections.ObjectModel;

namespace Donezo.Pages;

public class DashboardPage : ContentPage
{
    private readonly INeonDbService _db;
    private readonly string _username;
    private int? _userId;
    private Picker _listsPicker = null!;
    private Entry _newListEntry = null!;
    private Button _createListButton = null!;
    private CollectionView _itemsView = null!;
    private Entry _newItemEntry = null!;
    private Button _addItemButton = null!;

    private readonly ObservableCollection<ItemRecord> _items = new();

    private int? SelectedListId => _listsPicker.SelectedItem is ListRecord lr ? lr.Id : null;

    public DashboardPage(INeonDbService db, string username)
    {
        _db = db;
        _username = username;
        Title = "Dashboard";
        BuildUi();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private void BuildUi()
    {
        _listsPicker = new Picker { Title = "Select List" };
        _listsPicker.ItemDisplayBinding = new Binding("Name");
        _listsPicker.SelectedIndexChanged += async (_, _) => await RefreshItemsAsync();

        _newListEntry = new Entry { Placeholder = "New list name" };
        _createListButton = new Button { Text = "Create List" };
        _createListButton.Clicked += async (_, _) => await CreateListAsync();

        _newItemEntry = new Entry { Placeholder = "New item name" };
        _addItemButton = new Button { Text = "Add Item" };
        _addItemButton.Clicked += async (_, _) => await AddItemAsync();

        _itemsView = new CollectionView
        {
            ItemsSource = _items,
            ItemTemplate = new DataTemplate(() =>
            {
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    Padding = new Thickness(4,2)
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
                        ir.IsCompleted = e.Value; // keep UI in sync
                    }
                };

                grid.Add(nameLabel);
                grid.Add(check, 1, 0);
                return grid;
            })
        };

        var card = new Frame
        {
            BorderColor = Colors.Grey,
            CornerRadius = 12,
            Padding = 12,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Lists", FontAttributes = FontAttributes.Bold },
                    _listsPicker,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton } },
                    new Label { Text = "Items", FontAttributes = FontAttributes.Bold },
                    _itemsView,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newItemEntry, _addItemButton } }
                }
            }
        };

        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 20, Children = { card } } };
    }

    private async Task InitializeAsync()
    {
        _userId = await _db.GetUserIdAsync(_username);
        if (_userId == null)
        {
            await DisplayAlert("Error", "User not found.", "OK");
            return;
        }
        await RefreshListsAsync();
    }

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        var lists = await _db.GetListsAsync(_userId.Value);
        _listsPicker.ItemsSource = lists.Count == 0 ? new List<ListRecord>() : lists.ToList();
        _listsPicker.SelectedIndex = lists.Count > 0 ? 0 : -1;
        // No explicit RefreshItemsAsync here; SelectedIndexChanged will handle it
    }

    private async Task CreateListAsync()
    {
        if (_userId == null) return;
        var name = _newListEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        await _db.CreateListAsync(_userId.Value, name);
        _newListEntry.Text = string.Empty;
        await RefreshListsAsync();
    }

    private async Task RefreshItemsAsync()
    {
        _items.Clear();
        var listId = SelectedListId;
        if (listId == null) return;
        var items = await _db.GetItemsAsync(listId.Value);
        foreach (var i in items)
            _items.Add(new ItemRecord(i.Id, i.Name, i.IsCompleted));
    }

    private async Task AddItemAsync()
    {
        var listId = SelectedListId;
        if (listId == null) return;
        var name = _newItemEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        await _db.AddItemAsync(listId.Value, name);
        _newItemEntry.Text = string.Empty;
        await RefreshItemsAsync(); // reload from DB to avoid duplicates
    }
}

// Make ItemRecord mutable for checkbox updates
public partial record ItemRecord(int Id, string Name, bool IsCompleted)
{
    public bool IsCompleted { get; set; } = IsCompleted;
}
