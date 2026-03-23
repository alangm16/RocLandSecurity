using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class SupervisorHistorialPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService  _session;

        private DateTime       _fechaActual       = DateTime.Today;
        private DateTime       _fechaSeleccionada = DateTime.Today;
        private List<DateTime> _fechasConActividad = new();

        private bool _cargandoHistorial = false;

        public SupervisorHistorialPage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db      = db;
            _session = session;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarCalendarioAsync();
            await CargarHistorialDiaAsync(_fechaSeleccionada);
        }

        // ─────────────────────────────────────────────────────────────────
        // CALENDARIO
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarCalendarioAsync()
        {
            try
            {
                _fechasConActividad = await _db.GetFechasConActividadAsync();
                ActualizarCalendario();
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error cargando calendario: {ex.Message}");
            }
        }

        private void ActualizarCalendario()
        {
            LblMesActual.Text = _fechaActual.ToString("MMMM yyyy",
                new System.Globalization.CultureInfo("es-MX"));

            GridDias.Children.Clear();
            GridDias.RowDefinitions.Clear();

            var primerDia  = new DateTime(_fechaActual.Year, _fechaActual.Month, 1);
            int diasEnMes  = DateTime.DaysInMonth(_fechaActual.Year, _fechaActual.Month);
            int offset     = ((int)primerDia.DayOfWeek + 6) % 7; // Lunes = 0

            // Calcular filas necesarias
            int totalCeldas = offset + diasEnMes;
            int filasNeeded = (int)Math.Ceiling(totalCeldas / 7.0);
            for (int f = 0; f < filasNeeded; f++)
                GridDias.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int fila = 0, col = 0;

            // Celdas vacías
            for (int i = 0; i < offset; i++)
            {
                GridDias.Children.Add(new BoxView { Color = Colors.Transparent,
                    HeightRequest = 36 });
                Grid.SetRow(GridDias.Children.Last() as View, fila);
                Grid.SetColumn(GridDias.Children.Last() as View, col);
                Avanzar(ref fila, ref col);
            }

            // Días del mes
            for (int dia = 1; dia <= diasEnMes; dia++)
            {
                var fechaDia      = new DateTime(_fechaActual.Year, _fechaActual.Month, dia);
                bool tieneActiv   = _fechasConActividad.Any(f => f.Date == fechaDia.Date);
                bool esSelec      = fechaDia.Date == _fechaSeleccionada.Date;
                bool esHoy        = fechaDia.Date == DateTime.Today;

                var celda = CrearCeldaDia(dia, fechaDia, tieneActiv, esSelec, esHoy);
                GridDias.Children.Add(celda);
                Grid.SetRow(celda, fila);
                Grid.SetColumn(celda, col);
                Avanzar(ref fila, ref col);
            }
        }

        private static void Avanzar(ref int fila, ref int col)
        {
            col++;
            if (col >= 7) { col = 0; fila++; }
        }

        private View CrearCeldaDia(int dia, DateTime fecha, bool tieneActiv, bool esSelec, bool esHoy)
        {
            Color bgColor   = esSelec    ? Color.FromArgb("#6DBF2E")
                            : tieneActiv ? Color.FromArgb("#1A2A1A")
                            :              Color.FromArgb("#1A1A1A");

            Color txtColor  = esSelec    ? Color.FromArgb("#111111")
                            : tieneActiv ? Color.FromArgb("#97C459")
                            :              Color.FromArgb("#888888");

            var border = new Border
            {
                BackgroundColor = bgColor,
                StrokeThickness = esHoy && !esSelec ? 1 : 0,
                Stroke          = Color.FromArgb("#6DBF2E"),
                HeightRequest   = 36,
                WidthRequest    = 36,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            border.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(10) };

            border.Content = new Label
            {
                Text           = dia.ToString(),
                TextColor      = txtColor,
                FontSize       = 13,
                FontAttributes = esSelec ? FontAttributes.Bold : FontAttributes.None,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };

            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command          = new Command(async () =>
                {
                    _fechaSeleccionada = fecha;
                    ActualizarCalendario();
                    await CargarHistorialDiaAsync(fecha);
                })
            });

            return border;
        }

        private async void OnMesAnteriorClicked(object sender, EventArgs e)
        {
            _fechaActual = _fechaActual.AddMonths(-1);
            await CargarCalendarioAsync();
            await AjustarFechaSeleccionada();
        }

        private async void OnMesSiguienteClicked(object sender, EventArgs e)
        {
            _fechaActual = _fechaActual.AddMonths(1);
            await CargarCalendarioAsync();
            await AjustarFechaSeleccionada();
        }

        private async Task AjustarFechaSeleccionada()
        {
            var primero = new DateTime(_fechaActual.Year, _fechaActual.Month, 1);
            var ultimo  = primero.AddMonths(1).AddDays(-1);
            if (_fechaSeleccionada < primero || _fechaSeleccionada > ultimo)
            {
                _fechaSeleccionada = primero;
                ActualizarCalendario();
                await CargarHistorialDiaAsync(_fechaSeleccionada);  
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // HISTORIAL DEL DÍA
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarHistorialDiaAsync(DateTime fecha)
        {
            // Evitar cargas concurrentes
            if (_cargandoHistorial) return;

            _cargandoHistorial = true;
            LoadingIndicator.IsVisible = true;
            PanelContenido.IsVisible = false;

            // Limpiar correctamente el StackLayout
            ListaTurnos.Children.Clear();

            try
            {
                var historial = await _db.GetHistorialPorFechaAsync(fecha);

                PanelSinActividad.IsVisible = !historial.TieneRegistros;

                if (historial.TieneRegistros)
                {
                    // Título del día
                    ListaTurnos.Children.Add(new Label
                    {
                        Text = historial.Titulo,
                        TextColor = Color.FromArgb("#97C459"),
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        Margin = new Thickness(0, 0, 0, 4),
                    });

                    foreach (var turno in historial.Turnos)
                        ListaTurnos.Children.Add(CrearTarjetaTurno(turno));
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
                _cargandoHistorial = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // TARJETA DE TURNO
        // ─────────────────────────────────────────────────────────────────

        private View CrearTarjetaTurno(HistorialTurnoDia turno)
        {
            // Total real = incidencias fuera de rondín + incidencias dentro de rondines
            int totalIncidencias = turno.Incidencias.Count
                + turno.Rondines.Sum(r => r.IncidenciasCount);

            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#1A1A1A"),
                Stroke          = totalIncidencias > 0
                    ? Color.FromArgb("#3A1A1A")
                    : Color.FromArgb("#2A2A2A"),
                StrokeThickness = 1,
                Padding         = new Thickness(16, 14),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(14) };

            var stack = new VerticalStackLayout { Spacing = 12 };

            // ── Encabezado: guardia + badge cumplimiento ──────────────────
            var fila1 = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                }
            };
            fila1.Children.Add(new Label
            {
                Text           = $"{turno.NombreGuardia}",
                TextColor      = Colors.White,
                FontSize       = 15,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(fila1.Children.Last() as View, 0);
            var badgeCump = CrearBadgeCumplimiento(turno.Cumplimiento);
            Grid.SetColumn(badgeCump, 1);
            fila1.Children.Add(badgeCump);
            stack.Children.Add(fila1);

            // ── Horario ───────────────────────────────────────────────────
            stack.Children.Add(new Label
            {
                Text      = turno.RangoHorario,
                TextColor = Color.FromArgb("#888888"),
                FontSize  = 13,
                Margin    = new Thickness(0, -8, 0, 0),
            });

            // ── Métricas: rondines + incidencias ──────────────────────────
            var fila2 = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                },
                ColumnSpacing = 10,
            };

            // Métrica rondines con PNG
            fila2.Children.Add(CrearMetricaConIcono(
                "rondin_icon.png",
                $"{turno.TotalRondines} rondines",
                "#2A2A2A", Colors.White));
            Grid.SetColumn(fila2.Children.Last() as View, 0);

            // Métrica incidencias con PNG
            fila2.Children.Add(CrearMetricaIncidencias(totalIncidencias));
            Grid.SetColumn(fila2.Children.Last() as View, 1);

            stack.Children.Add(fila2);

            // ── Rondines ──────────────────────────────────────────────────
            if (turno.Rondines?.Count > 0)
            {
                stack.Children.Add(new Label
                {
                    Text      = "RONDINES",
                    TextColor = Color.FromArgb("#555555"),
                    FontSize  = 11,
                    FontAttributes = FontAttributes.Bold,
                });

                foreach (var r in turno.Rondines)
                    stack.Children.Add(CrearFichaRondin(r));
            }

            // ── Incidencias fuera de rondín ───────────────────────────────
            if (turno.Incidencias?.Count > 0)
            {
                stack.Children.Add(new Label
                {
                    Text           = "INCIDENCIAS FUERA DE RONDÍN",
                    TextColor      = Color.FromArgb("#F09595"),
                    FontSize       = 11,
                    FontAttributes = FontAttributes.Bold,
                    Margin         = new Thickness(0, 4, 0, 0),
                });

                foreach (var inc in turno.Incidencias)
                    stack.Children.Add(CrearFichaIncidencia(inc));
            }

            card.Content = stack;
            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // FICHA DE RONDÍN
        // ─────────────────────────────────────────────────────────────────

        private View CrearFichaRondin(HistorialRondinDia rondin)
        {
            var clr = Color.FromArgb(rondin.EstadoColor);
            var bg  = Color.FromArgb(rondin.EstadoColorFondo);

            var row = new Border
            {
                BackgroundColor = Color.FromArgb("#141414"),
                StrokeThickness = 0.5,
                Stroke          = Color.FromArgb("#2A2A2A"),
                Padding         = new Thickness(12, 10),
            };
            row.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(10) };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                ColumnSpacing = 8,
            };

            // Hora
            grid.Children.Add(new Label
            {
                Text           = rondin.HoraStr,
                TextColor      = Colors.White,
                FontSize       = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 0);

            // Puntos
            grid.Children.Add(new Label
            {
                Text      = rondin.PuntosStr,
                TextColor = Color.FromArgb("#888888"),
                FontSize  = 12,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 1);

            // Incidencias del rondín con PNG
            if (rondin.TieneIncidencias)
            {
                var incRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto },
                    },
                    ColumnSpacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                };
                incRow.Children.Add(new Image
                {
                    Source        = "warning.png",
                    WidthRequest  = 12,
                    HeightRequest = 12,
                    VerticalOptions = LayoutOptions.Center,
                });
                Grid.SetColumn(incRow.Children.Last() as View, 0);
                incRow.Children.Add(new Label
                {
                    Text      = rondin.IncidenciasCount.ToString(),
                    TextColor = Color.FromArgb("#F09595"),
                    FontSize  = 11,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                });
                Grid.SetColumn(incRow.Children.Last() as View, 1);
                Grid.SetColumn(incRow, 2);
                grid.Children.Add(incRow);
            }

            // Badge estado
            var badge = new Border
            {
                BackgroundColor = bg,
                StrokeThickness = 1,
                Stroke          = clr,
                Padding         = new Thickness(6, 3),
                VerticalOptions = LayoutOptions.Center,
            };
            badge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(6) };
            badge.Content = new Label
            {
                Text           = rondin.EstadoTexto,
                TextColor      = clr,
                FontSize       = 10,
                FontAttributes = FontAttributes.Bold,
            };
            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);

            row.Content = grid;

            // Si hay incidencias, envolver en VerticalStackLayout y agregar descripción
            if (rondin.TieneIncidencias && rondin.Incidencias.Count > 0)
            {
                var wrapper = new VerticalStackLayout { Spacing = 6 };
                wrapper.Children.Add(grid);

                foreach (var inc in rondin.Incidencias)
                {
                    var incFila = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = GridLength.Auto },
                            new ColumnDefinition { Width = GridLength.Star },
                        },
                        ColumnSpacing = 6,
                    };

                    incFila.Children.Add(new Image
                    {
                        Source          = "warning.png",
                        WidthRequest    = 11,
                        HeightRequest   = 11,
                        VerticalOptions = LayoutOptions.Center,
                    });
                    Grid.SetColumn(incFila.Children.Last() as View, 0);

                    incFila.Children.Add(new Label
                    {
                        Text          = inc.Descripcion,
                        TextColor     = Color.FromArgb("#F09595"),
                        FontSize      = 11,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        MaxLines      = 2,
                        VerticalOptions = LayoutOptions.Center,
                    });
                    Grid.SetColumn(incFila.Children.Last() as View, 1);

                    wrapper.Children.Add(incFila);
                }

                row.Content = wrapper;
            }

            return row;
        }

        // ─────────────────────────────────────────────────────────────────
        // FICHA DE INCIDENCIA
        // ─────────────────────────────────────────────────────────────────

        private View CrearFichaIncidencia(IncidenciaSupervisorItem inc)
        {
            var card = new Border
            {
                BackgroundColor = Color.FromArgb("#2A1A1A"),
                StrokeThickness = 0,
                Padding         = new Thickness(12, 8),
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };

            var stack = new VerticalStackLayout { Spacing = 2 };

            stack.Children.Add(new Label
            {
                Text           = inc.Descripcion,
                TextColor      = Color.FromArgb("#F09595"),
                FontSize       = 13,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode  = LineBreakMode.WordWrap,
            });

            string sub = !string.IsNullOrEmpty(inc.NombrePunto)
                ? $"{inc.NombrePunto} · {inc.HoraStr}"
                : inc.HoraStr;
            stack.Children.Add(new Label
            {
                Text      = sub,
                TextColor = Color.FromArgb("#888888"),
                FontSize  = 11,
            });

            if (!string.IsNullOrEmpty(inc.NotaResolucion))
            {
                // Fila con check.png + nota
                var filaRes = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star },
                    },
                    ColumnSpacing = 4,
                    Margin        = new Thickness(0, 2, 0, 0),
                };
                filaRes.Children.Add(new Image
                {
                    Source        = "check.png",
                    WidthRequest  = 12,
                    HeightRequest = 12,
                    VerticalOptions = LayoutOptions.Center,
                });
                Grid.SetColumn(filaRes.Children.Last() as View, 0);
                filaRes.Children.Add(new Label
                {
                    Text      = $"Resuelta: {inc.NotaResolucion}",
                    TextColor = Color.FromArgb("#97C459"),
                    FontSize  = 11,
                });
                Grid.SetColumn(filaRes.Children.Last() as View, 1);
                stack.Children.Add(filaRes);
            }

            card.Content = stack;
            return card;
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS DE BADGES
        // ─────────────────────────────────────────────────────────────────

        private View CrearBadgeCumplimiento(double cumplimiento)
        {
            Color clr = cumplimiento >= 80 ? Color.FromArgb("#97C459")
                      : cumplimiento >= 50 ? Color.FromArgb("#FAC775")
                      :                      Color.FromArgb("#F09595");

            Color fondo = cumplimiento >= 80 ? Color.FromArgb("#0a1a0a")
                        : cumplimiento >= 50 ? Color.FromArgb("#2a2400")
                        :                      Color.FromArgb("#2a0a0a");

            var b = new Border
            {
                BackgroundColor = fondo,
                Stroke          = clr,
                StrokeThickness = 1,
                Padding         = new Thickness(10, 4),
                VerticalOptions = LayoutOptions.Center,
            };
            b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };
            b.Content = new Label
            {
                Text           = $"{cumplimiento:F0}%",
                TextColor      = clr,
                FontSize       = 12,
                FontAttributes = FontAttributes.Bold,
            };
            return b;
        }

        private View CrearMetricaConIcono(string iconoPng, string texto,
            string bgHex, Color txtColor)
        {
            var b = new Border
            {
                BackgroundColor = Color.FromArgb(bgHex),
                StrokeThickness = 0,
                Padding         = new Thickness(10, 7),
            };
            b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                },
                ColumnSpacing = 6,
            };
            grid.Children.Add(new Image
            {
                Source        = iconoPng,
                WidthRequest  = 14,
                HeightRequest = 14,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 0);
            grid.Children.Add(new Label
            {
                Text      = texto,
                TextColor = txtColor,
                FontSize  = 12,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 1);

            b.Content = grid;
            return b;
        }

        private View CrearMetricaIncidencias(int total)
        {
            bool tiene = total > 0;
            string bgHex = tiene ? "#2a0a0a" : "#2A2A2A";
            Color  clr   = tiene ? Color.FromArgb("#F09595") : Color.FromArgb("#888888");

            var b = new Border
            {
                BackgroundColor = Color.FromArgb(bgHex),
                StrokeThickness = 0,
                Padding         = new Thickness(10, 7),
            };
            b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                },
                ColumnSpacing = 6,
            };
            grid.Children.Add(new Image
            {
                Source        = "warning.png",
                WidthRequest  = 14,
                HeightRequest = 14,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 0);
            grid.Children.Add(new Label
            {
                Text      = $"{total} incidencia(s)",
                TextColor = clr,
                FontSize  = 12,
                VerticalOptions = LayoutOptions.Center,
            });
            Grid.SetColumn(grid.Children.Last() as View, 1);

            b.Content = grid;
            return b;
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async void OnRefrescarClicked(object sender, EventArgs e)
        {
            await CargarCalendarioAsync();
            await CargarHistorialDiaAsync(_fechaSeleccionada);
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
