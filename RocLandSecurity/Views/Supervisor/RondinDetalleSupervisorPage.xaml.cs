using Microsoft.Maui.Controls.Shapes;
using RocLandSecurity.Models;
using RocLandSecurity.Services;

// NOTA: ya no se importa RocLandSecurity.Views.Guardia — el supervisor
// no navega a ninguna página del guardia.

namespace RocLandSecurity.Views.Supervisor
{
    public partial class RondinDetalleSupervisorPage : ContentPage
    {
        private readonly SupervisorDatabaseService _db;
        private readonly int _rondinID;

        // OfflineDatabaseService eliminado: el supervisor opera siempre
        // con conexión directa a SQL Server. No usa SQLite local.
        public RondinDetalleSupervisorPage(SupervisorDatabaseService db, int rondinID)
        {
            InitializeComponent();
            _db = db;
            _rondinID = rondinID;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarDetalleAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarDetalleAsync()
        {
            LoadingIndicator.IsVisible = true;
            PanelContenido.IsVisible = false;

            try
            {
                var detalle = await _db.GetDetalleRondinSupervisorAsync(_rondinID);

                if (detalle == null)
                {
                    await ShowToastAsync("No se encontró el rondín.");
                    return;
                }

                // ── Título en header
                LblTitulo.Text = $"Rondín {detalle.HoraProgramada:HH:mm} hrs";

                // ── Tarjeta resumen
                LblNombreGuardia.Text = detalle.NombreGuardia;
                LblHoraProgramada.Text = detalle.HoraProgramada.ToString("HH:mm");

                LblHoraInicio.Text = detalle.HoraInicio.HasValue
                    ? detalle.HoraInicio.Value.ToString("HH:mm")
                    : "--:--";

                if (detalle.HoraInicio.HasValue)
                {
                    var retraso = detalle.HoraInicio.Value - detalle.HoraProgramada;
                    if (retraso.TotalMinutes > 1)
                        LblHoraInicio.Text += $"\n(+{(int)retraso.TotalMinutes}min tarde)";
                    else if (retraso.TotalMinutes < -1)
                        LblHoraInicio.Text += $"\n({(int)Math.Abs(retraso.TotalMinutes)}min antes)";
                }

                LblDuracion.Text = detalle.DuracionStr;

                var estadoColor = Color.FromArgb(detalle.EstadoColor);
                LblEstado.Text = detalle.EstadoTexto;
                LblEstado.TextColor = estadoColor;
                BadgeEstado.Stroke = estadoColor;

                LblPuntosResumen.Text = $"{detalle.PuntosVisitados} / {detalle.PuntosTotal} puntos visitados";

                if (detalle.TotalIncidencias > 0)
                {
                    BadgeIncidencias.IsVisible = true;
                    LblIncidencias.Text = $"⚠ {detalle.TotalIncidencias} incidencia{(detalle.TotalIncidencias > 1 ? "s" : "")}";
                }

                // ── Lista de puntos
                ListaPuntos.Children.Clear();
                foreach (var punto in detalle.Puntos)
                    ListaPuntos.Children.Add(CrearFilaPunto(punto));

                // ── Sección de incidencias
                if (detalle.Incidencias.Any())
                {
                    ListaPuntos.Children.Add(new Label
                    {
                        Text = "Incidencias reportadas",
                        TextColor = Color.FromArgb("#F09595"),
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        Margin = new Thickness(0, 12, 0, 8)
                    });

                    foreach (var inc in detalle.Incidencias)
                        ListaPuntos.Children.Add(CrearFilaIncidencia(inc));
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

        // ─────────────────────────────────────────────────────────────────
        // FILA DE PUNTO DE CONTROL
        // ─────────────────────────────────────────────────────────────────

        private static readonly HashSet<int> _ordenesConFoto = new() { 1, 11, 19 };

        private View CrearFilaPunto(PuntoDetalleItem punto)
        {
            var colorPunto = Color.FromArgb(punto.EstadoColor);

            // FIX: el botón se muestra si el punto fue VISITADO y es uno de los
            // puntos que requieren foto. No depende de que FotoBytes esté cargado
            // en memoria, porque los bytes se piden al servidor al tocar el botón.
            bool mostrarBotonFoto =
                punto.Estado == 1 &&
                _ordenesConFoto.Contains(punto.Orden);

            var card = CrearCard();

            var grid = CrearGridBase(mostrarBotonFoto);

            AgregarBarraColor(grid, colorPunto);
            AgregarOrdenEstado(grid, punto, colorPunto);
            AgregarNombre(grid, punto);

            if (mostrarBotonFoto)
                AgregarBotonFoto(grid, punto);   // Col 3

            AgregarHoraEIntervalo(grid, punto, mostrarBotonFoto); // Col 3 ó 4

            card.Content = grid;
            return card;
        }

        private static Border CrearCard()
        {
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#181818"),
                Stroke = Color.FromArgb("#2A2A2A"),
                StrokeThickness = 1,
                Padding = 0
            };

            card.StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(10)
            };

            return card;
        }

        private static Grid CrearGridBase(bool mostrarBotonFoto)
        {
            var grid = new Grid
            {
                Padding = new Thickness(0, 10)
            };

            // Col 0: barra color (3px)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            // Col 1: orden/icono (36px)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            // Col 2: nombre (*)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            if (mostrarBotonFoto)
            {
                // Col 3: botón foto (Auto)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                // Col 4: hora/intervalo (Auto)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            else
            {
                // Col 3: hora/intervalo (Auto)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            return grid;
        }

        private static void AgregarBarraColor(Grid grid, Color color)
        {
            var barra = new BoxView
            {
                Color = color,
                VerticalOptions = LayoutOptions.Fill
            };
            Grid.SetColumn(barra, 0);
            grid.Children.Add(barra);
        }

        private static void AgregarOrdenEstado(Grid grid, PuntoDetalleItem punto, Color color)
        {
            var stack = new VerticalStackLayout
            {
                Spacing = 1,
                Padding = new Thickness(0, 0, 2, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            stack.Children.Add(new Label
            {
                Text = punto.EstadoIcon,
                TextColor = color,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center
            });

            stack.Children.Add(new Label
            {
                Text = punto.Orden.ToString(),
                TextColor = Color.FromArgb("#555555"),
                FontSize = 9,
                HorizontalOptions = LayoutOptions.Center
            });

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);
        }

        private static void AgregarNombre(Grid grid, PuntoDetalleItem punto)
        {
            var label = new Label
            {
                Text = punto.Nombre,
                FontSize = 13,
                Padding = new Thickness(6, 0, 8, 0),
                VerticalOptions = LayoutOptions.Center,
                TextColor = punto.Estado == 0
                    ? Color.FromArgb("#555555")
                    : Colors.White
            };
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
        }

        private static void AgregarHoraEIntervalo(Grid grid, PuntoDetalleItem punto, bool mostrarBotonFoto)
        {
            var stack = new VerticalStackLayout
            {
                Spacing = 2,
                Padding = new Thickness(0, 0, 12, 0),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };

            stack.Children.Add(new Label
            {
                Text = punto.HoraStr,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                TextColor = punto.Estado == 1
                    ? Colors.White
                    : Color.FromArgb("#555555")
            });

            if (!string.IsNullOrEmpty(punto.IntervaloStr) && punto.Intervalo.HasValue)
            {
                stack.Children.Add(new Label
                {
                    Text = punto.IntervaloStr,
                    FontSize = 10,
                    HorizontalOptions = LayoutOptions.End,
                    TextColor = ObtenerColorIntervalo(punto.Intervalo.Value)
                });
            }

            // Si hay botón foto ocupa col 3, la hora va en col 4; si no, hora en col 3
            Grid.SetColumn(stack, mostrarBotonFoto ? 4 : 3);
            grid.Children.Add(stack);
        }

        private static Color ObtenerColorIntervalo(TimeSpan intervalo)
        {
            var minutos = intervalo.TotalMinutes;
            if (minutos < 3)  return Color.FromArgb("#97C459");
            if (minutos < 8)  return Color.FromArgb("#FAC775");
            return Color.FromArgb("#F09595");
        }

        private void AgregarBotonFoto(Grid grid, PuntoDetalleItem punto)
        {
            var boton = new Border
            {
                BackgroundColor = Color.FromArgb("#252525"),
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeThickness = 0,
                Margin = new Thickness(4, 0, 8, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            boton.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) };
            boton.Content = new Image
            {
                Source = "camera.png",
                WidthRequest = 18,
                HeightRequest = 18,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                // FIX: navega a FotoEvidenciaSupervisorPage (solo SQL Server).
                // No usa OfflineDatabaseService ni SQLite en ningún momento.
                var fotoPage = new FotoEvidenciaSupervisorPage(_db, punto.RondinPuntoID);
                await Navigation.PushModalAsync(fotoPage);
            };
            boton.GestureRecognizers.Add(tap);

            Grid.SetColumn(boton, 3);
            grid.Children.Add(boton);
        }

        // ─────────────────────────────────────────────────────────────────
        // FILA DE INCIDENCIA
        // ─────────────────────────────────────────────────────────────────

        private View CrearFilaIncidencia(IncidenciaResumen inc)
        {
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#2A1A1A"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#A32D2D"),
                Padding = new Thickness(12, 10),
            };
            card.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) };

            var stack = new VerticalStackLayout { Spacing = 6 };

            stack.Children.Add(new Label
            {
                Text = inc.Descripcion,
                TextColor = Color.FromArgb("#F09595"),
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap
            });

            if (!string.IsNullOrEmpty(inc.NombrePunto))
            {
                var ubicacionGrid = new Grid
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
                    Text = inc.NombrePunto,
                    TextColor = Color.FromArgb("#CCCCCC"),
                    FontSize = 11,
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(lblUbicacion, 1);

                ubicacionGrid.Children.Add(locationIcon);
                ubicacionGrid.Children.Add(lblUbicacion);
                stack.Children.Add(ubicacionGrid);
            }

            var estadoColor = inc.Estado == 0
                ? Color.FromArgb("#F09595")
                : Color.FromArgb("#97C459");
            var estadoText = inc.Estado == 0 ? "⚠ Abierta" : "✓ Resuelta";

            stack.Children.Add(new Label
            {
                Text = $"{inc.FechaReporte:HH:mm:ss} · {estadoText}",
                TextColor = estadoColor,
                FontSize = 11
            });

            if (!string.IsNullOrEmpty(inc.NotaResolucion))
            {
                stack.Children.Add(new Label
                {
                    Text = $"Resolución: {inc.NotaResolucion}",
                    TextColor = Color.FromArgb("#97C459"),
                    FontSize = 11
                });
            }

            card.Content = stack;
            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // EVENTOS
        // ─────────────────────────────────────────────────────────────────

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message)
        {
            ToastLabel.Text = message;
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;
            await ToastFrame.FadeToAsync(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeToAsync(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}
