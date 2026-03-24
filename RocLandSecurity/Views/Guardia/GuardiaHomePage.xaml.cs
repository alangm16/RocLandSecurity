using RocLandSecurity.Models;
using RocLandSecurity.Services;
using System.Runtime.ConstrainedExecution;

namespace RocLandSecurity.Views.Guardia
{
    public partial class GuardiaHomePage : ContentPage
    {
        private readonly OfflineDatabaseService _offline;
        private readonly SessionService _session;
        private Turno? _turnoActivo;
        private List<Rondin> _rondines = new();
        private System.Timers.Timer? _refreshTimer;

        public GuardiaHomePage(OfflineDatabaseService offline, SessionService session)
        {
            InitializeComponent();
            _offline = offline;
            _session = session;

        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarDatosAsync();

            // Si modo estricto está activo, iniciar timer para refrescar UI
            if (AppConfig.ModoEstrictoRondines)
            {
                _refreshTimer = new System.Timers.Timer(60000); // Cada 60 segundos
                _refreshTimer.Elapsed += async (s, e) =>
                {
                    if (PanelRondines.IsVisible)
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await CargarDatosAsync();
                        });
                    }
                };
                _refreshTimer.Start();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarDatosAsync()
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            LblNombre.Text = usuario.Nombre;
            LblFechaTurno.Text = DateTime.Now.ToString(
                "dddd dd 'de' MMMM",
                new System.Globalization.CultureInfo("es-MX"));

            LoadingIndicator.IsVisible = true;
            PanelSinTurno.IsVisible = false;
            PanelRondines.IsVisible = false;

            try
            {
                _turnoActivo = await _offline.GetTurnoActivoAsync(usuario.ID);

                if (_turnoActivo == null)
                    MostrarSinTurno();
                else
                {
                    // Cerrar automáticamente los rondines cuya ventana de tiempo venció
                    // antes de renderizar, para que la UI refleje el estado real.
                    if (AppConfig.ModoEstrictoRondines)
                        await _offline.ExpirarRondinesVencidosAsync(_turnoActivo.ID);

                    _rondines = await _offline.GetRondinesPorTurnoAsync(_turnoActivo.ID);
                    MostrarRondines();
                }
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error de conexión: {ex.Message}", true);
                MostrarSinTurno();
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // RENDERIZAR
        // ─────────────────────────────────────────────────────────────────

        private void MostrarSinTurno()
        {
            PanelSinTurno.IsVisible = true;
            PanelRondines.IsVisible = false;
        }

        private void MostrarRondines()
        {
            // Separar rondines por estado
            var enProgreso = _rondines.Where(r => r.Estado == 1).ToList();
            var pendientes = _rondines.Where(r => r.Estado == 0).OrderBy(r => r.HoraProgramada).ToList();
            var completados = _rondines.Where(r => r.Estado >= 2).OrderBy(r => r.HoraProgramada).ToList();

            // Actualizar header
            LblNombre.Text = "Rondines";
            LblFechaTurno.Text = $"Turno nocturno · {DateTime.Now:dd/M/yyyy}";

            // ── En progreso ──────────────────────────────────
            PanelEnProgreso.Children.Clear();
            LblSeccionEnProgreso.IsVisible = enProgreso.Count > 0;
            PanelEnProgreso.IsVisible = enProgreso.Count > 0;
            foreach (var r in enProgreso)
                PanelEnProgreso.Children.Add(CrearTarjetaEnProgreso(r));

            // ── Pendientes ───────────────────────────────────
            ListaPendientes.Children.Clear();
            LblSeccionPendientes.IsVisible = pendientes.Count > 0;
            if (pendientes.Count > 0)
                LblSeccionPendientes.Text = $"PENDIENTES ({pendientes.Count})";
            foreach (var r in pendientes)
                ListaPendientes.Children.Add(CrearTarjetaRondin(r));

            // ── Completados ──────────────────────────────────
            ListaCompletados.Children.Clear();
            LblSeccionCompletados.IsVisible = completados.Count > 0;
            foreach (var r in completados)
                ListaCompletados.Children.Add(CrearTarjetaRondin(r));

            PanelSinTurno.IsVisible = false;
            PanelRondines.IsVisible = true;
        }


        private View CrearTarjetaEnProgreso(Rondin rondin)
        {
            var clr = Color.FromArgb(rondin.EstadoColor);

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1A1A1A"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2A4A2A"),
                Padding = new Thickness(0),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(14) };

            var innerGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = GridLength.Star },
                }
            };

            innerGrid.Children.Add(new BoxView
            {
                Color = Color.FromArgb("#1A3A1A"),
                VerticalOptions = LayoutOptions.Fill
            });

            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(40) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(24) },
                }
            };

            // Icono play
            var iconBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#1A3A1A"),
                StrokeThickness = 0,
                WidthRequest = 36,
                HeightRequest = 36,
                VerticalOptions = LayoutOptions.Center,
            };
            iconBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };
            iconBorder.Content = new Image
            {
                Source = "play.png",
                WidthRequest = 24,
                HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(iconBorder, 0); Grid.SetRow(iconBorder, 0);

            // Texto
            var info = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            info.Children.Add(new Label
            {
                Text = "Rondín en progreso",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
            });
            info.Children.Add(new Label
            {
                Text = $"Programado: {rondin.HoraProgramadaStr} hrs",
                TextColor = Color.FromArgb("#888888"),
                FontSize = 12,
            });
            Grid.SetColumn(info, 1); Grid.SetRow(info, 0);

            // Flecha
            var arrow = new Label
            {
                Text = "›",
                TextColor = Color.FromArgb("#888888"),
                FontSize = 22,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(arrow, 2); Grid.SetRow(arrow, 0);

            // Barra de progreso
            double pct = rondin.PuntosTotal > 0
                ? (double)rondin.PuntosVisitados / rondin.PuntosTotal
                : 0;
            var progress = new ProgressBar
            {
                Progress = pct,
                ProgressColor = clr,
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                HeightRequest = 4,
                Margin = new Thickness(0, 12, 0, 0),
            };
            Grid.SetRow(progress, 1); Grid.SetColumn(progress, 0); Grid.SetColumnSpan(progress, 3);

            var btnContinuar = new Button
            {
                Text = "Continuar rondín",
                BackgroundColor = Color.FromArgb("#6DBF2E"),
                TextColor = Colors.Black,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 10,
                HeightRequest = 50,
                Margin = new Thickness(0, 12, 0, 0),
            };
            btnContinuar.Clicked += async (s, e) => await IrARondinActivoAsync(rondin);
            Grid.SetRow(btnContinuar, 2);
            Grid.SetColumn(btnContinuar, 0);
            Grid.SetColumnSpan(btnContinuar, 3);

            var wrapper = new Grid
            {
                Padding = new Thickness(16, 14),
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                }
            };
            grid.Children.Add(iconBorder);
            grid.Children.Add(info);
            grid.Children.Add(arrow);
            grid.Children.Add(progress);
            grid.Children.Add(btnContinuar);
            wrapper.Children.Add(grid);
            Grid.SetColumn(wrapper, 1);
            innerGrid.Children.Add(wrapper);
            card.Content = innerGrid;

            // Tap para retomar
            /*card.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await IrARondinActivoAsync(rondin))
            });*/

            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // TARJETA DE RONDÍN
        // ─────────────────────────────────────────────────────────────────

        private View CrearTarjetaRondin(Rondin rondin)
        {
            var clr = Color.FromArgb(rondin.EstadoColor);
            var fondo = Color.FromArgb(rondin.EstadoColorFondo);

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1C1C1C"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2E2E2E"),
                Padding = new Thickness(0),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(12) };

            var innerGrid = new Grid
            {
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(4) },
            new ColumnDefinition { Width = GridLength.Star },
        }
            };

            innerGrid.Children.Add(new BoxView
            {
                Color = clr,
                VerticalOptions = LayoutOptions.Fill
            });
            var boxView = new BoxView
            {
                Color = clr,
                VerticalOptions = LayoutOptions.Fill
            };
            innerGrid.Children.Add(boxView);
            Grid.SetColumn(boxView, 0);

            var contenido = new Grid
            {
                Padding = new Thickness(14, 12),
                RowDefinitions =
        {
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto },
        },
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = GridLength.Star },
            new ColumnDefinition { Width = GridLength.Auto },
        }
            };
            Grid.SetColumn(contenido, 1);

            var imgClock = new Image
            {
                Source = "clock.png",
                WidthRequest = 14,
                HeightRequest = 14,
                VerticalOptions = LayoutOptions.Center,
            };


            var lblHora = new Label
            {
                Text = $"Rondín {rondin.HoraProgramadaStr} hrs",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
            };

            var headerHora = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center
            };
            headerHora.Children.Add(imgClock);
            headerHora.Children.Add(lblHora);
            Grid.SetRow(headerHora, 0); Grid.SetColumn(headerHora, 0);

            // Badge estado
            var badge = new Border
            {
                BackgroundColor = fondo,
                StrokeThickness = 1,
                Stroke = clr,
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center,
            };
            badge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };
            badge.Content = new Label
            {
                Text = rondin.EstadoTexto,
                TextColor = clr,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetRow(badge, 0); Grid.SetColumn(badge, 1);

            // Info puntos / duración
            var lblInfo = new Label
            {
                Text = rondin.EstaFinalizado
                    ? $"{rondin.PuntosStr}  ·  {rondin.DuracionStr}"
                    : rondin.PuntosStr,
                TextColor = Color.FromArgb("#888888"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            };
            Grid.SetRow(lblInfo, 1); Grid.SetColumn(lblInfo, 0);
            Grid.SetColumnSpan(lblInfo, 2);

            // Barra de progreso
            double pct = rondin.PuntosTotal > 0
                ? (double)rondin.PuntosVisitados / rondin.PuntosTotal : 0;
            var progress = new ProgressBar
            {
                Progress = pct,
                ProgressColor = clr,
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                HeightRequest = 4,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(progress, 2); Grid.SetColumn(progress, 0);
            Grid.SetColumnSpan(progress, 2);

            contenido.Children.Add(headerHora);
            contenido.Children.Add(badge);
            contenido.Children.Add(lblInfo);
            contenido.Children.Add(progress);

            // Botón iniciar — solo en el primer rondín pendiente
            if (rondin.EsIniciable && EsPrimerPendiente(rondin))
            {
                var estadoInicio = EvaluarEstadoInicio(rondin);

                string btnTexto = estadoInicio switch
                {
                    EstadoInicioRondin.Disponible => "Iniciar rondín",
                    EstadoInicioRondin.Pronto     => $"Disponible a las {rondin.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos):HH:mm}",
                    EstadoInicioRondin.Vencido    => $"Tiempo vencido — {rondin.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos):HH:mm}",
                    _                              => "No disponible"
                };
                Color btnFondo = estadoInicio == EstadoInicioRondin.Disponible
                    ? Color.FromArgb("#6DBF2E")
                    : Color.FromArgb("#555555");
                Color btnTextoClr = estadoInicio == EstadoInicioRondin.Disponible
                    ? Color.FromArgb("#111111")
                    : Color.FromArgb("#AAAAAA");

                var btn = new Button
                {
                    Text = btnTexto,
                    BackgroundColor = btnFondo,
                    TextColor = btnTextoClr,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 14,
                    CornerRadius = 10,
                    HeightRequest = 50,
                    Margin = new Thickness(0, 12, 0, 0),
                    IsEnabled = estadoInicio == EstadoInicioRondin.Disponible,
                };
                btn.Clicked += async (s, e) => await IrARondinActivoAsync(rondin);

                contenido.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(btn, 3); Grid.SetColumn(btn, 0);
                Grid.SetColumnSpan(btn, 2);
                contenido.Children.Add(btn);
            }
            // Rondín en progreso — botón para retomar
            else if (rondin.EstaEnProgreso)
            {
                var btn = new Button
                {
                    Text = "Continuar rondín",
                    BackgroundColor = Color.FromArgb("#185FA5"),
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 14,
                    CornerRadius = 10,
                    HeightRequest = 50,
                    Margin = new Thickness(0, 12, 0, 0),
                };
                btn.Clicked += async (s, e) => await IrARondinActivoAsync(rondin);

                contenido.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(btn, 3); Grid.SetColumn(btn, 0);
                Grid.SetColumnSpan(btn, 2);
                contenido.Children.Add(btn);
            }

            innerGrid.Children.Add(contenido);
            card.Content = innerGrid;
            return card;
        }

        private bool EsPrimerPendiente(Rondin rondin)
        {
            var pendientes = _rondines
                .Where(r => r.Estado == 0)
                .OrderBy(r => r.HoraProgramada)
                .ToList();
            return pendientes.Count > 0 && pendientes[0].ID == rondin.ID;
        }

        // ─────────────────────────────────────────────────────────────────
        // EVENTOS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evalúa si un rondín puede iniciarse en este momento según AppConfig.
        /// Retorna:
        ///   Disponible  → dentro de la ventana, botón verde habilitado.
        ///   Pronto      → antes de la apertura, botón gris con hora de apertura.
        ///   Vencido     → después del cierre (no debería verse en pendientes
        ///                 porque ExpirarRondinesVencidosAsync ya lo cerró, pero
        ///                 se maneja por si acaso el timer aún no corrió).
        /// </summary>
        private enum EstadoInicioRondin { Disponible, Pronto, Vencido }

        private EstadoInicioRondin EvaluarEstadoInicio(Rondin rondin)
        {
            if (!AppConfig.ModoEstrictoRondines) return EstadoInicioRondin.Disponible;

            var ahora = DateTime.Now;
            var apertura = rondin.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
            var cierre   = rondin.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);

            if (ahora < apertura)  return EstadoInicioRondin.Pronto;
            if (ahora > cierre)    return EstadoInicioRondin.Vencido;
            return EstadoInicioRondin.Disponible;
        }

        private bool PuedeIniciarRondin(Rondin rondin) =>
            EvaluarEstadoInicio(rondin) == EstadoInicioRondin.Disponible;

        private async void OnIniciarTurnoClicked(object sender, EventArgs e)
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            BtnIniciarTurno.IsEnabled = false;
            BtnIniciarTurno.Text = "Creando turno...";

            try
            {
                _turnoActivo = await _offline.CrearTurnoYRondinesAsync(usuario.ID);
                await ShowToastAsync("Turno iniciado correctamente", false);
                await CargarDatosAsync();
            }
            catch (InvalidOperationException ioe)
            {
                // Duplicado — turno ya existe
                await ShowToastAsync(ioe.Message, true);
                await CargarDatosAsync();
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error: {ex.Message}", true);
                BtnIniciarTurno.IsEnabled = true;
                BtnIniciarTurno.Text = "Iniciar turno de hoy";
            }
        }

        private async Task IrARondinActivoAsync(Rondin rondin)
        {
            await Shell.Current.GoToAsync(
                $"rondinactivo?rondinId={rondin.ID}");
        }

        /// <summary>
        /// Botón de incidencia fuera de rondín activo (desde la tab principal).
        /// Solo disponible si hay turno activo.
        /// </summary>
        private async void OnReportarIncidenciaFueraRondinClicked(object sender, EventArgs e)
        {
            if (_turnoActivo == null)
            {
                await ShowToastAsync("Debes iniciar un turno primero.");
                return;
            }
            await Shell.Current.GoToAsync(
                $"reportarincidencia?rondinId=0&turnoId={_turnoActivo.ID}");
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message, bool esError = true)
        {
            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = esError
                ? Color.FromArgb("#FF5555")
                : Color.FromArgb("#6DBF2E");
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;
            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}