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

        public MainPage(DatabaseService databaseService, SessionService sessionService, IFlashlightService flashlight)
        {
            InitializeComponent();
            _db = databaseService;
            _session = sessionService;
            _flashlight = flashlight;

            OnTabCredencialesClicked(null, null);
        }

        // ─────────────────────────────────────────────
        // TABS
        // ─────────────────────────────────────────────

        private void OnTabCredencialesClicked(object? sender, EventArgs? e)
        {
            // Apagar linterna al salir del QR
            if (_flashlight.IsOn)
                _ = ApagarLinternaAsync();

            QrScanner.IsDetecting = false;
            _qrProcesando = false;

            ScrollLogin.IsVisible = true;
            PanelQR.IsVisible = false;

            TabCredencialesIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabCredenciales.TextColor = Color.FromArgb("#111111");
            BtnTabCredenciales.FontAttributes = FontAttributes.Bold;
            TabQRIndicator.BackgroundColor = Colors.Transparent;
            BtnTabQR.TextColor = Color.FromArgb("#888888");
            BtnTabQR.FontAttributes = FontAttributes.None;

            // Resetear icono de linterna
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

            QrStatusLabel.IsVisible = false;
            _qrProcesando = false;
            QrScanner.IsDetecting = true;

            // Verificar disponibilidad de linterna
            if (!await _flashlight.IsAvailableAsync())
            {
                BtnFlash.IsEnabled = false;
                BtnFlash.Opacity = 0.5;
            }
        }

        // ─────────────────────────────────────────────
        // LINTERNA
        // ─────────────────────────────────────────────
        private bool _flashOn = false;
        private void OnFlashClicked(object sender, EventArgs e)
        {
            // Cambiar estado
            _flashOn = !_flashOn;

            // Aplicar al scanner
            QrScanner.IsTorchOn = _flashOn;

            // Actualizar icono visual
            UpdateFlashlightIcon(_flashOn);
        }

        private void UpdateFlashlightIcon(bool isOn)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isOn)
                {
                    BtnFlash.BackgroundColor = Color.FromArgb("#6DBF2E");
                    BtnFlash.ImageSource = "flash_on.png";
                }
                else
                {
                    BtnFlash.BackgroundColor = Color.FromArgb("#333333");
                    BtnFlash.ImageSource = "flash_off.png";
                }
            });
        }

        private async Task ApagarLinternaAsync()
        {
            try
            {
                await _flashlight.TurnOffAsync();
                UpdateFlashlightIcon(false);
            }
            catch { }
        }

        // ─────────────────────────────────────────────
        // LOGIN POR CREDENCIALES
        // ─────────────────────────────────────────────

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

        // ─────────────────────────────────────────────
        // LOGIN POR QR
        // ─────────────────────────────────────────────

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
                    _session.IniciarSesion(user);

                    // Apagar linterna antes de navegar
                    await ApagarLinternaAsync();

                    await Task.Delay(600);
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

        // ─────────────────────────────────────────────
        // NAVEGACIÓN POR ROL
        // ─────────────────────────────────────────────

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

        // ─────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────

        private static string HashSHA256(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
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