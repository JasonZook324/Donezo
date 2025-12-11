namespace Donezo
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Loaded += OnShellLoaded;
        }

        private async void OnShellLoaded(object? sender, EventArgs e)
        {
            Loaded -= OnShellLoaded;
            await InitializeAuthDeferred();
        }

        private async Task InitializeAuthDeferred()
        {
            try
            {
                var username = await Microsoft.Maui.Storage.SecureStorage.GetAsync("AUTH_USERNAME");
                if (!string.IsNullOrWhiteSpace(username))
                {
                    // Fire-and-forget: trigger daily auto-reset for user's daily lists on app launch
                    _ = TriggerDailyResetForUserAsync(username);
                    await GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
                }
                else
                {
                    await GoToAsync("//login");
                }
            }
            catch
            {
                try { await GoToAsync("//login"); } catch { }
            }
        }

        private async Task TriggerDailyResetForUserAsync(string username)
        {
            try
            {
                var db = ServiceHelper.GetRequiredService<Donezo.Services.INeonDbService>();
                var userId = await db.GetUserIdAsync(username);
                if (userId == null) return;
                var owned = await db.GetOwnedListsAsync(userId.Value);
                foreach (var lr in owned)
                {
                    if (!lr.IsDaily) continue;
                    try { await db.GetItemsAsync(lr.Id); } catch { }
                }
            }
            catch { }
        }
    }
}
