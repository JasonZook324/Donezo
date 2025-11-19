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
    private readonly INeonDbService _db;
    private readonly ListRecord _list;
    private readonly int _currentUserId;

    private readonly ObservableCollection<ShareCodeVm> _codes = new();
    private readonly ObservableCollection<MembershipVm> _memberships = new();

    private Picker _newRolePicker = null!;
    private DatePicker _expirationPicker = null!;
    private CheckBox _neverExpireCheck = null!;
    private Entry _maxRedeemsEntry = null!;
    private Button _createCodeButton = null!;
    private CollectionView _codesView = null!;
    private CollectionView _membershipsView = null!;

    private int? _ownerUserId; // cached owner

    private static readonly Regex ShareCodeRegex = new("^[A-Z]{3}-[0-9]{5}-[A-Z]{3}$", RegexOptions.Compiled);

    public ShareListPage(INeonDbService db, ListRecord list, int currentUserId)
    {
        _db = db; _list = list; _currentUserId = currentUserId;
        BackgroundColor = Colors.Black.WithAlpha(0.6f);
        Padding = 0; Title = "Share";
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
        _ = InitializeAsync();
    }

    public ShareListPage() : this(ServiceHelper.GetRequiredService<INeonDbService>(), new ListRecord(0,"",false), 0) { }

    private async Task InitializeAsync()
    {
        try
        {
            _ownerUserId = await _db.GetListOwnerUserIdAsync(_list.Id);
            // Guard: if current user is not the owner, close immediately
            if (_ownerUserId == null || _ownerUserId.Value != _currentUserId)
            {
                await DisplayAlert("Share", "Only the list owner can manage sharing.", "OK");
                await Navigation.PopModalAsync();
                return;
            }
            await LoadCodesAsync();
            await LoadMembershipsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Share", "Failed to load share data: " + ex.Message, "OK");
        }
    }

    private async Task LoadCodesAsync()
    {
        _codes.Clear();
        var records = await _db.GetShareCodesAsync(_list.Id);
        foreach (var r in records.Where(c => !c.IsDeleted))
            _codes.Add(new ShareCodeVm(r.Id, r.Code, r.Role, r.ExpirationUtc, r.RedeemedCount, r.MaxRedeems, r.IsDeleted));
        RefreshCodesView();
    }

    private async Task LoadMembershipsAsync()
    {
        _memberships.Clear();
        var records = await _db.GetMembershipsAsync(_list.Id);
        foreach (var m in records)
        {
            _memberships.Add(new MembershipVm(m.Id, m.UserId, m.Username, m.Role, m.JoinedUtc, m.Revoked, _ownerUserId == m.UserId, _ownerUserId == _currentUserId));
        }
        // ensure owner flag
        RefreshMemberships();
    }

    private View BuildContent()
    {
        var title = new Label { Text = $"Share '{_list.Name}'", FontSize = 20, FontAttributes = FontAttributes.Bold };
        var backBtn = new Button { Text = "< Back", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 14 };
        backBtn.Clicked += async (_, __) => await Navigation.PopModalAsync();
        var headerRow = new HorizontalStackLayout { Spacing = 12, Children = { backBtn, title } };
        var info = new Label { Text = "Manage share codes and list memberships.", FontSize = 14, TextColor = Colors.Gray, Margin = new Thickness(0, 0, 0, 10) };
        var codesSection = BuildCodesSection();
        var membershipsSection = BuildMembershipsSection();
        return new ScrollView
        {
            Content = new VerticalStackLayout { Spacing = 18, Children = { headerRow, info, codesSection, membershipsSection } }
        };
    }

    private View BuildCodesSection()
    {
        _newRolePicker = new Picker { Title = "Role", WidthRequest = 140, ItemsSource = new[] { "Viewer", "Contributor" }, SelectedIndex = 0 };
        _neverExpireCheck = new CheckBox { IsChecked = true, VerticalOptions = LayoutOptions.Center };
        _neverExpireCheck.CheckedChanged += (_, __) => UpdateExpirationControls();
        _expirationPicker = new DatePicker { IsVisible = false, MinimumDate = DateTime.Today, Date = DateTime.Today.AddDays(7) };
        _maxRedeemsEntry = new Entry { Placeholder = "Max Redeems (0=Unlimited)", Keyboard = Keyboard.Numeric, WidthRequest = 180 };
        _maxRedeemsEntry.TextChanged += (_, __) => ValidateCreateInputs();
        _createCodeButton = new Button { Text = "Create Code", Style = (Style)Application.Current!.Resources["PrimaryButton"], IsEnabled = true };
        _createCodeButton.Clicked += async (_, __) => await CreateNewCodeAsync();

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
                copyBtn.Clicked += async (s, e) => { if (((BindableObject)s).BindingContext is ShareCodeVm vm) { try { await Clipboard.Default.SetTextAsync(vm.Code); } catch { } } };
                var rolePicker = new Picker { WidthRequest = 130, FontSize = 12, ItemsSource = new[] { "Viewer", "Contributor" } };
                // Remove TwoWay binding to avoid pre-setting vm.Role before we compare/change.
                rolePicker.BindingContextChanged += (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ShareCodeVm vm)
                    {
                        // Initialize picker selection from VM role
                        rolePicker.SelectedItem = vm.Role;
                    }
                };
                rolePicker.SelectedIndexChanged += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ShareCodeVm vm && vm.ShareCodeId != null)
                    {
                        var newRole = rolePicker.SelectedItem as string;
                        if (string.IsNullOrWhiteSpace(newRole)) return;
                        var oldRole = vm.Role;
                        if (newRole == oldRole) return; // no change
                        var ok = await _db.UpdateShareCodeRoleAsync(vm.ShareCodeId.Value, newRole);
                        if (!ok)
                        {
                            await DisplayAlert("Role", "Failed to update role.", "OK");
                            // revert picker selection
                            rolePicker.SelectedItem = oldRole;
                            return;
                        }
                        // update model
                        vm.Role = newRole;
                        // Refresh codes and memberships to reflect change everywhere
                        await LoadCodesAsync();
                        await LoadMembershipsAsync();
                    }
                };
                var expLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                expLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.ExpirationDisplay));
                var daysLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                daysLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.DaysLeftDisplay));
                var maxLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
                maxLabel.SetBinding(Label.TextProperty, nameof(ShareCodeVm.MaxRedeemsDisplay));
                var deleteBtn = new Button { Text = "Delete", Style = (Style)Application.Current!.Resources["OutlinedButton"], TextColor = Colors.Red, FontSize = 12, Padding = new Thickness(8, 4) };
                deleteBtn.Clicked += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is ShareCodeVm vm && vm.ShareCodeId != null)
                    {
                        var ok = await _db.SoftDeleteShareCodeAsync(vm.ShareCodeId.Value);
                        if (!ok) { await DisplayAlert("Delete", "Failed to delete code.", "OK"); return; }
                        _codes.Remove(vm);
                        RefreshCodesView();
                    }
                };
                var stack = new VerticalStackLayout { Spacing = 4 };
                stack.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { codeLabel, copyBtn, rolePicker, deleteBtn } });
                stack.Children.Add(new HorizontalStackLayout { Spacing = 12, Children = { expLabel, daysLabel, maxLabel } });
                outer.Content = stack; return outer;
            })
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "Codes", FontSize = 18, FontAttributes = FontAttributes.Bold },
                new VerticalStackLayout { Spacing = 8, Children = { new HorizontalStackLayout { Spacing = 12, Children = { new Label { Text = "Role:" }, _newRolePicker, new Label { Text = "Never Expire" }, _neverExpireCheck, _expirationPicker } }, new HorizontalStackLayout { Spacing = 12, Children = { _maxRedeemsEntry, _createCodeButton } } } },
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
                revokeBtn.SetBinding(IsVisibleProperty, nameof(MembershipVm.CanRevoke));
                revokeBtn.Clicked += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is MembershipVm vm)
                    {
                        var ok = await _db.RevokeMembershipAsync(_list.Id, vm.UserId);
                        if (!ok) { await DisplayAlert("Revoke", "Failed to revoke membership.", "OK"); return; }
                        vm.IsRevoked = true;
                        RefreshMemberships();
                    }
                };
                var transferBtn = new Button { Text = "Transfer Ownership", Style = (Style)Application.Current!.Resources["OutlinedButton"], FontSize = 12, Padding = new Thickness(8, 4) };
                transferBtn.SetBinding(IsVisibleProperty, nameof(MembershipVm.CanTransferOwnership));
                transferBtn.Clicked += async (s, e) =>
                {
                    if (((BindableObject)s).BindingContext is MembershipVm vm)
                    {
                        var confirm = await DisplayAlert("Transfer Ownership", $"Transfer list ownership to {vm.Username}?", "Transfer", "Cancel");
                        if (!confirm) return;
                        var ok = await _db.TransferOwnershipAsync(_list.Id, vm.UserId);
                        if (!ok) { await DisplayAlert("Transfer", "Failed to transfer ownership.", "OK"); return; }
                        // Notify Dashboard to refresh grouping; previous owner now becomes shared member
                        try { MessagingCenter.Send(this, "OwnershipTransferred", _list.Id); } catch { }
                        // Close modal only (no reopen) – dashboard will show list under shared section.
                        try { await Navigation.PopModalAsync(); } catch { }
                    }
                };
                var revokedBadge = new Border
                {
                    BackgroundColor = Colors.Red.WithAlpha(0.10f), StrokeThickness = 0, Padding = new Thickness(6, 2),
                    Content = new Label { Text = "Revoked", TextColor = Colors.Red, FontSize = 12, FontAttributes = FontAttributes.Bold }
                };
                revokedBadge.SetBinding(IsVisibleProperty, nameof(MembershipVm.IsRevoked));
                outer.Content = new HorizontalStackLayout { Spacing = 12, Children = { userLabel, roleLabel, joinedLabel, revokeBtn, transferBtn, revokedBadge } };
                return outer;
            })
        };
        return new VerticalStackLayout { Spacing = 12, Children = { new Label { Text = "Memberships", FontSize = 18, FontAttributes = FontAttributes.Bold }, _membershipsView } };
    }

    private void UpdateExpirationControls() => _expirationPicker.IsVisible = !_neverExpireCheck.IsChecked;
    private void ValidateCreateInputs() { /* simple always enabled */ }

    private async Task CreateNewCodeAsync()
    {
        try
        {
            var role = _newRolePicker.SelectedItem as string ?? "Viewer";
            DateTime? expiration = _neverExpireCheck.IsChecked ? null : _expirationPicker.Date.ToUniversalTime();
            int maxRedeems = 0; if (int.TryParse(_maxRedeemsEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) maxRedeems = parsed;
            var rec = await _db.CreateShareCodeAsync(_list.Id, role, expiration, maxRedeems);
            if (rec == null) { await DisplayAlert("Create", "Failed to create code.", "OK"); return; }
            _codes.Insert(0, new ShareCodeVm(rec.Id, rec.Code, rec.Role, rec.ExpirationUtc, rec.RedeemedCount, rec.MaxRedeems, rec.IsDeleted));
            RefreshCodesView(); _maxRedeemsEntry.Text = string.Empty; _expirationPicker.Date = DateTime.Today.AddDays(7); _neverExpireCheck.IsChecked = true;
        }
        catch (Exception ex) { await DisplayAlert("Create", ex.Message, "OK"); }
    }

    private void RefreshCodesView() => _codesView.ItemsSource = _codes.ToList();
    private void RefreshMemberships() => _membershipsView.ItemsSource = _memberships.ToList();
}

public class ShareCodeVm : BindableObject
{
    public int? ShareCodeId { get; }
    public string Code { get; }
    private string _role;
    public string Role { get => _role; set { if (_role == value) return; _role = value; OnPropertyChanged(nameof(Role)); OnPropertyChanged(nameof(ExpirationDisplay)); } }
    public DateTime? ExpirationUtc { get; }
    public int RedeemedCount { get; private set; }
    public int MaxRedeems { get; }
    public bool IsDeleted { get; set; }
    public ShareCodeVm(int id, string code, string role, DateTime? expirationUtc, int redeemedCount, int maxRedeems, bool isDeleted)
    { ShareCodeId = id; Code = code; _role = role; ExpirationUtc = expirationUtc; RedeemedCount = redeemedCount; MaxRedeems = maxRedeems; IsDeleted = isDeleted; }
    public string ExpirationDisplay => ExpirationUtc == null ? "Never Expire" : $"Expires: {ExpirationUtc.Value:yyyy-MM-dd}";
    public string DaysLeftDisplay
    { get { if (ExpirationUtc == null) return string.Empty; var days = (int)Math.Ceiling((ExpirationUtc.Value.Date - DateTime.UtcNow.Date).TotalDays); return days < 0 ? "Expired" : $"Days Left: {days}"; } }
    public string MaxRedeemsDisplay => MaxRedeems > 0 ? $"Redeems: {RedeemedCount}/{MaxRedeems}" : "Unlimited";
}

public class MembershipVm : BindableObject
{
    public int MembershipId { get; }
    public int UserId { get; }
    public string Username { get; }
    public string Role { get; }
    public DateTime JoinedUtc { get; }
    private bool _isRevoked;
    private bool _isOwner;
    private bool _currentUserIsOwner;
    public bool IsRevoked { get => _isRevoked; set { if (_isRevoked == value) return; _isRevoked = value; OnPropertyChanged(nameof(IsRevoked)); OnPropertyChanged(nameof(RoleDisplay)); OnPropertyChanged(nameof(CanRevoke)); OnPropertyChanged(nameof(CanTransferOwnership)); } }
    public MembershipVm(int membershipId, int userId, string username, string role, DateTime joinedUtc, bool revoked, bool isOwner, bool currentUserIsOwner)
    { MembershipId = membershipId; UserId = userId; Username = username; Role = role; JoinedUtc = joinedUtc; _isRevoked = revoked; _isOwner = isOwner; _currentUserIsOwner = currentUserIsOwner; }
    public string JoinedDisplay => $"Joined: {JoinedUtc:yyyy-MM-dd}";
    public string RoleDisplay => _isOwner ? $"Owner ({Role})" : (_isRevoked ? "(Revoked)" : Role);
    public bool CanRevoke => !_isRevoked && !_isOwner && _currentUserIsOwner;
    public bool CanTransferOwnership => !_isRevoked && !_isOwner && _currentUserIsOwner;
}
