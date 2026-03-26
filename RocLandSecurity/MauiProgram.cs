using Microsoft.Extensions.Logging;
using RocLandSecurity.Services;
using RocLandSecurity.Views.Guardia;
using RocLandSecurity.Views.Supervisor;
using RocLandSecurity.Views.Shared;
using ZXing.Net.Maui.Controls;
using ZXing.Net.Maui;
#if ANDROID
using RocLandSecurity.Platforms.Android;
#endif

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
            const string connectionString = AppConfig.ConnectionString;

            // ── Servicios ───
            builder.Services.AddSingleton<SharedDatabaseService>(sp =>
                new SharedDatabaseService(AppConfig.ConnectionString));
            builder.Services.AddSingleton<GuardiaDatabaseService>(sp =>
                new GuardiaDatabaseService(AppConfig.ConnectionString));
            builder.Services.AddSingleton<SupervisorDatabaseService>(sp =>
                new SupervisorDatabaseService(AppConfig.ConnectionString));
            builder.Services.AddSingleton<SessionService>();
            builder.Services.AddSingleton<IFlashlightService, FlashlightService>();

            // ── Servicios offline / sync ─────────────────────────────────
            builder.Services.AddSingleton(new ConnectivityService(connectionString));
            builder.Services.AddSingleton<LocalDatabase>();
            builder.Services.AddSingleton<SyncService>(sp =>
            {
                var sync = new SyncService(
                    sp.GetRequiredService<LocalDatabase>(),
                    sp.GetRequiredService<ConnectivityService>(),
                    connectionString);
                sync.IniciarTimerSync();
                return sync;
            });
            builder.Services.AddSingleton<OfflineDatabaseService>();

            // ── Páginas (Transient = nueva instancia cada vez) ───────────────
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<GuardiaHomePage>();
            builder.Services.AddTransient<SupervisorHomePage>();
            builder.Services.AddTransient<PerfilPage>();
            builder.Services.AddTransient<RondinActivoPage>();
            builder.Services.AddTransient<ReportarIncidenciaPage>();
            builder.Services.AddTransient<HistorialGuardiaPage>();
            builder.Services.AddTransient<SupervisorIncidenciasPage>();
            builder.Services.AddTransient<SupervisorHistorialPage>();

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
            // ── Servicio de notificaciones ─────────────────────────────────
#if ANDROID
            builder.Services.AddSingleton<INotificationManagerService, NotificationManagerService>();
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}