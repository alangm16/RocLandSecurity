warning: in the working copy of 'RocLandSecurity/Views/Supervisor/SupervisorHistorialPage.xaml.cs', LF will be replaced by CRLF the next time Git touches it
[1mdiff --git a/RocLandSecurity/Views/Guardia/HistorialGuardiaPage.xaml.cs b/RocLandSecurity/Views/Guardia/HistorialGuardiaPage.xaml.cs[m
[1mindex ff77171..2b04e39 100644[m
[1m--- a/RocLandSecurity/Views/Guardia/HistorialGuardiaPage.xaml.cs[m
[1m+++ b/RocLandSecurity/Views/Guardia/HistorialGuardiaPage.xaml.cs[m
[36m@@ -67,8 +67,27 @@[m [mnamespace RocLandSecurity.Views.Guardia[m
                 if (esOffline)[m
                     ListaHistorial.Children.Add(CrearBannerOffline());[m
 [m
[32m+[m[32m                // Agrupar rondines por fecha (día del turno)[m
[32m+[m[32m                // Turno nocturno cruza medianoche: la fecha del turno es la de HoraProgramada[m
[32m+[m[32m                DateTime? fechaActual = null;[m
                 foreach (var item in items)[m
[32m+[m[32m                {[m
[32m+[m[32m                    // Fecha del día al que pertenece este rondín[m
[32m+[m[32m                    var fechaItem = item.HoraProgramada.Date;[m
[32m+[m
[32m+[m[32m                    // Si el rondín es nocturno (hora < 08:00) pertenece al día anterior[m
[32m+[m[32m                    if (item.HoraProgramada.Hour < 8)[m
[32m+[m[32m                        fechaItem = item.HoraProgramada.Date.AddDays(-1);[m
[32m+[m
[32m+[m[32m                    // Insertar separador cuando cambia el día[m
[32m+[m[32m                    if (fechaActual == null || fechaActual.Value.Date != fechaItem)[m
[32m+[m[32m                    {[m
[32m+[m[32m                        fechaActual = fechaItem;[m
[32m+[m[32m                        ListaHistorial.Children.Add(CrearSeparadorDia(fechaItem));[m
[32m+[m[32m                    }[m
[32m+[m
                     ListaHistorial.Children.Add(CrearTarjetaHistorial(item));[m
[32m+[m[32m                }[m
             }[m
             catch (Exception ex)[m
             {[m
[36m@@ -80,6 +99,39 @@[m [mnamespace RocLandSecurity.Views.Guardia[m
             }[m
         }[m
 [m
[32m+[m[32m        // ─────────────────────────────────────────────────────────────────[m
[32m+[m[32m        // SEPARADOR DE DÍA[m
[32m+[m[32m        // ─────────────────────────────────────────────────────────────────[m
[32m+[m
[32m+[m[32m        private View CrearSeparadorDia(DateTime fecha)[m
[32m+[m[32m        {[m
[32m+[m[32m            var cultura = new System.Globalization.CultureInfo("es-MX");[m
[32m+[m
[32m+[m[32m            string etiqueta;[m
[32m+[m[32m            if (fecha.Date == DateTime.Today)[m
[32m+[m[32m                etiqueta = "HOY";[m
[32m+[m[32m            else if (fecha.Date == DateTime.Today.AddDays(-1))[m
[32m+[m[32m                etiqueta = "AYER";[m
[32m+[m[32m            else[m
[32m+[m[32m                etiqueta = cultura.DateTimeFormat.GetDayName(fecha.DayOfWeek).ToUpper();[m
[32m+[m
[32m+[m[32m            // Formato: HOY · SÁBADO 21 - 03 - 2026[m
[32m+[m[32m            string fechaStr = $"{fecha:dd} - {fecha:MM} - {fecha:yyyy}";[m
[32m+[m[32m            string textoCompleto = etiqueta == "HOY" || etiqueta == "AYER"[m
[32m+[m[32m                ? $"{etiqueta}  ·  {cultura.DateTimeFormat.GetDayName(fecha.DayOfWeek).ToUpper()}  {fechaStr}"[m
[32m+[m[32m                : $"{etiqueta}  {fechaStr}";[m
[32m+[m
[32m+[m[32m            return new Label[m
[32m+[m[32m            {[m
[32m+[m[32m                Text = textoCompleto,[m
[32m+[m[32m                TextColor = Color.FromArgb("#555555"),[m
[32m+[m[32m                FontSize = 11,[m
[32m+[m[32m                FontAttributes = FontAttributes.Bold,[m
[32m+[m[32m                Margin = new Thickness(4, 16, 0, 6),[m
[32m+[m[32m                LineBreakMode = LineBreakMode.NoWrap,[m
[32m+[m[32m            };[m
[32m+[m[32m        }[m
[32m+[m
         // ─────────────────────────────────────────────────────────────────[m
         // BANNER OFFLINE[m
         // ─────────────────────────────────────────────────────────────────[m
[1mdiff --git a/RocLandSecurity/Views/Supervisor/SupervisorHistorialPage.xaml.cs b/RocLandSecurity/Views/Supervisor/SupervisorHistorialPage.xaml.cs[m
[1mindex a7230f0..86be701 100644[m
[1m--- a/RocLandSecurity/Views/Supervisor/SupervisorHistorialPage.xaml.cs[m
[1m+++ b/RocLandSecurity/Views/Supervisor/SupervisorHistorialPage.xaml.cs[m
[36m@@ -12,6 +12,8 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
         private DateTime       _fechaSeleccionada = DateTime.Today;[m
         private List<DateTime> _fechasConActividad = new();[m
 [m
[32m+[m[32m        private bool _cargandoHistorial = false;[m
[32m+[m
         public SupervisorHistorialPage(DatabaseService db, SessionService session)[m
         {[m
             InitializeComponent();[m
[36m@@ -145,17 +147,17 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
         {[m
             _fechaActual = _fechaActual.AddMonths(-1);[m
             await CargarCalendarioAsync();[m
[31m-            AjustarFechaSeleccionada();[m
[32m+[m[32m            await AjustarFechaSeleccionada();[m
         }[m
 [m
         private async void OnMesSiguienteClicked(object sender, EventArgs e)[m
         {[m
             _fechaActual = _fechaActual.AddMonths(1);[m
             await CargarCalendarioAsync();[m
[31m-            AjustarFechaSeleccionada();[m
[32m+[m[32m            await AjustarFechaSeleccionada();[m
         }[m
 [m
[31m-        private async void AjustarFechaSeleccionada()[m
[32m+[m[32m        private async Task AjustarFechaSeleccionada()[m
         {[m
             var primero = new DateTime(_fechaActual.Year, _fechaActual.Month, 1);[m
             var ultimo  = primero.AddMonths(1).AddDays(-1);[m
[36m@@ -163,7 +165,7 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
             {[m
                 _fechaSeleccionada = primero;[m
                 ActualizarCalendario();[m
[31m-                await CargarHistorialDiaAsync(_fechaSeleccionada);[m
[32m+[m[32m                await CargarHistorialDiaAsync(_fechaSeleccionada);[m[41m  [m
             }[m
         }[m
 [m
[36m@@ -173,8 +175,14 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
 [m
         private async Task CargarHistorialDiaAsync(DateTime fecha)[m
         {[m
[32m+[m[32m            // Evitar cargas concurrentes[m
[32m+[m[32m            if (_cargandoHistorial) return;[m
[32m+[m
[32m+[m[32m            _cargandoHistorial = true;[m
             LoadingIndicator.IsVisible = true;[m
[31m-            PanelContenido.IsVisible   = false;[m
[32m+[m[32m            PanelContenido.IsVisible = false;[m
[32m+[m
[32m+[m[32m            // Limpiar correctamente el StackLayout[m
             ListaTurnos.Children.Clear();[m
 [m
             try[m
[36m@@ -188,11 +196,11 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
                     // Título del día[m
                     ListaTurnos.Children.Add(new Label[m
                     {[m
[31m-                        Text           = historial.Titulo,[m
[31m-                        TextColor      = Color.FromArgb("#97C459"),[m
[31m-                        FontSize       = 13,[m
[32m+[m[32m                        Text = historial.Titulo,[m
[32m+[m[32m                        TextColor = Color.FromArgb("#97C459"),[m
[32m+[m[32m                        FontSize = 13,[m
                         FontAttributes = FontAttributes.Bold,[m
[31m-                        Margin         = new Thickness(0, 0, 0, 4),[m
[32m+[m[32m                        Margin = new Thickness(0, 0, 0, 4),[m
                     });[m
 [m
                     foreach (var turno in historial.Turnos)[m
[36m@@ -208,6 +216,7 @@[m [mnamespace RocLandSecurity.Views.Supervisor[m
             finally[m
             {[m
                 LoadingIndicator.IsVisible = false;[m
[32m+[m[32m                _cargandoHistorial = false;[m
             }[m
         }[m
 [m
