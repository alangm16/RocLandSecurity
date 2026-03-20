using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.AppCompat.App;
using RocLandSecurity.Services;

namespace RocLandSecurity
{
    [Activity(Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize |
        ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
        ScreenOrientation = ScreenOrientation.Portrait,
        HardwareAccelerated = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
            base.OnCreate(savedInstanceState);
        }

        /// <summary>
        /// Intercepta el botón Back físico/gestual de Android.
        ///
        /// Caso 1 — Login visible (no autenticado):
        ///   Bloquear. El modal del login no debe cerrarse con Back
        ///   revelando el Shell debajo.
        ///
        /// Caso 2 — Página con stack de navegación (RondinActivo, etc.):
        ///   MAUI lo maneja primero vía OnBackButtonPressed de la ContentPage.
        ///   Si llega aquí, dejar que base lo resuelva.
        ///
        /// Caso 3 — Raíz de un TabBar (GuardiaHome, Historial, Perfil…):
        ///   Minimizar la app. No navegar a tab anterior ni salir.
        /// </summary>
#pragma warning disable CA1422
        public override void OnBackPressed()
        {
            var session = IPlatformApplication.Current?.Services
                .GetService<SessionService>();

            // Caso 1: no autenticado → bloquear
            if (session == null || !session.EstaAutenticado)
                return;

            var shell = Microsoft.Maui.Controls.Shell.Current;
            if (shell != null)
            {
                // Hay modal abierto (ej: login sobre el Shell) → dejar que base lo cierre
                if (shell.Navigation?.ModalStack?.Count > 0)
                {
                    base.OnBackPressed();
                    return;
                }

                // Hay páginas en el stack de navegación (rutas tipo rondinactivo)
                if (shell.Navigation?.NavigationStack?.Count > 1)
                {
                    base.OnBackPressed();
                    return;
                }
            }

            // Caso 3: raíz de tab → minimizar
            MoveTaskToBack(true);
        }
#pragma warning restore CA1422
    }
}
