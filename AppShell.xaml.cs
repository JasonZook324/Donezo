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
                    // User already logged in; navigate directly to dashboard
                    var db = ServiceHelper.GetRequiredService<Services.INeonDbService>();
                    await Navigation.PushAsync(new Pages.DashboardPage(db, username));
                }
            }
            catch { /* ignore */ }
        }
    }
}
