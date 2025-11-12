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
        _listsPicker.SelectedIndexChanged += async (_, _) => await RefreshItemsAsync();

        _newListEntry = new Entry { Placeholder = "New list name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _createListButton = new Button { Text = "Create", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
        _createListButton.Clicked += async (_, _) => await CreateListAsync();

        _newItemEntry = new Entry { Placeholder = "New item name", Style = (Style)Application.Current!.Resources["FilledEntry"] };
        _addItemButton = new Button { Text = "+ Add", Style = (Style)Application.Current!.Resources["OutlinedButton"] };
        _addItemButton.Clicked += async (_, _) => await AddItemAsync();

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
                    }
                };

                grid.Add(nameLabel);
                grid.Add(check, 1, 0);
                card.Content = grid;
                return card;
            })
        };

        var listsCard = new Frame
        {
            Style = (Style)Application.Current!.Resources["CardFrame"],
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Lists", Style = (Style)Application.Current!.Resources["SectionTitle"] },
                    _listsPicker,
                    new HorizontalStackLayout { Spacing = 8, Children = { _newListEntry, _createListButton } }
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
                Children = { listsCard, itemsCard }
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
        await RefreshListsAsync();
    }

    private async Task RefreshListsAsync()
    {
        if (_userId == null) return;
        var lists = await _db.GetListsAsync(_userId.Value);
        _listsPicker.ItemsSource = lists.Count == 0 ? new List<ListRecord>() : lists.ToList();
        _listsPicker.SelectedIndex = lists.Count > 0 ? 0 : -1;
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
        await RefreshItemsAsync();
    }
}

public partial record ItemRecord(int Id, string Name, bool IsCompleted)
{
    public bool IsCompleted { get; set; } = IsCompleted;
}
