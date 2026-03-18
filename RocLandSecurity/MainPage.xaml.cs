using RocLandSecurity.Services;
using ZXing.Net.Maui;

namespace RocLandSecurity
{
    public partial class MainPage : ContentPage
    {
        private readonly DatabaseService db;
        private bool _qrProcesando = false;

        public MainPage(DatabaseService databaseService)
        {
            InitializeComponent();
            db = databaseService;

            OnTabCredencialesClicked(null, null);
        }

        // ─────────────────────────────────────────────
        // TABS
        // ─────────────────────────────────────────────

        private void OnTabCredencialesClicked(object sender, EventArgs e)
        {
            QrScanner.IsDetecting = false;
            _qrProcesando = false;

            // Mostrar login, ocultar QR
            ScrollLogin.IsVisible = true;
            PanelQR.IsVisible = false;

            // Estilos tabs activo/inactivo
            TabCredencialesIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabCredenciales.TextColor = Color.FromArgb("#111111");
            BtnTabCredenciales.FontAttributes = FontAttributes.Bold;
            TabQRIndicator.BackgroundColor = Colors.Transparent;
            BtnTabQR.TextColor = Color.FromArgb("#888888");
            BtnTabQR.FontAttributes = FontAttributes.None;

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
                var user = await db.GetUsuarioByLoginAsync(usuario, hash);

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

                await MostrarBienvenida(user.Nombre, user.Rol);
            }
            catch (Exception)
            {
                await ShowToastAsync("Error de conexión");
            }
            finally
            {
                BtnIngresar.IsEnabled = true;
                BtnIngresar.Text = "Ingresar";
            }
        }

        // ─────────────────────────────────────────────
        // LOGIN POR QR (automático al detectar)
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
                    var user = await db.GetUsuarioByQRAsync(codigoQR);

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
                    await MostrarBienvenida(user.Nombre, user.Rol);
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
        // HELPERS
        // ─────────────────────────────────────────────

        private async Task MostrarBienvenida(string nombre, int rol)
        {
            string rolTexto = rol == 1 ? "Supervisor" : "Guardia";
            await ShowToastAsync($"Bienvenido {nombre} ({rolTexto})");
            // Aquí después: await Shell.Current.GoToAsync("//HomePage");
        }

        private static string HashSHA256(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        public enum ToastType { Error, Warning, Success }

        public async Task ShowToastAsync(string message, ToastType type = ToastType.Error, int duration = 2000)
        {
            // Definir color según tipo
            Color bgColor = type switch
            {
                ToastType.Success => Color.FromArgb("#6DBF2E"), // verde
                ToastType.Warning => Color.FromArgb("#FFA500"), // naranja
                _ => Color.FromArgb("#FF5555") // rojo por defecto
            };

            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = bgColor;
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;

            // Fade in
            await ToastFrame.FadeTo(1, 250);

            // Mantener visible
            await Task.Delay(duration);

            // Fade out
            await ToastFrame.FadeTo(0, 250);
            ToastFrame.IsVisible = false;
        }

    }
}