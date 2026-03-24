#if ANDROID
using RocLandSecurity.Platforms.Android;
#endif
using RocLandSecurity.Services;

namespace RocLandSecurity
{
    public partial class App : Application
    {
        private readonly MainPage _loginPage;
        private readonly LocalDatabase _localDb;
        private readonly SyncService _sync;
        private readonly ConnectivityService _connectivity;
        private readonly INotificationManagerService? _notificationService;

        public App(MainPage loginPage, LocalDatabase localDb,
            SyncService sync, ConnectivityService connectivity, INotificationManagerService? notificationService = null)
        {
            InitializeComponent();
            _loginPage = loginPage;
            _localDb = localDb;
            _sync = sync;
            _connectivity = connectivity;
            _notificationService = notificationService;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var shell = new AppShell();
            var window = new Window(shell);

            shell.Loaded += async (s, e) =>
            {
                // Solicitar permiso de notificaciones
                await SolicitarPermisoNotificacionesAsync();

                // 1. Inicializar SQLite (una sola vez)
                await _localDb.InitAsync();

                // 2. Arrancar timer de sync (5 min viene de AppConfig)
                _sync.IniciarTimerSync(intervalMinutos: AppConfig.SyncTimerIntervaloMinutos);

                // 3. Sync inicial si hay red (en background, no bloquea login)
                bool online = await _connectivity.CheckServerAsync();
                if (online)
                    _ = Task.Run(async () =>
                        await _sync.SincronizarAsync(SyncReason.AlAbrir));

                // 4. Mostrar login
                await shell.Navigation.PushModalAsync(_loginPage, animated: false);
            };

            return window;
        }

        private async Task SolicitarPermisoNotificacionesAsync()
        {
            try
            {
                if (_notificationService != null)
                {
#if ANDROID
                    var status = await Permissions.RequestAsync<NotificationPermission>();
                    if (status != PermissionStatus.Granted)
                    {
                        System.Diagnostics.Debug.WriteLine("Permiso de notificaciones no concedido");
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al solicitar permiso de notificaciones: {ex.Message}");
            }
        }
    }
}