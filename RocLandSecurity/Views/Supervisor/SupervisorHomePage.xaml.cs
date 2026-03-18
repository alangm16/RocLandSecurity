using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class SupervisorHomePage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService _session;

        public SupervisorHomePage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db = db;
            _session = session;
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
                // Métricas y rondines en paralelo
                var metricasTask = _db.GetMetricasTurnoActivoAsync();
                var rondinesTask = _db.GetRondinesTurnoActivoAsync();

                await Task.WhenAll(metricasTask, rondinesTask);

                var (completados, enProgreso, pendientes, incidencias) = metricasTask.Result;
                var rondines = rondinesTask.Result;

                // Actualizar tarjetas métricas
                LblCompletados.Text = completados.ToString();
                LblEnProgreso.Text = enProgreso.ToString();
                LblPendientes.Text = pendientes.ToString();
                LblIncidencias.Text = incidencias.ToString();
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
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = GridLength.Star },
                }
            };

            // Barra lateral
            var barra = new BoxView { Color = estadoColor, VerticalOptions = LayoutOptions.Fill };
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
                Text = $"Rondín {rondin.HoraProgramadaStr} hrs",
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

            // Si tiene incidencia, mostrar aviso
            if (rondin.Estado == 4)
            {
                var lblIncidencia = new Label
                {
                    Text = "⚠  Hay incidencias reportadas en este rondín",
                    TextColor = Color.FromArgb("#FAC775"),
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                contenido.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(lblIncidencia, 3);
                Grid.SetColumn(lblIncidencia, 0);
                Grid.SetColumnSpan(lblIncidencia, 2);
                contenido.Children.Add(lblIncidencia);
            }

            innerGrid.Children.Add(barra);
            innerGrid.Children.Add(contenido);
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