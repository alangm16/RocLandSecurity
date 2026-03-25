using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class RondinDetalleSupervisorPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly int _rondinID;

        public RondinDetalleSupervisorPage(DatabaseService db, int rondinID)
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
        private View CrearFilaIncidencia(IncidenciaResumen inc)
        {
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#2A1A1A"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#A32D2D"),
                Padding = new Thickness(12, 10),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };

            var stack = new VerticalStackLayout { Spacing = 4 };

            stack.Children.Add(new Label
            {
                Text = inc.Descripcion,
                TextColor = Color.FromArgb("#F09595"),
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap
            });

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

                // Retraso al inicio (diferencia entre programado e inicio real)
                if (detalle.HoraInicio.HasValue)
                {
                    var retraso = detalle.HoraInicio.Value - detalle.HoraProgramada;
                    if (retraso.TotalMinutes > 1)
                        LblHoraInicio.Text += $"\n(+{(int)retraso.TotalMinutes}min tarde)";
                    else if (retraso.TotalMinutes < -1)
                        LblHoraInicio.Text += $"\n({(int)Math.Abs(retraso.TotalMinutes)}min antes)";
                }

                LblDuracion.Text = detalle.DuracionStr;

                // Badge estado
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

                // ── Sección de incidencias (después de la lista de puntos) ──
                if (detalle.Incidencias.Any())
                {
                    var incidenciasHeader = new Label
                    {
                        Text = "Incidencias reportadas",
                        TextColor = Color.FromArgb("#F09595"),
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        Margin = new Thickness(0, 12, 0, 8)
                    };
                    ListaPuntos.Children.Add(incidenciasHeader);

                    foreach (var inc in detalle.Incidencias)
                    {
                        ListaPuntos.Children.Add(CrearFilaIncidencia(inc));
                    }
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

        private View CrearFilaPunto(PuntoDetalleItem punto)
        {
            var colorPunto = Color.FromArgb(punto.EstadoColor);

            // Contenedor principal
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#181818"),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#2A2A2A"),
                Padding = new Thickness(0),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(3) },   // barra lateral
                    new ColumnDefinition { Width = new GridLength(36) },  // número/icono
                    new ColumnDefinition { Width = GridLength.Star },     // nombre
                    new ColumnDefinition { Width = GridLength.Auto },     // hora + intervalo
                },
                Padding = new Thickness(0, 10),
            };

            // Barra lateral de color
            var barra = new BoxView
            {
                Color = colorPunto,
                VerticalOptions = LayoutOptions.Fill,
            };
            Grid.SetColumn(barra, 0);

            // Número de orden + icono de estado
            var stackOrden = new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Spacing = 1,
                Padding = new Thickness(0, 0, 2, 0),
            };
            stackOrden.Children.Add(new Label
            {
                Text = punto.EstadoIcon,
                TextColor = colorPunto,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
            });
            stackOrden.Children.Add(new Label
            {
                Text = punto.Orden.ToString(),
                TextColor = Color.FromArgb("#555555"),
                FontSize = 9,
                HorizontalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(stackOrden, 1);

            // Nombre del punto
            var lblNombre = new Label
            {
                Text = punto.Nombre,
                TextColor = punto.Estado == 0
                    ? Color.FromArgb("#555555")   // Pendiente: gris oscuro
                    : Colors.White,
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(6, 0, 8, 0),
            };
            Grid.SetColumn(lblNombre, 2);

            // Columna derecha: hora y tiempo entre QRs
            var stackHora = new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Spacing = 2,
                Padding = new Thickness(0, 0, 12, 0),
            };

            var lblHora = new Label
            {
                Text = punto.HoraStr,
                TextColor = punto.Estado == 1 ? Colors.White : Color.FromArgb("#555555"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
            };
            stackHora.Children.Add(lblHora);

            // Tiempo entre QRs consecutivos (solo si fue visitado y hay intervalo)
            if (!string.IsNullOrEmpty(punto.IntervaloStr))
            {
                // Color basado en tiempo: verde < 3 min, amarillo 3-8 min, rojo > 8 min
                Color intervaloColor;
                if (punto.Intervalo!.Value.TotalMinutes < 3)
                    intervaloColor = Color.FromArgb("#97C459");
                else if (punto.Intervalo.Value.TotalMinutes < 8)
                    intervaloColor = Color.FromArgb("#FAC775");
                else
                    intervaloColor = Color.FromArgb("#F09595");

                stackHora.Children.Add(new Label
                {
                    Text = punto.IntervaloStr,
                    TextColor = intervaloColor,
                    FontSize = 10,
                    HorizontalOptions = LayoutOptions.End,
                });
            }

            Grid.SetColumn(stackHora, 3);

            row.Children.Add(barra);
            row.Children.Add(stackOrden);
            row.Children.Add(lblNombre);
            row.Children.Add(stackHora);

            card.Content = row;
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
            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}
