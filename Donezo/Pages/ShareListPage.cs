using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Donezo.Pages;

public class ShareListPage : ContentPage
{
    private readonly Donezo.Services.ListRecord _list;

    // Dummy data collections
    private readonly ObservableCollection<ShareCodeVm> _codes = new();
    private readonly ObservableCollection<MembershipVm> _memberships = new();

    // New code inputs
    private Picker _newRolePicker = null!;
    private DatePicker _expirationPicker = null!;
    private CheckBox _neverExpireCheck = null!;
    private Entry _maxRedeemsEntry = null!;
    private Button _createCodeButton = null!;

    private CollectionView _codesView = null!;
    private CollectionView _membershipsView = null!;

    // Updated pattern: AAA-12345-ZZZ
    private static readonly Regex ShareCodeRegex = new("^[A-Z]{3}-[0-9]{5}-[A-Z]{3}$", RegexOptions.Compiled);
    private static readonly Random _rnd = new();

    public ShareListPage(Donezo.Services.ListRecord list)
    {
        _list = list;
        BackgroundColor = Colors.Black.WithAlpha(0.6f); // overlay dim
        Padding = 0;
        Title = "Share";

        SeedDummyData();

        var overlay = new Grid { VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Fill };
        var card = new Border
        {
            StrokeThickness = 1,
            Stroke = (Color)Application.Current!.Resources["Primary"],
            BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
            Padding = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = BuildContent()
        };
        overlay.Children.Add(card);
        Content = overlay;
    }

    private void SeedDummyData()
    {
        _codes.Clear();
        // All codes match new pattern [A-Z]{3}-[0-9]{5}-[A-Z]{3}
        _codes.Add(new ShareCodeVm("ABC-12345-XYZ", "Viewer", null, 0, 0, false));
        _codes.Add(new ShareCodeVm("JKL-54321-MNO", "Contributor", DateTime.UtcNow.AddDays(10), 2, 5, false));
        _codes.Add(new ShareCodeVm("PQR-00001-STU", "Viewer", DateTime.UtcNow.AddDays(3), 0, 0, false));

        _memberships.Clear();
        _memberships.Add(new MembershipVm("alice", "Viewer", DateTime.UtcNow.AddDays(-2)));
        _memberships.Add(new MembershipVm("bob", "Contributor", DateTime.UtcNow.AddDays(-1)));
        _memberships.Add(new MembershipVm("charlie", "Contributor", DateTime.UtcNow));
    }

    private View BuildContent()
    {
        var title = new Label { Text = $"Share '{_list.Name}'", FontSize = 20, FontAttributes = FontAttributes.Bold };
        var backBtn = new Button { Text = "< Back", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 14 };
        backBtn.Clicked += async (_, __) => await Navigation.PopModalAsync();
        var headerRow = new HorizontalStackLayout { Spacing = 12, Children = { backBtn, title } };
        var info = new Label { Text = "Generate share codes and manage memberships.", FontSize = 14, TextColor = Colors.Gray, Margin = new Thickness(0, 0, 0, 10) };

        var codesSection = BuildCodesSection();
        var membershipsSection = BuildMembershipsSection();

        return new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children = { headerRow, info, codesSection, membershipsSection }
            }
        };
    }

    private View BuildCodesSection()
    {
        _newRolePicker = new Picker { Title = "Role", WidthRequest = 140 };
        _newRolePicker.ItemsSource = new[] { "Viewer", "Contributor" };
        _newRolePicker.SelectedIndex = 0;

        _neverExpireCheck = new CheckBox { IsChecked = true, VerticalOptions = LayoutOptions.Center };
        _neverExpireCheck.CheckedChanged += (_, __) => UpdateExpirationControls();
        _expirationPicker = new DatePicker { IsVisible = false, MinimumDate = DateTime.Today, Date = DateTime.Today.AddDays(7) };

        _maxRedeemsEntry = new Entry { Placeholder = "Max Redeems (0=Unlimited)", Keyboard = Keyboard.Numeric, WidthRequest = 180 };
        _maxRedeemsEntry.TextChanged += (_, __) => ValidateCreateInputs();

        _createCodeButton = new Button { Text = "Create Code", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = true };
        _createCodeButton.Clicked += (_, __) => CreateNewCode();

        _codesView = new CollectionView
        {
            ItemsSource = _codes,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var outer = new Border
                {
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 6),
                    BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
                    Stroke = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "Gray600" : "Gray100"],
                };

                var codeLabel = new Label { FontAttributes = FontAttributes.Bold, FontSize = 16 };
                codeLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.Code));

                var copyBtn = new Button { Text = "Copy", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(8, 4) };
                copyBtn.Clicked += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ShareCodeVm vm)
                    {
                        try { await Clipboard.Default.SetTextAsync(vm.Code); } catch { }
                    }
                };

                var rolePicker = new Picker { WidthRequest = 130, FontSize = 12 };
                rolePicker.ItemsSource = new[] { "Viewer", "Contributor" };
                rolePicker.SetBinding(Picker.SelectedItemProperty, nameof(ShareCodeVm.Role), BindingMode.TwoWay);

                var expLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                expLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.ExpirationDisplay));

                var daysLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                daysLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.DaysLeftDisplay));

                var maxLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                maxLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.MaxRedeemsDisplay));

                var deleteBtn = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red, FontSize = 12, Padding = new Thickness(8, 4) };
                deleteBtn.Clicked += (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ShareCodeVm vm)
                    {
                        vm.IsDeleted = true;
                        RefreshCodesView();
                    }
                };

                var stack = new VerticalStackLayout { Spacing = 4 };
                stack.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { codeLabel, copyBtn, rolePicker, deleteBtn } });
                stack.Children.Add(new HorizontalStackLayout { Spacing = 12, Children = { expLabel, daysLabel, maxLabel } });
                outer.Content = stack;
                outer.BindingContextChanged += (_, __) => { if (outer.BindingContext is ShareCodeVm vm) outer.IsVisible = !vm.IsDeleted; };
                return outer;
            })
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "Codes", FontSize = 18, FontAttributes = FontAttributes.Bold },
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new HorizontalStackLayout { Spacing = 12, Children = { new Label { Text = "Role:" }, _newRolePicker, new Label { Text = "Never Expire" }, _neverExpireCheck, _expirationPicker } },
                        new HorizontalStackLayout { Spacing = 12, Children = { _maxRedeemsEntry, _createCodeButton } }
                    }
                },
                _codesView
            }
        };
    }

    private View BuildMembershipsSection()
    {
        _membershipsView = new CollectionView
        {
            ItemsSource = _memberships,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var outer = new Border
                {
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 6),
                    BackgroundColor = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "OffBlack" : "White"],
                    Stroke = (Color)Application.Current!.Resources[Application.Current!.RequestedTheme == AppTheme.Dark ? "Gray600" : "Gray100"],
                };

                var userLabel = new Label { FontAttributes = FontAttributes.Bold };
                userLabel.SetBinding(Label.TextProperty, nameof(MembershipVm.Username));

                var roleLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                roleLabel.SetBinding(Label.TextProperty, nameof(MembershipVm.RoleDisplay));

                var joinedLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                joinedLabel.SetBinding(Label.TextProperty, nameof(MembershipVm.JoinedDisplay));

                var revokeBtn = new Button { Text = "Revoke", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(8, 4), TextColor = Colors.Red };
                revokeBtn.Clicked += (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is MembershipVm vm)
                    {
                        vm.IsRevoked = true; // dummy action
                    }
                };
                revokeBtn.SetBinding(IsVisibleProperty, nameof(MembershipVm.CanRevoke));

                var revokedBadge = new Border
                {
                    BackgroundColor = Colors.Red.WithAlpha(0.10f),
                    StrokeThickness = 0,
                    Padding = new Thickness(6, 2),
                    Content = new Label { Text = "Revoked", TextColor = Colors.Red, FontSize = 12, FontAttributes = FontAttributes.Bold }
                };
                revokedBadge.SetBinding(IsVisibleProperty, nameof(MembershipVm.IsRevoked));

                outer.Content = new HorizontalStackLayout { Spacing = 12, Children = { userLabel, roleLabel, joinedLabel, revokeBtn, revokedBadge } };
                return outer;
            })
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "Memberships", FontSize = 18, FontAttributes = FontAttributes.Bold },
                _membershipsView
            }
        };
    }

    private void UpdateExpirationControls()
    {
        _expirationPicker.IsVisible = !_neverExpireCheck.IsChecked;
    }

    private void ValidateCreateInputs()
    {
        if (_createCodeButton == null) return;
        _createCodeButton.IsEnabled = true; // Always enabled for dummy data
    }

    private void CreateNewCode()
    {
        var role = _newRolePicker.SelectedItem as string ?? "Viewer";
        DateTime? expiration = _neverExpireCheck.IsChecked ? null : _expirationPicker.Date;
        int maxRedeems = 0;
        if (int.TryParse(_maxRedeemsEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) maxRedeems = parsed;
        var code = GenerateRandomShareCodeUnique();
        _codes.Add(new ShareCodeVm(code, role, expiration, 0, maxRedeems, false));
        _maxRedeemsEntry.Text = string.Empty;
        RefreshCodesView();
    }

    private string GenerateRandomShareCodeUnique()
    {
        string code;
        do { code = GenerateRandomShareCode(); } while (_codes.Any(c => c.Code == code));
        return code;
    }

    private static string GenerateRandomShareCode()
    {
        // Format: [A-Z]{3}-[0-9]{5}-[A-Z]{3}
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        var sb = new StringBuilder(13); // 3 + 1 + 5 + 1 + 3 = 13
        for (int i = 0; i < 3; i++) sb.Append(letters[_rnd.Next(letters.Length)]);
        sb.Append('-');
        for (int i = 0; i < 5; i++) sb.Append(digits[_rnd.Next(digits.Length)]);
        sb.Append('-');
        for (int i = 0; i < 3; i++) sb.Append(letters[_rnd.Next(letters.Length)]);
        return sb.ToString();
    }

    private static bool IsValidShareCodeFormat(string code) => ShareCodeRegex.IsMatch(code);

    private void RefreshCodesView()
    {
        _codesView.ItemsSource = null; _codesView.ItemsSource = _codes.Where(c => !c.IsDeleted).ToList();
    }
}

// ViewModels (dummy)
public class ShareCodeVm : BindableObject
{
    public string Code { get; }
    private string _role;
    public string Role { get => _role; set { if (_role == value) return; _role = value; OnPropertyChanged(nameof(Role)); OnPropertyChanged(nameof(ExpirationDisplay)); } }
    public DateTime? ExpirationUtc { get; }
    public int RedeemedCount { get; private set; }
    public int MaxRedeems { get; }
    public bool IsDeleted { get; set; }

    public ShareCodeVm(string code, string role, DateTime? expirationUtc, int redeemedCount, int maxRedeems, bool isDeleted)
    { Code = code; _role = role; ExpirationUtc = expirationUtc; RedeemedCount = redeemedCount; MaxRedeems = maxRedeems; IsDeleted = isDeleted; }

    public string ExpirationDisplay => ExpirationUtc == null ? "Never Expire" : $"Expires: {ExpirationUtc.Value:yyyy-MM-dd}";
    public string DaysLeftDisplay
    {
        get
        {
            if (ExpirationUtc == null) return string.Empty;
            var days = (int)Math.Ceiling((ExpirationUtc.Value.Date - DateTime.UtcNow.Date).TotalDays);
            return days < 0 ? "Expired" : $"Days Left: {days}";
        }
    }
    public string MaxRedeemsDisplay => MaxRedeems > 0 ? $"Redeems: {RedeemedCount}/{MaxRedeems}" : "Unlimited";
}

public class MembershipVm : BindableObject
{
    public string Username { get; }
    public string Role { get; }
    public DateTime JoinedUtc { get; }
    private bool _isRevoked;
    public bool IsRevoked { get => _isRevoked; set { if (_isRevoked == value) return; _isRevoked = value; OnPropertyChanged(nameof(IsRevoked)); OnPropertyChanged(nameof(RoleDisplay)); OnPropertyChanged(nameof(CanRevoke)); } }
    public MembershipVm(string username, string role, DateTime joinedUtc)
    { Username = username; Role = role; JoinedUtc = joinedUtc; }

    public string JoinedDisplay => $"Joined: {JoinedUtc:yyyy-MM-dd}";
    public string RoleDisplay => IsRevoked ? "(Revoked)" : Role;
    public bool CanRevoke => !IsRevoked;
}
