using CommunityToolkit.Maui;
using dj_buddy.Services;
using MauiIcons.Material;
using Microsoft.Extensions.Logging;

namespace dj_buddy
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMaterialMauiIcons()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if IOS || MACCATALYST
            builder.Services.AddSingleton<IFileBookmarkService, Platforms.iOS.FileBookmarkService>();
#else
            builder.Services.AddSingleton<IFileBookmarkService, DefaultFileBookmarkService>();
#endif
            builder.Services.AddSingleton<MainPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
