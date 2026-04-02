using Microsoft.Extensions.Logging;

namespace Maui.CodePush.Demo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseCodePush(options =>
            {
                options.AddModule("Maui.CodePush.Demo.Feature");
                options.ReleaseVersion = "1.0.0";

                // Server configuration — fill with values from `codepush apps list`
                // options.ServerUrl = "https://your-codepush-server.com";
                // options.AppId = "your-app-id-guid";
                // options.AppToken = "your-app-token";
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
