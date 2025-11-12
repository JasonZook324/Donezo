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
            }
            catch { /* ignore */ }
        }
    }
}
