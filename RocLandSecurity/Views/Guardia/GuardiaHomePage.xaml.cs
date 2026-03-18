using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Guardia
{
    public partial class GuardiaHomePage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService _session;
        private Turno? _turnoActivo;
        private List<Rondin> _rondines = new();

        public GuardiaHomePage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db = db;
            _session = session;
        }

        // ─────────────────────────────────────────────────────────────────
        // CICLO DE VIDA — recarga al volver a la pantalla
        // ─────────────────────────────────────────────────────────────────

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarDatosAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA DE DATOS
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarDatosAsync()
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            // Header
            LblNombre.Text = usuario.Nombre;
            LblFechaTurno.Text = DateTime.Now.ToString("dddd dd 'de' MMMM",
                new System.Globalization.CultureInfo("es-MX"));

            // Estado de carga
            LoadingIndicator.IsVisible = true;
            PanelSinTurno.IsVisible = false;
            PanelRondines.IsVisible = false;

            try
            {
                _turnoActivo = await _db.GetTurnoActivoAsync(usuario.ID);

                if (_turnoActivo == null)
                {
                    // No hay turno hoy
                    MostrarSinTurno();
                }
                else
                {
                    // Hay turno — cargar rondines
                    _rondines = await _db.GetRondinesPorTurnoAsync(_turnoActivo.ID);
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
        // RENDERIZAR ESTADO
        // ─────────────────────────────────────────────────────────────────

        private void MostrarSinTurno()
        {
            BadgeTurno.BackgroundColor = Color.FromArgb("#1a0a0a");
            BadgeTurno.Stroke = Color.FromArgb("#F09595");
            LblBadgeTurno.Text = "Sin turno";
            LblBadgeTurno.TextColor = Color.FromArgb("#F09595");

            PanelSinTurno.IsVisible = true;
            PanelRondines.IsVisible = false;
        }

        private void MostrarRondines()
        {
            BadgeTurno.BackgroundColor = Color.FromArgb("#0a1a0a");
            BadgeTurno.Stroke = Color.FromArgb("#97C459");
            LblBadgeTurno.Text = "Turno activo";
            LblBadgeTurno.TextColor = Color.FromArgb("#97C459");

            // Conteo
            int completados = _rondines.Count(r => r.Estado == 2 || r.Estado == 4);
            LblConteoRondines.Text = $"{completados}/{_rondines.Count} completados";

            // Construir tarjetas de rondines
            ListaRondines.Children.Clear();
            foreach (var rondin in _rondines)
                ListaRondines.Children.Add(CrearTarjetaRondin(rondin));

            PanelSinTurno.IsVisible = false;
            PanelRondines.IsVisible = true;
        }

        // ─────────────────────────────────────────────────────────────────
        // TARJETA DE RONDÍN
        // ─────────────────────────────────────────────────────────────────

        private View CrearTarjetaRondin(Rondin rondin)
        {
            var estadoColor = Color.FromArgb(rondin.EstadoColor);
            var estadoColorFondo = Color.FromArgb(rondin.EstadoColorFondo);

            // Contenedor principal de la tarjeta
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1C1C1C"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2E2E2E"),
                Padding = new Thickness(0),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(12) };

            // Barra de color izquierda + contenido
            var innerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = GridLength.Star },
                }
            };

            // Barra lateral de color según estado
            var barra = new BoxView
            {
                Color = estadoColor,
                WidthRequest = 4,
                VerticalOptions = LayoutOptions.Fill,
            };
            Grid.SetColumn(barra, 0);

            // Contenido de la tarjeta
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

            // Hora programada
            var lblHora = new Label
            {
                Text = $"Rondín {rondin.HoraProgramadaStr} hrs",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetRow(lblHora, 0);
            Grid.SetColumn(lblHora, 0);

            // Badge de estado
            var badgeBorder = new Border
            {
                BackgroundColor = estadoColorFondo,
                StrokeThickness = 1,
                Stroke = estadoColor,
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center,
            };
            badgeBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };
            badgeBorder.Content = new Label
            {
                Text = rondin.EstadoTexto,
                TextColor = estadoColor,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetRow(badgeBorder, 0);
            Grid.SetColumn(badgeBorder, 1);

            // Info secundaria: puntos y duración
            var lblInfo = new Label
            {
                Text = rondin.EstaFinalizado
                    ? $"{rondin.PuntosStr}  ·  {rondin.DuracionStr}"
                    : rondin.PuntosStr,
                TextColor = Color.FromArgb("#888888"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            };
            Grid.SetRow(lblInfo, 1);
            Grid.SetColumn(lblInfo, 0);
            Grid.SetColumnSpan(lblInfo, 2);

            // Barra de progreso de puntos
            var barraProgreso = new Grid
            {
                HeightRequest = 4,
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                Margin = new Thickness(0, 8, 0, 0),
            };
            barraProgreso.StrokeShape();  // extensión no existe, usamos Border directamente

            var progresoFill = new BoxView
            {
                Color = estadoColor,
                HeightRequest = 4,
                HorizontalOptions = LayoutOptions.Start,
                WidthRequest = 0, // se ajusta abajo
            };

            // Calcular ancho proporcional en code (aprox)
            double pct = rondin.PuntosTotal > 0
                ? (double)rondin.PuntosVisitados / rondin.PuntosTotal
                : 0;

            var progressContainer = new Border
            {
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                HeightRequest = 4,
                StrokeThickness = 0,
                Margin = new Thickness(0, 8, 0, 0),
            };
            progressContainer.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(2) };

            // Usamos ProgressBar nativo de MAUI — más simple y correcto
            var progress = new ProgressBar
            {
                Progress = pct,
                ProgressColor = estadoColor,
                BackgroundColor = Color.FromArgb("#2E2E2E"),
                HeightRequest = 4,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(progress, 2);
            Grid.SetColumn(progress, 0);
            Grid.SetColumnSpan(progress, 2);

            contenido.Children.Add(lblHora);
            contenido.Children.Add(badgeBorder);
            contenido.Children.Add(lblInfo);
            contenido.Children.Add(progress);

            // Botón iniciar (solo si el rondín es el pendiente actual)
            if (rondin.EsInicialbe)
            {
                var btnIniciar = new Button
                {
                    Text = "▶  Iniciar rondín",
                    BackgroundColor = Color.FromArgb("#6DBF2E"),
                    TextColor = Color.FromArgb("#111111"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 14,
                    CornerRadius = 10,
                    HeightRequest = 46,
                    Margin = new Thickness(0, 12, 0, 0),
                };
                btnIniciar.Clicked += (s, e) => OnIniciarRondinClicked(rondin);

                var rowBoton = new RowDefinition { Height = GridLength.Auto };
                contenido.RowDefinitions.Add(rowBoton);
                Grid.SetRow(btnIniciar, 3);
                Grid.SetColumn(btnIniciar, 0);
                Grid.SetColumnSpan(btnIniciar, 2);
                contenido.Children.Add(btnIniciar);
            }

            innerGrid.Children.Add(barra);
            innerGrid.Children.Add(contenido);
            card.Content = innerGrid;

            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // EVENTOS
        // ─────────────────────────────────────────────────────────────────

        private async void OnIniciarTurnoClicked(object sender, EventArgs e)
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            BtnIniciarTurno.IsEnabled = false;
            BtnIniciarTurno.Text = "Creando turno...";

            try
            {
                _turnoActivo = await _db.CrearTurnoYRondinesAsync(usuario.ID);
                await ShowToastAsync("Turno iniciado correctamente", false);
                await CargarDatosAsync();
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error al crear turno: {ex.Message}", true);
                BtnIniciarTurno.IsEnabled = true;
                BtnIniciarTurno.Text = "Iniciar turno de hoy";
            }
        }

        private async void OnIniciarRondinClicked(Rondin rondin)
        {
            // Sprint 3: navegar a RondinActivoPage pasando el ID del rondín
            await ShowToastAsync("Rondín activo — próximamente", false);
            // await Shell.Current.GoToAsync($"rondinactivo?rondinId={rondin.ID}");
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
            await Task.Delay(2000);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }

    // Extensión vacía para que compile (no existe StrokeShape() en Grid)
    internal static class GridExtensions
    {
        internal static void StrokeShape(this Grid _) { }
    }
}