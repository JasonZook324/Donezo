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
    }
}
