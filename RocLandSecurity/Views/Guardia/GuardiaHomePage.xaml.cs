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

            LblNombre.Text = usuario.Nombre;
            LblFechaTurno.Text = DateTime.Now.ToString(
                "dddd dd 'de' MMMM",
                new System.Globalization.CultureInfo("es-MX"));

            LoadingIndicator.IsVisible = true;
            PanelSinTurno.IsVisible = false;
            PanelRondines.IsVisible = false;

            try
            {
                _turnoActivo = await _db.GetTurnoActivoAsync(usuario.ID);

                if (_turnoActivo == null)
                    MostrarSinTurno();
                else
                {
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
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1A1A1A"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2A4A2A"),
                Padding = new Thickness(16, 14),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(14) };

            var grid = new Grid
            {
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
            Grid.SetColumn(iconBorder, 0);

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
            Grid.SetColumn(info, 1);

            // Flecha
            var arrow = new Label
            {
                Text = "›",
                TextColor = Color.FromArgb("#888888"),
                FontSize = 22,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(arrow, 2);

            grid.Children.Add(iconBorder);
            grid.Children.Add(info);
            grid.Children.Add(arrow);
            card.Content = grid;

            // Tap para retomar
            card.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await IrARondinActivoAsync(rondin))
            });

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

            contenido.Children.Add(lblHora);
            contenido.Children.Add(badge);
            contenido.Children.Add(lblInfo);
            contenido.Children.Add(progress);

            // Botón iniciar — solo en el primer rondín pendiente
            if (rondin.EsIniciable && EsPrimerPendiente(rondin))
            {
                var btn = new Button
                {
                    Text = "Iniciar rondín",
                    BackgroundColor = Color.FromArgb("#6DBF2E"),
                    TextColor = Color.FromArgb("#111111"),
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
            // Rondín en progreso — botón para retomar
            else if (rondin.EstaEnProgreso)
            {
                var btn = new Button
                {
                    Text = "▶  Continuar rondín",
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