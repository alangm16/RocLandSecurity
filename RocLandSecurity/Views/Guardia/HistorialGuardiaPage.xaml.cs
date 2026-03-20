using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Guardia
{
    public partial class HistorialGuardiaPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService  _session;

        public HistorialGuardiaPage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db      = db;
            _session = session;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarHistorialAsync();
        }

        private async Task CargarHistorialAsync()
        {
            var usuario = _session.UsuarioActual;
            if (usuario == null) return;

            LoadingIndicator.IsVisible = true;
            PanelVacio.IsVisible       = false;
            ListaHistorial.Children.Clear();

            try
            {
                var items = await _db.GetHistorialGuardiaAsync(usuario.ID);

                if (items.Count == 0)
                {
                    PanelVacio.IsVisible = true;
                    return;
                }

                foreach (var item in items)
                    ListaHistorial.Children.Add(CrearTarjetaHistorial(item));
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
        // TARJETA DE HISTORIAL
        // ─────────────────────────────────────────────────────────────────

        private View CrearTarjetaHistorial(RondinHistorialItem item)
        {
            bool tieneIncidencias = item.TotalIncidencias > 0;
            bool esSolo           = item.EsSoloIncidencias;

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1A1A1A"),
                StrokeThickness = 1,
                Stroke          = tieneIncidencias
                    ? Color.FromArgb("#3A1A1A")
                    : Color.FromArgb("#2A2A2A"),
                Padding = new Thickness(16, 14),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(14) };

            var stack = new VerticalStackLayout { Spacing = 8 };

            if (esSolo)
            {
                // Entrada sin rondín — solo muestra incidencias sueltas
                stack.Children.Add(new Label
                {
                    Text           = "Incidencias fuera de rondín",
                    TextColor      = Color.FromArgb("#F09595"),
                    FontSize       = 14,
                    FontAttributes = FontAttributes.Bold,
                });
                stack.Children.Add(new Label
                {
                    Text      = item.HoraProgramada.ToString("dd/MM/yyyy"),
                    TextColor = Color.FromArgb("#888888"),
                    FontSize  = 12,
                });
            }
            else
            {
                // ── Fila 1: icono + hora + duración ──────────────────────
                var fila1 = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                    }
                };

                // Icono: warning.png si hay incidencias, checkmark verde si no
                View icono;
                if (tieneIncidencias)
                {
                    icono = new Image
                    {
                        Source          = "warning.png",
                        WidthRequest    = 18,
                        HeightRequest   = 18,
                        VerticalOptions = LayoutOptions.Center,
                        Margin          = new Thickness(0, 0, 10, 0),
                    };
                }
                else
                {
                    icono = new Label
                    {
                        Text            = "✓",
                        TextColor       = Color.FromArgb("#6DBF2E"),
                        FontSize        = 16,
                        FontAttributes  = FontAttributes.Bold,
                        VerticalOptions = LayoutOptions.Center,
                        Margin          = new Thickness(0, 0, 10, 0),
                    };
                }
                Grid.SetColumn(icono, 0);

                var lblHora = new Label
                {
                    Text            = $"{item.HoraProgramada:HH:mm} hrs",
                    TextColor       = Colors.White,
                    FontSize        = 16,
                    FontAttributes  = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                };
                Grid.SetColumn(lblHora, 1);

                var lblDuracion = new Label
                {
                    Text            = item.DuracionStr,
                    TextColor       = Color.FromArgb("#888888"),
                    FontSize        = 13,
                    VerticalOptions = LayoutOptions.Center,
                };
                Grid.SetColumn(lblDuracion, 2);

                fila1.Children.Add(icono);
                fila1.Children.Add(lblHora);
                fila1.Children.Add(lblDuracion);
                stack.Children.Add(fila1);

                // ── Fila 2: puntos + badge incidencias ───────────────────
                var fila2 = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto },
                    },
                    ColumnSpacing = 12,
                };

                fila2.Children.Add(new Label
                {
                    Text      = item.Estado == 0
                        ? "No iniciado"
                        : $"{item.PuntosVisitados}/{item.PuntosTotal} puntos",
                    TextColor = Color.FromArgb("#888888"),
                    FontSize  = 13,
                });

                if (tieneIncidencias)
                {
                    var lblInc = new Label
                    {
                        Text           = $"{item.TotalIncidencias} incidencia(s)",
                        TextColor      = Color.FromArgb("#F09595"),
                        FontSize       = 13,
                        FontAttributes = FontAttributes.Bold,
                    };
                    Grid.SetColumn(lblInc, 1);
                    fila2.Children.Add(lblInc);
                }

                stack.Children.Add(fila2);
            }

            // ── Incidencias detalladas (todas: del rondín + sueltas) ──────
            foreach (var inc in item.TodasLasIncidencias)
                stack.Children.Add(CrearFichaIncidencia(inc));

            card.Content = stack;
            return card;
        }

        private View CrearFichaIncidencia(Incidencia inc)
        {
            var incCard = new Border
            {
                BackgroundColor = Color.FromArgb("#2A1A1A"),
                StrokeThickness = 0,
                Padding         = new Thickness(12, 8),
            };
            incCard.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };

            var incStack = new VerticalStackLayout { Spacing = 2 };

            incStack.Children.Add(new Label
            {
                Text           = inc.Descripcion,
                TextColor      = Color.FromArgb("#F09595"),
                FontSize       = 13,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode  = LineBreakMode.TailTruncation,
                MaxLines       = 2,
            });

            string subText = !string.IsNullOrEmpty(inc.NombrePunto)
                ? $"{inc.NombrePunto} · {inc.FechaReporte:HH:mm}"
                : inc.FechaReporte.ToString("HH:mm");

            incStack.Children.Add(new Label
            {
                Text      = subText,
                TextColor = Color.FromArgb("#888888"),
                FontSize  = 12,
            });

            incCard.Content = incStack;
            return incCard;
        }

        private async Task ShowToastAsync(string message)
        {
            ToastLabel.Text            = message;
            ToastFrame.BackgroundColor = Color.FromArgb("#FF5555");
            ToastFrame.IsVisible       = true;
            ToastFrame.Opacity         = 0;
            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}
