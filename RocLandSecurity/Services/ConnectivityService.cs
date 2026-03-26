namespace RocLandSecurity.Services
{
    /// Detecta conectividad con el servidor SQL Server,
    /// no solo con internet en general (podría haber WiFi sin acceso al servidor).
    /// Registrado como Singleton.

    public class ConnectivityService
    {
        private readonly string _serverHost;
        private bool _lastKnownState = false;

        /// <summary>Se dispara cuando cambia el estado online/offline.</summary>
        public event EventHandler<bool>? ConnectivityChanged;

        public bool IsOnline => _lastKnownState;

        public ConnectivityService(string connectionString)
        {
            // Extraer el host del connection string para el ping
            _serverHost = ExtraerHost(connectionString);

            // Suscribirse a cambios de red del sistema
            Connectivity.ConnectivityChanged += OnSystemConnectivityChanged;
        }

        private static string ExtraerHost(string cs)
        {
            // Formato: Server=192.168.1.94;...
            foreach (var parte in cs.Split(';'))
            {
                var kv = parte.Split('=');
                if (kv.Length == 2 &&
                    (kv[0].Trim().Equals("Server", StringComparison.OrdinalIgnoreCase) ||
                     kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase)))
                    return kv[1].Trim();
            }
            return "192.168.1.94";
        }


        /// Verifica conectividad real con el servidor.
        /// Usa un TCP connect rápido al puerto 1433.

        public async Task<bool> CheckServerAsync()
        {
            // Si el sistema no tiene red, ni intentamos
            if (Connectivity.NetworkAccess != NetworkAccess.Internet &&
                Connectivity.NetworkAccess != NetworkAccess.Local)
            {
                SetState(false);
                return false;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var tcp = new System.Net.Sockets.TcpClient();
                await tcp.ConnectAsync(_serverHost, 1433, cts.Token);
                SetState(true);
                return true;
            }
            catch
            {
                SetState(false);
                return false;
            }
        }

        private void OnSystemConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            // Verificación real asíncrona al detectar cambio de red
            _ = Task.Run(async () => await CheckServerAsync());
        }

        private void SetState(bool online)
        {
            if (online == _lastKnownState) return;
            _lastKnownState = online;
            ConnectivityChanged?.Invoke(this, online);
        }
    }
}
