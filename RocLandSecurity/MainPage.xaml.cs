using RocLandSecurity.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls.Shapes;

namespace RocLandSecurity
{
    public partial class MainPage : ContentPage
    {
        private readonly DatabaseService db;
        private bool _qrProcesando = false;   // evita doble disparo del escáner

        public MainPage(DatabaseService databaseService)
        {
            Console.WriteLine("[DEBUG] MainPage - Constructor iniciado");
            InitializeComponent();
            db = databaseService;
            Console.WriteLine("[DEBUG] MainPage - DatabaseService asignado");

            QrScanner.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true,
                Multiple = false
            };
            Console.WriteLine("[DEBUG] MainPage - Opciones del escáner configuradas");
        }

        // ─────────────────────────────────────────────
        // TABS
        // ─────────────────────────────────────────────

        private void OnTabCredencialesClicked(object sender, EventArgs e)
        {
            Console.WriteLine("[DEBUG] OnTabCredencialesClicked - Cambiando a pestaña Credenciales");

            // Apagar escáner antes de cambiar
            QrScanner.IsDetecting = false;
            _qrProcesando = false;
            Console.WriteLine($"[DEBUG] OnTabCredencialesClicked - Escáner detenido. IsDetecting: {QrScanner.IsDetecting}");

            PanelCredenciales.IsVisible = true;
            PanelQR.IsVisible = false;

            // Estilos de tabs
            TabCredencialesIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabCredenciales.TextColor = Color.FromArgb("#111111");
            BtnTabCredenciales.FontAttributes = FontAttributes.Bold;

            TabQRIndicator.BackgroundColor = Colors.Transparent;
            BtnTabQR.TextColor = Color.FromArgb("#888888");
            BtnTabQR.FontAttributes = FontAttributes.None;

            OcultarError();
            QrStatusLabel.IsVisible = false;
            Console.WriteLine("[DEBUG] OnTabCredencialesClicked - Cambio completado");
        }

        private async void OnTabQRClicked(object sender, EventArgs e)
        {
            Console.WriteLine("[DEBUG] OnTabQRClicked - Iniciando cambio a pestaña QR");

            // Verificar permisos
            Console.WriteLine("[DEBUG] OnTabQRClicked - Solicitando permiso de cámara");
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            Console.WriteLine($"[DEBUG] OnTabQRClicked - Estado del permiso: {status}");

            if (status != PermissionStatus.Granted)
            {
                MostrarError("Se necesita permiso de cámara para escanear QR.");
                return;
            }

            Console.WriteLine("[DEBUG] OnTabQRClicked - Permiso concedido, mostrando panel QR");

            Console.WriteLine("[DEBUG] OnTabQRClicked - Recreando escáner");

            // Guardar referencia al padre
            var parentGrid = QrScanner.Parent as Grid;
            if (parentGrid != null)
            {
                // Remover el escáner actual
                parentGrid.Children.Remove(QrScanner);

                // Crear nuevo escáner
                var newScanner = new CameraBarcodeReaderView
                {
                    Options = new BarcodeReaderOptions
                    {
                        Formats = BarcodeFormat.QrCode,
                        AutoRotate = true,
                        Multiple = false
                    },
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    VerticalOptions = LayoutOptions.FillAndExpand,
                    WidthRequest = 300,
                    HeightRequest = 300
                };
                newScanner.BarcodesDetected += OnQrDetected;

                // Agregar el nuevo escáner al grid
                parentGrid.Children.Add(newScanner);

                // Reemplazar la referencia
                QrScanner = newScanner;
            }

            // Cambiar UI
            PanelCredenciales.IsVisible = false;
            PanelQR.IsVisible = true;

            // Actualizar estilos de tabs
            TabQRIndicator.BackgroundColor = Color.FromArgb("#6DBF2E");
            BtnTabQR.TextColor = Color.FromArgb("#111111");
            BtnTabQR.FontAttributes = FontAttributes.Bold;

            TabCredencialesIndicator.BackgroundColor = Colors.Transparent;
            BtnTabCredenciales.TextColor = Color.FromArgb("#888888");
            BtnTabCredenciales.FontAttributes = FontAttributes.None;

            OcultarError();
            QrStatusLabel.IsVisible = false;
            _qrProcesando = false;

            // Esperar a que la UI se actualice
            await Task.Delay(500);

            // Activar el nuevo escáner
            try
            {
                Console.WriteLine("[DEBUG] OnTabQRClicked - Activando nuevo escáner");
                QrScanner.IsDetecting = true;
                Console.WriteLine($"[DEBUG] OnTabQRClicked - IsDetecting después de activar: {QrScanner.IsDetecting}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] OnTabQRClicked - {ex.Message}");
                MostrarError($"Error al iniciar cámara: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // LOGIN POR CREDENCIALES
        // ─────────────────────────────────────────────

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            Console.WriteLine("[DEBUG] OnLoginClicked - Intento de login con credenciales");
            string usuario = UsuarioEntry.Text?.Trim() ?? "";
            string contrasena = ContrasenaEntry.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(contrasena))
            {
                Console.WriteLine("[DEBUG] OnLoginClicked - Campos vacíos");
                MostrarError("Ingresa usuario y contraseña.");
                return;
            }

            BtnIngresar.IsEnabled = false;
            BtnIngresar.Text = "Verificando...";
            OcultarError();

            try
            {
                Console.WriteLine($"[DEBUG] OnLoginClicked - Verificando usuario: {usuario}");
                string hashContrasena = HashSHA256(contrasena);
                var user = await db.GetUsuarioByLoginAsync(usuario, hashContrasena);

                if (user == null)
                {
                    Console.WriteLine("[DEBUG] OnLoginClicked - Usuario no encontrado");
                    MostrarError("Usuario o contraseña incorrectos.");
                    return;
                }

                Console.WriteLine($"[DEBUG] OnLoginClicked - Usuario encontrado: {user.Nombre}, Activo: {user.Activo}");

                if (!user.Activo)
                {
                    Console.WriteLine("[DEBUG] OnLoginClicked - Usuario inactivo");
                    MostrarError("Tu cuenta está desactivada. Contacta a tu supervisor.");
                    return;
                }

                Console.WriteLine("[DEBUG] OnLoginClicked - Login exitoso, mostrando bienvenida");
                await MostrarBienvenida(user.Nombre, user.Rol);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] OnLoginClicked - Excepción: {ex.Message}");
                MostrarError($"Error de conexión: {ex.Message}");
            }
            finally
            {
                BtnIngresar.IsEnabled = true;
                BtnIngresar.Text = "Ingresar";
                Console.WriteLine("[DEBUG] OnLoginClicked - Proceso finalizado");
            }
        }

        // ─────────────────────────────────────────────
        // LOGIN POR QR — se dispara automáticamente
        // ─────────────────────────────────────────────

        private async void OnQrDetected(object sender, BarcodeDetectionEventArgs e)
        {
            Console.WriteLine("[DEBUG] OnQrDetected - Código QR detectado");

            // Evitar procesar múltiples lecturas simultáneas
            if (_qrProcesando)
            {
                Console.WriteLine("[DEBUG] OnQrDetected - Procesamiento en curso, ignorando");
                return;
            }

            _qrProcesando = true;
            Console.WriteLine("[DEBUG] OnQrDetected - Marcando como procesando");

            // Detener escáner de inmediato
            QrScanner.IsDetecting = false;
            Console.WriteLine($"[DEBUG] OnQrDetected - Escáner detenido. IsDetecting: {QrScanner.IsDetecting}");

            string codigoQR = e.Results?.FirstOrDefault()?.Value ?? "";
            Console.WriteLine($"[DEBUG] OnQrDetected - Código QR leído: '{codigoQR}'");

            if (string.IsNullOrWhiteSpace(codigoQR))
            {
                Console.WriteLine("[DEBUG] OnQrDetected - Código QR vacío, reactivando escáner");
                _qrProcesando = false;
                QrScanner.IsDetecting = true;
                return;
            }

            // Actualizar UI en hilo principal
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                QrStatusLabel.Text = "Verificando credencial...";
                QrStatusLabel.TextColor = Color.FromArgb("#6DBF2E");
                QrStatusLabel.IsVisible = true;
                OcultarError();

                try
                {
                    Console.WriteLine($"[DEBUG] OnQrDetected - Buscando usuario con QR: {codigoQR}");
                    var user = await db.GetUsuarioByQRAsync(codigoQR);

                    if (user == null)
                    {
                        Console.WriteLine("[DEBUG] OnQrDetected - Usuario no encontrado para este QR");
                        MostrarError("Código QR no reconocido.");
                        QrStatusLabel.IsVisible = false;
                        // Reintentar después de 2 segundos
                        await Task.Delay(2000);
                        _qrProcesando = false;
                        QrScanner.IsDetecting = true;
                        Console.WriteLine("[DEBUG] OnQrDetected - Escáner reactivado después de error");
                        return;
                    }

                    Console.WriteLine($"[DEBUG] OnQrDetected - Usuario encontrado: {user.Nombre}, Activo: {user.Activo}");

                    if (!user.Activo)
                    {
                        Console.WriteLine("[DEBUG] OnQrDetected - Usuario inactivo");
                        MostrarError("Tu cuenta está desactivada. Contacta a tu supervisor.");
                        QrStatusLabel.IsVisible = false;
                        await Task.Delay(2000);
                        _qrProcesando = false;
                        try
                        {
                            QrScanner.IsDetecting = true;
                            Console.WriteLine("[DEBUG] OnQrDetected - Escáner reactivado después de usuario inactivo");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] OnQrDetected - Error al reactivar escáner: {ex.Message}");
                            MostrarError($"Error al iniciar cámara: {ex.Message}");
                        }

                        return;
                    }

                    Console.WriteLine("[DEBUG] OnQrDetected - Login exitoso con QR");
                    QrStatusLabel.Text = $"¡Bienvenido, {user.Nombre}!";
                    await MostrarBienvenida(user.Nombre, user.Rol);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] OnQrDetected - Excepción: {ex.Message}");
                    Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
                    MostrarError($"Error de conexión: {ex.Message}");
                    QrStatusLabel.IsVisible = false;
                    await Task.Delay(2000);
                    _qrProcesando = false;
                    QrScanner.IsDetecting = true;
                    Console.WriteLine("[DEBUG] OnQrDetected - Escáner reactivado después de excepción");
                }
            });
        }

        // ─────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────

        private async Task MostrarBienvenida(string nombre, int rol)
        {
            string rolTexto = rol == 1 ? "Supervisor" : "Guardia";
            Console.WriteLine($"[DEBUG] MostrarBienvenida - Usuario: {nombre}, Rol: {rolTexto}");
            await DisplayAlertAsync("Bienvenido", $"Hola {nombre}, ingresaste como {rolTexto}.", "OK");

            // Aquí después navegarás a la pantalla principal:
            // await Shell.Current.GoToAsync("//HomePage");
            Console.WriteLine("[DEBUG] MostrarBienvenida - Alerta mostrada");
        }

        private static string HashSHA256(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private void MostrarError(string mensaje)
        {
            Console.WriteLine($"[ERROR] {mensaje}");
            ErrorLabel.Text = mensaje;
            ErrorLabel.IsVisible = true;
        }

        private void OcultarError()
        {
            Console.WriteLine("[DEBUG] OcultarError - Ocultando mensaje de error");
            ErrorLabel.IsVisible = false;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Console.WriteLine("[DEBUG] OnAppearing - Página apareciendo");
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            Console.WriteLine($"[DEBUG] OnSizeAllocated - Width: {width}, Height: {height}");

            // Si estamos en el tab QR y el escáner no está detectando, intentar activarlo
            if (PanelQR.IsVisible && !QrScanner.IsDetecting)
            {
                Console.WriteLine("[DEBUG] OnSizeAllocated - Intentando activar escáner tardío");
                Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        QrScanner.IsDetecting = true;
                        Console.WriteLine($"[DEBUG] OnSizeAllocated - Escáner activado: {QrScanner.IsDetecting}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] OnSizeAllocated - {ex.Message}");
                    }
                });
            }
        }
    }
}