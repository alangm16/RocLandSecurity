using Microsoft.Extensions.Logging;
using RocLandSecurity.Services;
using ZXing.Net.Maui.Controls;
using ZXing.Net.Maui;


#if ANDROID
using Android.Widget;
using Android.Graphics;
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
                // ← Registra ZXing para el escáner QR
                .UseBarcodeReader()
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler(typeof(CameraBarcodeReaderView), typeof(CameraBarcodeReaderViewHandler));
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            string connectionString = @"Server=LAPTOP-2U33G2AH\SQLEXPRESS;Database=ROCLAND;User Id=sa;Password=12345678;TrustServerCertificate=True;";

            builder.Services.AddSingleton(new DatabaseService(connectionString));
            builder.Services.AddSingleton<MainPage>();

#if ANDROID
            ImageHandler.Mapper.AppendToMapping("NoTint_Android", (handler, view) =>
            {
                try
                {
                    if (handler.PlatformView is ImageView imageView)
                    {
                        imageView.ImageTintList = null;
                        imageView.ClearColorFilter();
                        var d = imageView.Drawable;
                        imageView.SetImageDrawable(d);
                    }
                }
                catch { }
            });
#endif

            return builder.Build();
        }
    }
}