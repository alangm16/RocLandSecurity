using RocLandSecurity.Models;
using RocLandSecurity.Services;
using ZXing.Net.Maui;

namespace RocLandSecurity.Views.Guardia
{
    [QueryProperty(nameof(RondinId), "rondinId")]
    public partial class RondinActivoPage : ContentPage
    {
        private readonly OfflineDatabaseService _offline;
        private readonly SessionService _session;

        private int _rondinId;
        public string RondinId
        {
            set => _rondinId = int.TryParse(value, out var id) ? id : 0;
        }

        private List<RondinPunto> _puntos = new();
        private RondinPunto? _puntoActual;
        private bool _escaneando = false;
        private bool _finalizando = false;
        private bool _flashEncendido = false;
        private DateTime _horaProgramada = DateTime.MinValue;
        private int _turnoId = 0;
        private System.Timers.Timer? _expiracionTimer;

        public RondinActivoPage(OfflineDatabaseService offline, SessionService session)
        {
            InitializeComponent();
            _offline = offline;
            _session = session;
        }

        // ─────────────────────────────────────────────────────────────────
        // CICLO DE VIDA
        // ─────────────────────────────────────────────────────────────────

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            ReiniciarScanner();

            // Forzar cierre de scanner y overlay
            if (PanelScanner != null)
            {
                PanelScanner.IsVisible = false;
            }
            if (QrScanner != null)
            {
                QrScanner.IsDetecting = false;
                QrScanner.IsTorchOn = false;
            }
            _flashEncendido = false;
            _escaneando = false;

            if (_rondinId == 0) return;
            await IniciarYCargarAsync();

            // Timer que verifica cada 30 segundos si el rondín expiró mientras el guardia escaneaba.
            if (AppConfig.ModoEstrictoRondines)
            {
                _expiracionTimer = new System.Timers.Timer(30_000);
                _expiracionTimer.Elapsed += async (s, e) =>
                    await MainThread.InvokeOnMainThreadAsync(VerificarExpiracionAsync);
                _expiracionTimer.Start();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _expiracionTimer?.Stop();
            _expiracionTimer?.Dispose();
            _expiracionTimer = null;
            QrScanner.IsDetecting = false;
            QrScanner.IsTorchOn = false;
            PanelScanner.IsVisible = false;
            _flashEncendido = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // EXPIRACIÓN EN TIEMPO REAL
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Llamado por el timer cada 30 s.
        /// Si el tiempo de cierre del rondín ya pasó y el rondín sigue abierto,
        /// lo cierra automáticamente, apaga el escáner y regresa al home.
        /// </summary>
        private async Task VerificarExpiracionAsync()
        {
            if (_finalizando || _horaProgramada == DateTime.MinValue) return;

            var cierre = _horaProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);
            if (DateTime.Now <= cierre) return;

            // El tiempo venció — detener todo primero para evitar acciones paralelas
            _finalizando = true;
            _expiracionTimer?.Stop();

            if (QrScanner != null)
            {
                QrScanner.IsDetecting = false;
                QrScanner.IsTorchOn = false;
            }
            PanelScanner.IsVisible = false;
            _flashEncendido = false;

            // Cerrar el rondín localmente (y subir al servidor si hay red)
            try
            {
                await _offline.FinalizarRondinAsync(_rondinId);
            }
            catch { /* No crítico: ya se sincronizará */ }

            await ShowToastAsync(
                $"Tiempo del rondín de las {_horaProgramada:HH:mm} finalizado. " +
                $"Se cerró automáticamente.", isError: true);

            await Task.Delay(2800);
            await Shell.Current.GoToAsync("..");
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA
        // ─────────────────────────────────────────────────────────────────

        private async Task IniciarYCargarAsync()
        {
            // Si ya están cargados los puntos (ej: volvemos de ReportarIncidencia),
            // solo refrescar la UI sin tocar la BD
            if (_puntos.Count > 0)
            {
                _puntos = await _offline.GetPuntosDeRondinAsync(_rondinId);
                ActualizarUI();
                RenderizarPuntos();
                return;
            }

            LoadingIndicator.IsVisible = true;
            ListaPuntos.Children.Clear();

            try
            {
                // Obtener hora programada y turnoId
                (_horaProgramada, _turnoId) = await _offline.GetDatosRondinAsync(_rondinId);

                // Verificar y generar puntos si faltan
                int totalPuntos = await _offline.AsegurarPuntosRondinAsync(_rondinId);
                LblEstadoRondin.Text = $"Verificando {totalPuntos} puntos...";

                // Iniciar o retomar — nunca lanza error por estado
                await _offline.IniciarRondinAsync(_rondinId);

                // Cargar puntos
                _puntos = await _offline.GetPuntosDeRondinAsync(_rondinId);

                if (_puntos.Count == 0)
                {
                    await ShowToastAsync("No se encontraron puntos para este rondín.");
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                ActualizarUI();
                RenderizarPuntos();
            }
            catch (InvalidOperationException ioe)
            {
                await ShowToastAsync(ioe.Message);
                await Task.Delay(2500);
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error al cargar rondín: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // UI — ESTADO GENERAL
        // ─────────────────────────────────────────────────────────────────

        private void ActualizarUI()
        {
            int visitados = _puntos.Count(p => p.EsVisitado);
            int total = _puntos.Count;
            double pct = total > 0 ? (double)visitados / total : 0;

            LblPorcentaje.Text = $"{visitados}/{total}";
            LblProgreso.Text = $"{(int)(pct * 100)}% completado";
            BarraProgreso.ProgressTo(pct, 300, Easing.CubicOut);

            _puntoActual = _puntos.FirstOrDefault(p => p.EsPendiente);

            string hora = _horaProgramada != DateTime.MinValue
                ? _horaProgramada.ToString("HH:mm") : "--:--";
            LblTituloRondin.Text = $"Rondín {hora} hrs";

            if (_puntoActual != null)
            {
                LblEstadoRondin.Text = "Iniciado:";
                BtnEscanear.IsVisible = true;
                BtnFinalizar.IsVisible = false;

                LblPuntoActualScanner.Text = _puntoActual.NombrePunto;
                LblOrdenActualScanner.Text = $"Punto {_puntoActual.OrdenPunto} de {total}";
            }
            else
            {
                LblEstadoRondin.Text = "¡Completado!";
                BtnEscanear.IsVisible = false;
                BtnFinalizar.IsVisible = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // UI — LISTA DE PUNTOS
        // ─────────────────────────────────────────────────────────────────

        private void RenderizarPuntos()
        {
            ListaPuntos.Children.Clear();
            foreach (var punto in _puntos)
                ListaPuntos.Children.Add(CrearFilaPunto(punto));
        }

        private View CrearFilaPunto(RondinPunto punto)
        {
            bool esActual = _puntoActual?.ID == punto.ID;
            var clr = Color.FromArgb(punto.EstadoColor);

            var card = new Border
            {
                BackgroundColor = esActual
                    ? Color.FromArgb("#1A2A1A")
                    : Color.FromArgb("#1A1A1A"),
                StrokeThickness = esActual ? 1 : 0.5,
                Stroke = esActual
                    ? Color.FromArgb("#2A4A2A")
                    : Color.FromArgb("#2A2A2A"),
                Padding = new Thickness(14, 12),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(12) };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(40) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                }
            };

            // Número / checkmark
            var numBorder = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                BackgroundColor = punto.EsVisitado
                    ? Color.FromArgb("#1A3A1A")
                    : esActual
                        ? Color.FromArgb("#1A2A1A")
                        : Color.FromArgb("#252525"),
                StrokeThickness = 0,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            numBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(8) };
            numBorder.Content = new Label
            {
                Text = punto.EsVisitado ? "✓" : punto.OrdenPunto.ToString(),
                TextColor = clr,
                FontSize = punto.EsVisitado ? 14 : 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(numBorder, 0);

            // Nombre + hora visita
            var infoStack = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            infoStack.Children.Add(new Label
            {
                Text = punto.NombrePunto,
                TextColor = punto.EsVisitado || esActual ? Colors.White : Color.FromArgb("#CCCCCC"),
                FontSize = 14,
                FontAttributes = esActual ? FontAttributes.Bold : FontAttributes.None,
            });
            if (punto.EsVisitado)
            {
                infoStack.Children.Add(new Label
                {
                    Text = $"Escaneado: {punto.HoraVisitaStr}",
                    TextColor = Color.FromArgb("#6DBF2E"),
                    FontSize = 12,
                });
            }
            Grid.SetColumn(infoStack, 1);

            // Botón Escanear inline (solo punto activo)
            if (esActual)
            {
                var btnEsc = new Border
                {
                    BackgroundColor = Color.FromArgb("#6DBF2E"),
                    StrokeThickness = 0,
                    Padding = new Thickness(12, 8),
                    VerticalOptions = LayoutOptions.Center,
                };
                btnEsc.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(10) };

                var btnLbl = new Label
                {
                    Text = "▣ Escanear",
                    TextColor = Color.FromArgb("#111111"),
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                };
                btnEsc.Content = btnLbl;
                btnEsc.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() => OnEscanearClicked(null, EventArgs.Empty))
                });
                Grid.SetColumn(btnEsc, 2);
                grid.Children.Add(btnEsc);
            }

            grid.Children.Add(numBorder);
            grid.Children.Add(infoStack);
            card.Content = grid;
            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // ESCANEO QR
        // ─────────────────────────────────────────────────────────────────

        private async void OnEscanearClicked(object? sender, EventArgs e)
        {
            if (_puntoActual == null) return;

            // Verificar que el tiempo del rondín no haya vencido justo en este momento
            if (AppConfig.ModoEstrictoRondines && _horaProgramada != DateTime.MinValue)
            {
                var cierre = _horaProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);
                if (DateTime.Now > cierre)
                {
                    await VerificarExpiracionAsync();
                    return;
                }
            }

            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await ShowToastAsync("Se necesita permiso de cámara");
                return;
            }

            _escaneando = false;
            _flashEncendido = false;
            LblScannerStatus.Text = "";
            ActualizarIconoFlash(false);
            PanelScanner.IsVisible = true;
            QrScanner.IsDetecting = true;
        }

        private void OnCancelarScanClicked(object sender, EventArgs e)
        {
            QrScanner.IsDetecting = false;
            QrScanner.IsTorchOn = false;
            PanelScanner.IsVisible = false;
            _escaneando = false;
            _flashEncendido = false;
            ActualizarIconoFlash(false);
        }

        private void OnFlashScannerClicked(object sender, EventArgs e)
        {
            _flashEncendido = !_flashEncendido;
            QrScanner.IsTorchOn = _flashEncendido;
            ActualizarIconoFlash(_flashEncendido);
        }

        private void ActualizarIconoFlash(bool encendido)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (BtnFlashBorder == null) return;
                BtnFlashBorder.BackgroundColor = encendido
                    ? Color.FromArgb("#6DBF2E")
                    : Color.FromArgb("#333333");
                if (ImgFlash != null)
                    ImgFlash.Source = encendido ? "flash_on.png" : "flash_off.png";
            });
        }
        private void ReiniciarScanner()
        {
            if (PanelScanner == null) return;

            // Guardar estado de flash
            bool flashAntes = _flashEncendido;

            // Remover el scanner actual
            if (QrScanner != null)
            {
                PanelScanner.Children.Remove(QrScanner);
                QrScanner.BarcodesDetected -= OnQrDetected;
                QrScanner = null;
            }

            // Crear nueva instancia del scanner
            QrScanner = new ZXing.Net.Maui.Controls.CameraBarcodeReaderView
            {
                IsDetecting = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
            QrScanner.BarcodesDetected += OnQrDetected;

            // Insertar nuevamente en la vista
            PanelScanner.Children.Insert(0, QrScanner);

            // Restaurar flash si estaba encendido
            if (flashAntes)
            {
                QrScanner.IsTorchOn = true;
                _flashEncendido = true;
            }
            else
            {
                _flashEncendido = false;
            }

            _escaneando = false;
        }

        private async void OnRefrescarScannerClicked(object sender, EventArgs e)
        {
            BtnRefrescarBorder.BackgroundColor = Color.FromArgb("#6DBF2E");
            LblScannerStatus.Text = "Reiniciando cámara...";
            LblScannerStatus.TextColor = Color.FromArgb("#6DBF2E");

            await Task.Delay(200);

            ReiniciarScanner();

            await Task.Delay(600);

            LblScannerStatus.Text = "";
            BtnRefrescarBorder.BackgroundColor = Color.FromArgb("#333333");
        }

        private async void OnQrDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if (_escaneando || _puntoActual == null) return;
            _escaneando = true;
            QrScanner.IsDetecting = false;

            string codigoLeido = e.Results?.FirstOrDefault()?.Value ?? "";
            if (string.IsNullOrWhiteSpace(codigoLeido))
            {
                _escaneando = false;
                QrScanner.IsDetecting = true;
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                RondinPunto? puntoEscaneado = null;
                try { puntoEscaneado = await _offline.GetRondinPuntoPorQRAsync(_rondinId, codigoLeido); }
                catch { }

                if (puntoEscaneado == null)
                {
                    LblScannerStatus.Text = "QR no reconocido";
                    LblScannerStatus.TextColor = Color.FromArgb("#F09595");
                    await Task.Delay(1200);
                    LblScannerStatus.Text = "";
                    _escaneando = false;
                    QrScanner.IsDetecting = true;
                    return;
                }

                if (puntoEscaneado.ID != _puntoActual.ID)
                {
                    LblScannerStatus.Text = $"Escanea primero: {_puntoActual.NombrePunto}";
                    LblScannerStatus.TextColor = Color.FromArgb("#FAC775");
                    await Task.Delay(1500);
                    LblScannerStatus.Text = "";
                    _escaneando = false;
                    QrScanner.IsDetecting = true;
                    return;
                }

                LblScannerStatus.Text = $"✓ {puntoEscaneado.NombrePunto}";
                LblScannerStatus.TextColor = Color.FromArgb("#97C459");

                try
                {
                    double? lat = null, lon = null;
                    try
                    {
                        var loc = await Geolocation.GetLastKnownLocationAsync();
                        lat = loc?.Latitude;
                        lon = loc?.Longitude;
                    }
                    catch { }

                    await _offline.RegistrarVisitaPuntoAsync(puntoEscaneado.ID, lat, lon, _rondinId, codigoLeido);

                    var local = _puntos.First(p => p.ID == puntoEscaneado.ID);
                    local.Estado = 1;
                    local.HoraVisita = DateTime.Now;

                    await Task.Delay(700);
                    PanelScanner.IsVisible = false;
                    _escaneando = false;
                    _flashEncendido = false;
                    QrScanner.IsTorchOn = false;
                    ActualizarIconoFlash(false);

                    ActualizarUI();
                    RenderizarPuntos();

                    // Scroll al siguiente punto
                    if (_puntoActual != null)
                    {
                        int idx = _puntos.IndexOf(_puntoActual);
                        if (idx >= 0)
                            await ScrollPuntos.ScrollToAsync(0, Math.Max(0, idx * 64 - 80), false);
                    }
                }
                catch (Exception ex)
                {
                    PanelScanner.IsVisible = false;
                    _escaneando = false;
                    await ShowToastAsync($"Error al registrar: {ex.Message}");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // FINALIZAR / VOLVER / INCIDENCIA
        // ─────────────────────────────────────────────────────────────────

        private async void OnFinalizarClicked(object sender, EventArgs e)
        {
            if (_finalizando) return;

            int pendientes = _puntos.Count(p => p.EsPendiente);
            if (pendientes > 0)
            {
                bool ok = await DisplayAlertAsync(
                    "Finalizar rondín",
                    $"Quedan {pendientes} puntos sin escanear. ¿Finalizar de todas formas? Se marcarán como omitidos.",
                    "Sí, finalizar", "Cancelar");
                if (!ok) return;
            }

            _finalizando = true;
            BtnFinalizar.IsEnabled = false;
            BtnFinalizar.Text = "Finalizando...";

            try
            {
                await _offline.FinalizarRondinAsync(_rondinId);
                await ShowToastAsync("Rondín finalizado", isError: false);
                await Task.Delay(1000);
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error: {ex.Message}");
                _finalizando = false;
                BtnFinalizar.IsEnabled = true;
                BtnFinalizar.Text = "✓  Finalizar rondín";
            }
        }

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            bool ok = await DisplayAlertAsync(
                "Salir del rondín",
                "El rondín quedará en progreso. Puedes retomarlo desde la pantalla principal.",
                "Salir", "Cancelar");
            if (ok) await Shell.Current.GoToAsync("..");
        }

        private async void OnReportarIncidenciaClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(
                $"reportarincidencia?rondinId={_rondinId}&turnoId={_turnoId}");
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message, bool isError = true)
        {
            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = isError
                ? Color.FromArgb("#FF5555")
                : Color.FromArgb("#6DBF2E");
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;
            await ToastFrame.FadeToAsync(1, 200);
            await Task.Delay(2200);
            await ToastFrame.FadeToAsync(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}