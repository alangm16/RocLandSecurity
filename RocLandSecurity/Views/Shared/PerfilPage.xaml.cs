using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Shared
{
    public partial class PerfilPage : ContentPage
    {
        private readonly SessionService _session;
        private readonly OfflineDatabaseService _offline;
        private readonly SyncService _sync;
        private readonly ConnectivityService _connectivity;

        public PerfilPage(SessionService session, OfflineDatabaseService offline,
            SyncService sync, ConnectivityService connectivity)
        {
            InitializeComponent();
            _session = session;
            _offline = offline;
            _sync = sync;
            _connectivity = connectivity;

            // Suscribir al evento de sync y conectividad para actualizar UI automáticamente
            _sync.SyncCompleted += OnSyncCompleted;
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            CargarDatos();
            if (_session.EsGuardia)
            {
                // Verificar conectividad real al aparecer (no usar cache IsOnline)
                await _connectivity.CheckServerAsync();
                await ActualizarEstadoSyncAsync();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DATOS DEL USUARIO
        // ─────────────────────────────────────────────────────────────────

        private void CargarDatos()
        {
            var u = _session.UsuarioActual;
            if (u == null) return;

            LblIniciales.Text = u.Iniciales;
            LblNombreCompleto.Text = u.Nombre;
            LblUsuario.Text = u.UsuarioLogin;
            LblFechaCreacion.Text = u.FechaCreacion.ToString("dd/MM/yyyy");

            bool esSupervisor = u.EsSupervisor();
            LblRol.Text = esSupervisor ? "Supervisor" : "Guardia";
            LblRolDetalle.Text = esSupervisor ? "Supervisor" : "Guardia de seguridad";

            if (esSupervisor)
            {
                BadgeRol.BackgroundColor = Color.FromArgb("#0a1a2e");
                BadgeRol.Stroke = Color.FromArgb("#185FA5");
                LblRol.TextColor = Color.FromArgb("#85B7EB");
                PanelSync.IsVisible = false;
            }
            else
            {
                BadgeRol.BackgroundColor = Color.FromArgb("#0a1a0a");
                BadgeRol.Stroke = Color.FromArgb("#3B6D11");
                LblRol.TextColor = Color.FromArgb("#97C459");
                PanelSync.IsVisible = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // SYNC
        // ─────────────────────────────────────────────────────────────────

        private async Task ActualizarEstadoSyncAsync()
        {
            int pendientes = await _offline.GetTotalPendientesSyncAsync();
            bool online = _connectivity.IsOnline;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (pendientes == 0)
                {
                    LblEstadoSync.Text = online ? "Todo sincronizado" : "Sin conexión · Sin pendientes";
                    LblBadgeSync.Text = "OK";
                    LblBadgeSync.TextColor = Color.FromArgb("#6DBF2E");
                    BadgeSync.BackgroundColor = Color.FromArgb("#1A2A1A");
                }
                else
                {
                    LblEstadoSync.Text = $"{pendientes} registro(s) pendiente(s)";
                    LblBadgeSync.Text = pendientes.ToString();
                    LblBadgeSync.TextColor = Color.FromArgb("#FAC775");
                    BadgeSync.BackgroundColor = Color.FromArgb("#2A2200");
                }

                BtnForzarSync.IsEnabled = online;
                BtnForzarSync.Opacity = online ? 1.0 : 0.5;
                BtnForzarSync.Text = online
                    ? "Forzar sincronización ahora"
                    : "Sin conexión al servidor";
            });
        }

        private async void OnForzarSyncClicked(object sender, EventArgs e)
        {
            BtnForzarSync.IsEnabled = false;
            BtnForzarSync.Text = "Sincronizando...";
            LblEstadoSync.Text = "Enviando datos al servidor...";

            var result = await _sync.SincronizarAsync(SyncReason.Manual);

            await ActualizarEstadoSyncAsync();

            if (result.Exitoso)
            {
                string msg = result.TienePendientes
                    ? $"Sync OK — {result.RondinesSincronizados}R · {result.PuntosSincronizados}P · {result.IncidenciasSincronizadas}I"
                    : "Todo ya estaba sincronizado";
                LblUltimaSync.Text = $"Última sync: {DateTime.Now:HH:mm:ss}";
                await ShowToastAsync(msg, isError: false);
            }
            else if (!result.Omitido)
            {
                await ShowToastAsync($"Error de sync: {result.Error}");
            }

            BtnForzarSync.IsEnabled = true;
            BtnForzarSync.Text = "Forzar sincronización ahora";
        }

        private void OnSyncCompleted(object? sender, SyncResult result)
        {
            if (PanelSync.IsVisible)
                _ = ActualizarEstadoSyncAsync();
        }

        private void OnConnectivityChanged(object? sender, bool online)
        {
            // Al recuperar conexión: actualizar botón y lanzar sync automático
            _ = ActualizarEstadoSyncAsync();
            if (online)
                _ = Task.Run(async () => await _sync.SincronizarAsync(SyncReason.Reconexion));
        }

        // ─────────────────────────────────────────────────────────────────
        // CICLO DE VIDA
        // ─────────────────────────────────────────────────────────────────

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        }

        // ─────────────────────────────────────────────────────────────────
        // LOGOUT
        // ─────────────────────────────────────────────────────────────────

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            // Avisar si hay pendientes
            int pendientes = _session.EsGuardia
                ? await _offline.GetTotalPendientesSyncAsync()
                : 0;

            string mensaje = pendientes > 0
                ? $"Tienes {pendientes} registro(s) sin sincronizar. Si cierras sesión ahora, se sincronizarán la próxima vez que abras la app con conexión. ¿Continuar?"
                : "¿Estás seguro que deseas cerrar tu sesión?";

            bool confirmar = await DisplayAlertAsync(
                "Cerrar sesión", mensaje, "Sí, salir", "Cancelar");

            if (!confirmar) return;

            _sync.SyncCompleted -= OnSyncCompleted;
            _session.CerrarSesion();
            await ((Shell.Current as AppShell)?.LogoutAsync() ?? Task.CompletedTask);
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message, bool isError = true)
        {
            // PerfilPage no tiene ToastFrame en el XAML actual — usar DisplayAlert simple
            if (isError)
                await DisplayAlertAsync("Error", message, "OK");
            else
                await DisplayAlertAsync("Información", message, "OK");
        }
    }
}