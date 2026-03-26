using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class SupervisorHomePage : ContentPage
    {
        private readonly SupervisorDatabaseService _db;
        private readonly SessionService _session;
        private readonly OfflineDatabaseService _offline;

        public SupervisorHomePage(SupervisorDatabaseService db, SessionService session, OfflineDatabaseService offline)
        {
            InitializeComponent();
            _db = db;
            _session = session;
            _offline = offline;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarDatosAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarDatosAsync()
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            LblNombreSupervisor.Text = usuario.Nombre;
            LblFechaHoy.Text = DateTime.Now.ToString("dddd dd 'de' MMMM",
                new System.Globalization.CultureInfo("es-MX"));

            LoadingIndicator.IsVisible = true;
            PanelContenido.IsVisible = false;

            try
            {
                // Obtener turno activo y su información
                var turnoActivo = await _db.GetTurnoActivoAsync();
                var nombreGuardia = "Sin turno activo";

                if (turnoActivo != null)
                {
                    nombreGuardia = turnoActivo.NombreGuardia;
                    LblTurnoInfo.Text = $"Turno nocturno · {DateTime.Now:dd/M/yyyy} · {nombreGuardia}";
                }
                else
                {
                    LblTurnoInfo.Text = "Turno nocturno · Sin turno activo";
                }

                // Métricas y rondines en paralelo
                var metricasTask = _db.GetMetricasTurnoActivoAsync();
                var rondinesTask = _db.GetRondinesTurnoActivoAsync();

                await Task.WhenAll(metricasTask, rondinesTask);

                var (completados, enProgreso, pendientes, incidencias) = metricasTask.Result;
                var rondines = rondinesTask.Result;

                // Calcular cumplimiento
                int totalRondines = rondines.Count;

                int rondinesCumplidos = rondines.Count(r =>
                    r.PuntosTotal > 0 &&
                    r.PuntosVisitados >= r.PuntosTotal
                );

                double cumplimiento = totalRondines > 0
                    ? (double)rondinesCumplidos / totalRondines * 100
                    : 0;

                // Actualizar tarjetas métricas
                LblCompletados.Text = completados.ToString();
                LblPendientes.Text = pendientes.ToString();
                LblIncidencias.Text = incidencias.ToString();
                LblCumplimientoDetalle.Text = $"{cumplimiento:F0}%";
                CumplimientoProgress.Progress = cumplimiento / 100;

                LblTotalRondines.Text = $"{rondines.Count} rondines";

                // Construir lista
                ListaRondines.Children.Clear();
                if (rondines.Count == 0)
                {
                    PanelSinRondines.IsVisible = true;
                }
                else
                {
                    PanelSinRondines.IsVisible = false;
                    foreach (var rondin in rondines)
                        ListaRondines.Children.Add(CrearTarjetaRondin(rondin));
                }

                PanelContenido.IsVisible = true;
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error de conexión: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // TARJETA DE RONDÍN (vista supervisor)
        // ─────────────────────────────────────────────────────────────────

        private View CrearTarjetaRondin(Rondin rondin)
        {
            var estadoColor = Color.FromArgb(rondin.EstadoColor);
            var estadoColorFondo = Color.FromArgb(rondin.EstadoColorFondo);

            bool tieneIncidencias = rondin.TieneIncidencias;

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1C1C1C"),
                StrokeThickness = 1,
                Stroke = tieneIncidencias ? Color.FromArgb("#A32D2D") : Color.FromArgb("#2E2E2E"),
                Padding = new Thickness(0),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(12) };

            // ── TAP para abrir el desglose del rondín ────────────────────────────
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                // Feedback visual breve
                await card.FadeToAsync(0.6, 80);
                await card.FadeToAsync(1.0, 80);

                // Navegar a la página de detalle
                await Navigation.PushModalAsync(
                    new RondinDetalleSupervisorPage(_db, _offline, rondin.ID));
            };
            card.GestureRecognizers.Add(tap);
            // ─────────────────────────────────────────────────────────────────────

            var innerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = new GridLength(4) },
            new ColumnDefinition { Width = GridLength.Star },
        }
            };

            // Barra lateral
            var barraColor = tieneIncidencias ? Color.FromArgb("#A32D2D") : estadoColor;
            var barra = new BoxView { Color = barraColor, VerticalOptions = LayoutOptions.Fill };
            Grid.SetColumn(barra, 0);

            // Contenido
            var contenido = new Grid
            {
                Padding = new Thickness(14, 12),
                RowDefinitions = new RowDefinitionCollection
        {
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto }, // Para incidencias
        },
                ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = GridLength.Star },
            new ColumnDefinition { Width = GridLength.Auto },
        }
            };
            Grid.SetColumn(contenido, 1);

            // Hora
            var lblHora = new Label
            {
                Text = $"{rondin.HoraProgramadaStr} hrs",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetRow(lblHora, 0); Grid.SetColumn(lblHora, 0);

            // Badge estado
            var badge = new Border
            {
                BackgroundColor = estadoColorFondo,
                StrokeThickness = 1,
                Stroke = estadoColor,
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center,
            };
            badge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };
            badge.Content = new Label
            {
                Text = rondin.EstadoTexto,
                TextColor = estadoColor,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetRow(badge, 0); Grid.SetColumn(badge, 1);

            // Info: puntos y duración
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
            var progress = new ProgressBar
            {
                Progress = rondin.PuntosTotal > 0
                    ? (double)rondin.PuntosVisitados / rondin.PuntosTotal
                    : 0,
                ProgressColor = estadoColor,
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                HeightRequest = 4,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(progress, 2); Grid.SetColumn(progress, 0);
            Grid.SetColumnSpan(progress, 2);

            contenido.Children.Add(lblHora);
            contenido.Children.Add(badge);
            contenido.Children.Add(lblInfo);
            contenido.Children.Add(progress);

            // ── INCIDENCIAS  ──
            if (tieneIncidencias)
            {
                // Badge de incidencias
                var incidenciaRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
                    ColumnSpacing = 6,
                    Margin = new Thickness(0, 8, 0, 0),
                };

                incidenciaRow.Children.Add(new Image
                {
                    Source = "warning.png",
                    WidthRequest = 12,
                    HeightRequest = 12,
                    VerticalOptions = LayoutOptions.Center,
                });
                Grid.SetColumn(incidenciaRow.Children.Last() as View, 0);

                incidenciaRow.Children.Add(new Label
                {
                    Text = "Hay incidencias reportadas en este rondín",
                    TextColor = Color.FromArgb("#F09595"),
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                });
                Grid.SetColumn(incidenciaRow.Children.Last() as View, 1);

                Grid.SetRow(incidenciaRow, 3);
                Grid.SetColumnSpan(incidenciaRow, 2);
                contenido.Children.Add(incidenciaRow);
            }

            // Indicador de que es tappable (flecha discreta)
            var lblChevron = new Label
            {
                Text = "›",
                TextColor = Color.FromArgb("#444444"),
                FontSize = 20,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };

            var wrapperGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = GridLength.Star },
            new ColumnDefinition { Width = new GridLength(20) },
        }
            };
            Grid.SetColumn(contenido, 0);
            wrapperGrid.Children.Add(contenido);
            Grid.SetColumn(lblChevron, 1);
            wrapperGrid.Children.Add(lblChevron);
            Grid.SetColumn(wrapperGrid, 1);

            innerGrid.Children.Add(barra);
            innerGrid.Children.Add(wrapperGrid);
            card.Content = innerGrid;

            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // EVENTOS
        // ─────────────────────────────────────────────────────────────────

        private async void OnRefrescarClicked(object sender, EventArgs e)
        {
            await CargarDatosAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message)
        {
            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = Color.FromArgb("#FF5555");
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;

            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}