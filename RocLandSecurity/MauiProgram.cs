using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RocLandSecurity.Services;
using System.Reflection;

namespace RocLandSecurity
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

            // Cargar AppSettings.json
            var a = Assembly.GetExecutingAssembly();
            using var stream = a.GetManifestResourceStream("ROCLAND_Rondines.AppSettings.json");
            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            // Registrar DatabaseService con la cadena de conexión
            builder.Services.AddSingleton(new DatabaseService(config.GetConnectionString("LocalSqlServer")));

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
