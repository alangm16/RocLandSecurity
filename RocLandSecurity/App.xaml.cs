using RocLandSecurity.Services;

namespace RocLandSecurity
{
    public partial class App : Application
    {
        private readonly MainPage _loginPage;
        private readonly LocalDatabase _localDb;
        private readonly SyncService _sync;
        private readonly ConnectivityService _connectivity;

        public App(MainPage loginPage, LocalDatabase localDb,
            SyncService sync, ConnectivityService connectivity)
        {
            InitializeComponent();
            _loginPage = loginPage;
            _localDb = localDb;
            _sync = sync;
            _connectivity = connectivity;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var shell = new AppShell();
            var window = new Window(shell);

            shell.Loaded += async (s, e) =>
            {
                // 1. Inicializar SQLite (una sola vez, aquí)
                await _localDb.InitAsync();

                // 2. Arrancar timer de sync (5 min)
                _sync.IniciarTimerSync(intervalMinutos: 5);

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
    }
}