namespace Donezo
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                TryShowGlobalError(ex);
            }
        }
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            TryShowGlobalError(e.Exception);
        }
        private void TryShowGlobalError(Exception ex)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try { await Shell.Current?.DisplayAlert("Error", ex.Message, "OK"); } catch { }
                });
            }
            catch { }
        }
    }
}