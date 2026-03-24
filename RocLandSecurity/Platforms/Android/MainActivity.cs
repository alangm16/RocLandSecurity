using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.AppCompat.App;
using RocLandSecurity.Services;
using RocLandSecurity.Platforms.Android;

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

            // Procesar notificación si la app se abrió desde una notificación
            CreateNotificationFromIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            CreateNotificationFromIntent(intent);
        }

        void CreateNotificationFromIntent(Intent? intent)
        {
            if (intent?.Extras != null)
            {
                string title = intent.GetStringExtra(NotificationManagerService.TitleKey) ?? string.Empty;
                string message = intent.GetStringExtra(NotificationManagerService.MessageKey) ?? string.Empty;
                string type = intent.GetStringExtra(NotificationManagerService.TypeKey) ?? string.Empty;
                int rondinId = intent.GetIntExtra(NotificationManagerService.RondinIdKey, 0);

                var service = IPlatformApplication.Current?.Services.GetService<INotificationManagerService>();
                service?.ReceiveNotification(title, message, type, rondinId);
            }
        }

        public override void OnBackPressed()
        {
            var session = IPlatformApplication.Current?.Services
                .GetService<SessionService>();

            if (session == null || !session.EstaAutenticado)
                return;

            var shell = Microsoft.Maui.Controls.Shell.Current;
            if (shell != null)
            {
                if (shell.Navigation?.ModalStack?.Count > 0)
                {
                    base.OnBackPressed();
                    return;
                }

                if (shell.Navigation?.NavigationStack?.Count > 1)
                {
                    base.OnBackPressed();
                    return;
                }
            }

            MoveTaskToBack(true);
        }
    }
}