namespace Donezo
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            InitializeAuth();
        }

        private async void InitializeAuth()
        {
            try
            {
                var username = await Microsoft.Maui.Storage.SecureStorage.GetAsync("AUTH_USERNAME");
                if (!string.IsNullOrWhiteSpace(username))
                {
                    // Navigate to dashboard with username in query
                    await GoToAsync($"//dashboard?username={Uri.EscapeDataString(username)}");
                }
                else
                {
                    // Ensure we land on login explicitly to avoid blank shell state
                    await GoToAsync("//login");
                }
            }
            catch
            {
                // Fallback to login on any error
                try { await GoToAsync("//login"); } catch { }
            }
        }
    }
}
