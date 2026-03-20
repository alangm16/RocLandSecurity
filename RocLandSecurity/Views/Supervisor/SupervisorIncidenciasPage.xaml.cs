using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class SupervisorIncidenciasPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService _session;

        private List<IncidenciaSupervisorItem> _todasIncidencias = new();

        public SupervisorIncidenciasPage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db = db;
            _session = session;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarIncidenciasAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA DE INCIDENCIAS
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarIncidenciasAsync()
        {
            LoadingIndicator.IsVisible = true;
            PanelContenido.IsVisible = false;
            ListaIncidencias.Children.Clear();

            try
            {
                _todasIncidencias = await _db.GetIncidenciasSemanaAsync();

                if (_todasIncidencias.Count == 0)
                {
                    PanelSinIncidencias.IsVisible = true;
                    PanelContenido.IsVisible = true;
                    return;
                }

                PanelSinIncidencias.IsVisible = false;

                // Agrupar por día
                var agrupadas = _todasIncidencias
                    .GroupBy(i => i.FechaReporte.Date)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new IncidenciasPorDia
                    {
                        Fecha = g.Key,
                        DiaSemana = ObtenerDiaSemana(g.Key),
                        Incidencias = g.OrderByDescending(i => i.FechaReporte).ToList()
                    })
                    .ToList();

                // Construir cada grupo
                foreach (var grupo in agrupadas)
                {
                    ListaIncidencias.Children.Add(CrearEncabezadoDia(grupo));

                    foreach (var inc in grupo.Incidencias)
                        ListaIncidencias.Children.Add(CrearTarjetaIncidencia(inc));
                }

                PanelContenido.IsVisible = true;
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
            }
        }

        private string ObtenerDiaSemana(DateTime fecha)
        {
            return fecha.ToString("dddd", new System.Globalization.CultureInfo("es-MX"));
        }

        // ─────────────────────────────────────────────────────────────────
        // CONSTRUCCIÓN DE VISTAS
        // ─────────────────────────────────────────────────────────────────

        private View CrearEncabezadoDia(IncidenciasPorDia grupo)
        {
            var header = new Border
            {
                BackgroundColor = Color.FromArgb("#1A1A1A"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2E2E2E"),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
            header.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(8) };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var lblDia = new Label
            {
                Text = grupo.TituloDia,
                TextColor = Color.FromArgb("#97C459"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblDia, 0);

            var lblTotal = new Label
            {
                Text = $"{grupo.Total} incidencia{(grupo.Total == 1 ? "" : "s")}",
                TextColor = Color.FromArgb("#888888"),
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblTotal, 1);

            grid.Children.Add(lblDia);
            grid.Children.Add(lblTotal);
            header.Content = grid;

            return header;
        }

        private View CrearTarjetaIncidencia(IncidenciaSupervisorItem inc)
        {
            var estadoColor = Color.FromArgb(inc.EstadoColor);
            var estadoBg = Color.FromArgb(inc.EstadoColorFondo);

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1C1C1C"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2E2E2E"),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(12) };

            var innerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            // Barra lateral de estado
            var barra = new BoxView { Color = estadoColor, VerticalOptions = LayoutOptions.Fill };
            Grid.SetColumn(barra, 0);

            // Contenido principal
            var contenido = new VerticalStackLayout
            {
                Padding = new Thickness(14, 12),
                Spacing = 8
            };
            Grid.SetColumn(contenido, 1);

            // Fila 1: Hora y badge estado
            var fila1 = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };

            var lblHora = new Label
            {
                Text = inc.HoraStr,
                TextColor = Color.FromArgb("#97C459"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblHora, 0);

            var lblGuardia = new Label
            {
                Text = inc.NombreGuardia,
                TextColor = Color.FromArgb("#888888"),
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            Grid.SetColumn(lblGuardia, 1);

            var badge = new Border
            {
                BackgroundColor = estadoBg,
                StrokeThickness = 1,
                Stroke = estadoColor,
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center
            };
            badge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };
            badge.Content = new Label
            {
                Text = inc.EstadoTexto,
                TextColor = estadoColor,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold
            };
            Grid.SetColumn(badge, 2);

            fila1.Children.Add(lblHora);
            fila1.Children.Add(lblGuardia);
            fila1.Children.Add(badge);

            // Fila 2: Ubicación
            var fila2 = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6
            };

            var locationIcon = new Image
            {
                Source = "location.png",
                WidthRequest = 12,
                HeightRequest = 12,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(locationIcon, 0);

            var lblUbicacion = new Label
            {
                Text = inc.UbicacionStr,
                TextColor = Color.FromArgb("#FFFFFF"),
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(lblUbicacion, 1);

            fila2.Children.Add(locationIcon);
            fila2.Children.Add(lblUbicacion);

            // Fila 3: Descripción
            var lblDescripcion = new Label
            {
                Text = inc.Descripcion,
                TextColor = Color.FromArgb("#FFFFFF"),
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap
            };

            contenido.Children.Add(fila1);
            contenido.Children.Add(fila2);
            contenido.Children.Add(lblDescripcion);

            // Botón Resolver (solo si está abierta)
            if (inc.EsAbierta)
            {
                var btnResolver = new Button
                {
                    Text = "Marcar como resuelta",
                    BackgroundColor = Color.FromArgb("#97C459"),
                    TextColor = Color.FromArgb("#111111"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 36,
                    Margin = new Thickness(0, 6, 0, 0),
                    CommandParameter = inc.ID
                };
                btnResolver.Clicked += OnResolverClicked;
                contenido.Children.Add(btnResolver);
            }
            else if (!string.IsNullOrEmpty(inc.NotaResolucion))
            {
                // Mostrar nota de resolución si existe
                var notaFrame = new Border
                {
                    BackgroundColor = Color.FromArgb("#0a1a0a"),
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#97C459"),
                    Padding = new Thickness(10, 6),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                notaFrame.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };

                notaFrame.Content = new Label
                {
                    Text = $"✓ Resuelta: {inc.NotaResolucion}",
                    TextColor = Color.FromArgb("#97C459"),
                    FontSize = 11,
                    LineBreakMode = LineBreakMode.WordWrap
                };
                contenido.Children.Add(notaFrame);
            }

            innerGrid.Children.Add(barra);
            innerGrid.Children.Add(contenido);
            card.Content = innerGrid;

            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // RESOLVER INCIDENCIA
        // ─────────────────────────────────────────────────────────────────

        private async void OnResolverClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn?.CommandParameter is not int incidenciaID)
                return;

            btn.IsEnabled = false;

            string nota = await DisplayPromptAsync(
                "Resolver incidencia",
                "Ingresa una nota sobre la resolución:",
                "Aceptar",
                "Cancelar",
                placeholder: "Ej: Se corrigió el problema...");

            if (string.IsNullOrWhiteSpace(nota))
            {
                btn.IsEnabled = true;
                return;
            }

            try
            {
                var supervisor = _session.UsuarioActual!;
                await _db.ResolverIncidenciaAsync(incidenciaID, supervisor.ID, nota);
                await ShowToastAsync("Incidencia resuelta correctamente", false);
                await CargarIncidenciasAsync();
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error: {ex.Message}");
                btn.IsEnabled = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // EVENTOS
        // ─────────────────────────────────────────────────────────────────

        private async void OnRefrescarClicked(object sender, EventArgs e)
        {
            await CargarIncidenciasAsync();
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

            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}
