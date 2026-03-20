using RocLandSecurity.Services;
using ZXing.Net.Maui;

namespace RocLandSecurity
{
    public partial class MainPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService _session;
        private readonly IFlashlightService _flashlight;
        private bool _qrProcesando = false;
        private bool _flashOn = false;

        public MainPage(DatabaseService databaseService, SessionService sessionService, IFlashlightService flashlight)
        {
            InitializeComponent();
            _db = databaseService;
            _session = sessionService;
            _flashlight = flashlight;

            OnTabCredencialesClicked(null, null);
        }

        public void ResetearVista()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (UsuarioEntry != null) UsuarioEntry.Text = string.Empty;
                if (ContrasenaEntry != null) ContrasenaEntry.Text = string.Empty;

                if (QrStatusLabel != null)
                {
                    QrStatusLabel.Text = string.Empty;
                    QrStatusLabel.IsVisible = false;
                }

                if (QrScanner != null) QrScanner.IsDetecting = false;
                _flashOn = false;
                UpdateFlashlightIcon(false);
                _qrProcesando = false;

                if (BtnRefrescar != null)
                {
                    BtnRefrescar.BackgroundColor = Color.FromArgb("#333333");
                    BtnRefrescar.IsEnabled = true;
                }

                OnTabCredencialesClicked(null, null);
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // TABS
        // ─────────────────────────────────────────────────────────────────

        private void OnTabCredencialesClicked(object? sender, EventArgs? e)
        {
            // Apagar linterna y scanner si estaban activos
            if (_flashOn) _ = ApagarLinternaAsync();
            if (QrScanner != null) QrScanner.IsDetecting = false;
            _qrProcesando = false;

            ScrollLogin.IsVisible = true;
            PanelQR.IsVisible = false;

            TabCredencialesIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabCredenciales.TextColor = Color.FromArgb("#111111");
            BtnTabCredenciales.FontAttributes = FontAttributes.Bold;
            TabQRIndicator.BackgroundColor = Colors.Transparent;
            BtnTabQR.TextColor = Color.FromArgb("#888888");
            BtnTabQR.FontAttributes = FontAttributes.None;

            UpdateFlashlightIcon(false);
        }

        private async void OnTabQRClicked(object sender, EventArgs e)
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await ShowToastAsync("Se necesita permiso de cámara");
                return;
            }

            ScrollLogin.IsVisible = false;
            PanelQR.IsVisible = true;

            TabQRIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabQR.TextColor = Color.FromArgb("#111111");
            BtnTabQR.FontAttributes = FontAttributes.Bold;
            TabCredencialesIndicator.BackgroundColor = Colors.Transparent;
            BtnTabCredenciales.TextColor = Color.FromArgb("#888888");
            BtnTabCredenciales.FontAttributes = FontAttributes.None;

            QrStatusLabel.Text = string.Empty;
            QrStatusLabel.IsVisible = false;
            _qrProcesando = false;
            QrScanner.IsDetecting = true;

            if (!await _flashlight.IsAvailableAsync())
            {
                BtnFlash.IsEnabled = false;
                BtnFlash.Opacity = 0.5;
            }

            BtnRefrescar.BackgroundColor = Color.FromArgb("#333333");
            BtnRefrescar.IsEnabled = true;
        }

        // ─────────────────────────────────────────────────────────────────
        // LINTERNA
        // ─────────────────────────────────────────────────────────────────

        private void OnFlashClicked(object sender, EventArgs e)
        {
            _flashOn = !_flashOn;
            QrScanner.IsTorchOn = _flashOn;
            UpdateFlashlightIcon(_flashOn);
        }

        private void UpdateFlashlightIcon(bool isOn)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (BtnFlash == null) return;
                BtnFlash.BackgroundColor = isOn
                    ? Color.FromArgb("#6DBF2E")
                    : Color.FromArgb("#333333");
                BtnFlash.ImageSource = isOn ? "flash_on.png" : "flash_off.png";
            });
        }

        private async Task ApagarLinternaAsync()
        {
            try
            {
                _flashOn = false;
                QrScanner.IsTorchOn = false;
                await _flashlight.TurnOffAsync();
                UpdateFlashlightIcon(false);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // LOGIN POR CREDENCIALES
        // ─────────────────────────────────────────────────────────────────

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string usuario = UsuarioEntry.Text?.Trim() ?? "";
            string contrasena = ContrasenaEntry.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(contrasena))
            {
                await ShowToastAsync("Usuario y contraseña requeridos");
                return;
            }

            BtnIngresar.IsEnabled = false;
            BtnIngresar.Text = "Verificando...";

            try
            {
                string hash = HashSHA256(contrasena);
                var user = await _db.GetUsuarioByLoginAsync(usuario, hash);

                if (user == null)
                {
                    await ShowToastAsync("Credenciales incorrectas");
                    return;
                }

                if (!user.Activo)
                {
                    await ShowToastAsync("Cuenta desactivada");
                    return;
                }

                _session.IniciarSesion(user);
                await NavegerPorRol();
            }
            catch (Exception)
            {
                await ShowToastAsync("Error de conexión. Verifica la red.");
            }
            finally
            {
                BtnIngresar.IsEnabled = true;
                BtnIngresar.Text = "Ingresar";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // LOGIN POR QR
        // ─────────────────────────────────────────────────────────────────

        private void ReiniciarCamara()
        {
            if (PanelQR == null) return;

            // Guardamos el flash
            bool flashEstabaEncendido = _flashOn;

            // Eliminamos la cámara actual
            if (QrScanner != null)
            {
                PanelQR.Children.Remove(QrScanner);
                QrScanner = null;
            }

            // Creamos una nueva instancia
            QrScanner = new ZXing.Net.Maui.Controls.CameraBarcodeReaderView
            {
                IsDetecting = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
            QrScanner.BarcodesDetected += OnQrDetected;

            // Agregamos la nueva cámara a la vista
            PanelQR.Children.Insert(0, QrScanner);

            // Restauramos linterna si estaba encendida
            if (flashEstabaEncendido)
            {
                QrScanner.IsTorchOn = true;
            }

            _qrProcesando = false;
        }

        private async void OnRefrescarClicked(object sender, EventArgs e)
        {
            QrStatusLabel.Text = "Reiniciando cámara...";
            QrStatusLabel.IsVisible = true;

            ReiniciarCamara();

            QrStatusLabel.Text = "Cámara lista";
            await Task.Delay(800);
            QrStatusLabel.IsVisible = false;
        }

        private async void OnQrDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if (_qrProcesando) return;
            _qrProcesando = true;
            QrScanner.IsDetecting = false;

            string codigoQR = e.Results?.FirstOrDefault()?.Value ?? "";

            if (string.IsNullOrWhiteSpace(codigoQR))
            {
                _qrProcesando = false;
                QrScanner.IsDetecting = true;
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                QrStatusLabel.Text = "Verificando...";
                QrStatusLabel.IsVisible = true;

                try
                {
                    var user = await _db.GetUsuarioByQRAsync(codigoQR);

                    if (user == null)
                    {
                        QrStatusLabel.IsVisible = false;
                        await ShowToastAsync("QR no reconocido");
                        await Task.Delay(500);
                        _qrProcesando = false;
                        QrScanner.IsDetecting = true;
                        return;
                    }

                    if (!user.Activo)
                    {
                        QrStatusLabel.IsVisible = false;
                        await ShowToastAsync("Cuenta desactivada");
                        await Task.Delay(500);
                        _qrProcesando = false;
                        QrScanner.IsDetecting = true;
                        return;
                    }

                    QrStatusLabel.Text = $"¡Bienvenido, {user.Nombre}!";
                    QrStatusLabel.IsVisible = true;

                    _session.IniciarSesion(user);
                    await ApagarLinternaAsync();
                    await Task.Delay(600);

                    QrStatusLabel.Text = string.Empty;
                    QrStatusLabel.IsVisible = false;

                    await NavegerPorRol();
                }
                catch (Exception)
                {
                    QrStatusLabel.IsVisible = false;
                    await ShowToastAsync("Error de conexión");
                    await Task.Delay(500);
                    _qrProcesando = false;
                    QrScanner.IsDetecting = true;
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // NAVEGACIÓN POR ROL
        // ─────────────────────────────────────────────────────────────────

        private async Task NavegerPorRol()
        {
            UsuarioEntry.Text = string.Empty;
            ContrasenaEntry.Text = string.Empty;

            var shell = Shell.Current as AppShell;

            if (_session.EsSupervisor)
                await (shell?.MostrarTabBarSupervisorAsync() ?? Task.CompletedTask);
            else
                await (shell?.MostrarTabBarGuardiaAsync() ?? Task.CompletedTask);
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static string HashSHA256(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        // ─────────────────────────────────────────────────────────────────
        // BOTÓN BACK DEL SISTEMA
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Intercepta el botón Back de Android mientras el login está visible.
        /// Si el usuario NO está autenticado → bloquear (evita revelar el Shell).
        /// Si está autenticado → comportamiento normal (navegar atrás en modales).
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            // Usuario no autenticado: el login está abierto como modal.
            // Bloquear el back para que no revele el Shell debajo.
            if (!_session.EstaAutenticado)
                return true;   // true = evento consumido, no hacer nada

            // Usuario autenticado: permitir comportamiento normal.
            return base.OnBackButtonPressed();
        }

                public enum ToastType { Error, Warning, Success }

        public async Task ShowToastAsync(string message, ToastType type = ToastType.Error, int duration = 2000)
        {
            Color bgColor = type switch
            {
                ToastType.Success => Color.FromArgb("#6DBF2E"),
                ToastType.Warning => Color.FromArgb("#FFA500"),
                _ => Color.FromArgb("#FF5555")
            };

            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = bgColor;
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;

            await ToastFrame.FadeTo(1, 250);
            await Task.Delay(duration);
            await ToastFrame.FadeTo(0, 250);
            ToastFrame.IsVisible = false;
        }
    }
}