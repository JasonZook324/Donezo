using Microsoft.Extensions.Logging;
using Donezo.Services;

namespace Donezo
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Register Neon DB service
            builder.Services.AddSingleton<INeonDbService, NeonDbService>();
            builder.Services.AddTransient<Pages.LoginPage>(sp => new Pages.LoginPage(sp.GetRequiredService<INeonDbService>()));

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            ServiceHelper.Initialize(app.Services);
            return app;
        }
    }
}
