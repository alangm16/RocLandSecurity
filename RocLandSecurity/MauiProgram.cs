using Microsoft.Extensions.Logging;
using RocLandSecurity.Services;
using RocLandSecurity.Views.Guardia;
using RocLandSecurity.Views.Supervisor;
using RocLandSecurity.Views.Shared;
using ZXing.Net.Maui.Controls;
using ZXing.Net.Maui;

#if ANDROID
using Android.Widget;
using Microsoft.Maui.Handlers;
#endif

namespace RocLandSecurity
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler(
                        typeof(CameraBarcodeReaderView),
                        typeof(CameraBarcodeReaderViewHandler));
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // ── Cadena de conexión ───────────────────────────────────────────
            // Para emulador: 10.0.2.2 apunta a localhost de la máquina host
            // Para dispositivo físico: usa la IP local de tu PC en la red WiFi
            const string connectionString = @"10.0.2.2,1433;Database=ROCLAND;User Id=sa;Password=12345678;TrustServerCertificate=True;";

            // ── Servicios (Singleton = una sola instancia en toda la app) ───
            builder.Services.AddSingleton(new DatabaseService(connectionString));
            builder.Services.AddSingleton<SessionService>();
            builder.Services.AddSingleton<IFlashlightService, FlashlightService>();

            // ── Páginas (Transient = nueva instancia cada vez) ───────────────
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<GuardiaHomePage>();
            builder.Services.AddTransient<SupervisorHomePage>();
            builder.Services.AddTransient<PerfilPage>();

#if ANDROID
            ImageHandler.Mapper.AppendToMapping("NoTint_Android", (handler, view) =>
            {
                try
                {
                    if (handler.PlatformView is Android.Widget.ImageView imageView)
                    {
                        imageView.ImageTintList = null;
                        imageView.ClearColorFilter();
                        imageView.SetImageDrawable(imageView.Drawable);
                    }
                }
                catch { }
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}