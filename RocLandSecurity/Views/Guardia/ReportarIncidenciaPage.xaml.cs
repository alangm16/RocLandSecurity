using RocLandSecurity.Models;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Guardia
{
    /// <summary>
    /// Página de reporte de incidencias.
    /// Recibe rondinId y turnoId como QueryProperties.
    /// Si rondinId = 0 significa que se reporta fuera de rondín activo.
    /// </summary>
    [QueryProperty(nameof(RondinId), "rondinId")]
    [QueryProperty(nameof(TurnoId),  "turnoId")]
    public partial class ReportarIncidenciaPage : ContentPage
    {
        private readonly DatabaseService _db;
        private readonly SessionService  _session;

        private int _rondinId = 0;   // 0 = fuera de rondín
        private int _turnoId  = 0;

        public string RondinId { set => _rondinId = int.TryParse(value, out var v) ? v : 0; }
        public string TurnoId  { set => _turnoId  = int.TryParse(value, out var v) ? v : 0; }

        // Estado del formulario
        private int               _severidad     = 1;   // 0=Baja 1=Media 2=Alta
        private int?              _puntoIdSeleccionado;  // null = ubicación libre
        private List<PuntoControl> _puntos        = new();

        // Colores
        private static readonly Color ClrSelected    = Color.FromArgb("#6DBF2E");
        private static readonly Color ClrSelectedTxt = Color.FromArgb("#111111");
        private static readonly Color ClrIdle        = Color.FromArgb("#252525");
        private static readonly Color ClrIdleTxt     = Color.FromArgb("#888888");
        private static readonly Color ClrIdleStroke  = Color.FromArgb("#2A2A2A");

        public ReportarIncidenciaPage(DatabaseService db, SessionService session)
        {
            InitializeComponent();
            _db      = db;
            _session = session;
        }

        // ─────────────────────────────────────────────────────────────────
        // CICLO DE VIDA
        // ─────────────────────────────────────────────────────────────────

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarPuntosAsync();
            MostrarContexto();
            ActualizarBotonesSeveridad();
            ActualizarTextoBoton();
        }

        // ─────────────────────────────────────────────────────────────────
        // CARGA INICIAL
        // ─────────────────────────────────────────────────────────────────

        private async Task CargarPuntosAsync()
        {
            try
            {
                _puntos = await _db.GetPuntosControlAsync();

                PickerPunto.Items.Clear();
                PickerPunto.Items.Add("Otra ubicación (libre)");   // índice 0

                foreach (var p in _puntos)
                    PickerPunto.Items.Add($"{p.Orden}. {p.Nombre}");

                PickerPunto.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error cargando puntos: {ex.Message}");
            }
        }

        private void MostrarContexto()
        {
            if (_rondinId > 0)
            {
                LblContexto.Text      = $"Rondín ID {_rondinId} · Turno ID {_turnoId}";
                PanelContexto.IsVisible = true;
            }
            else if (_turnoId > 0)
            {
                LblContexto.Text      = $"Fuera de rondín · Turno ID {_turnoId}";
                PanelContexto.IsVisible = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // SEVERIDAD
        // ─────────────────────────────────────────────────────────────────

        private void OnSeveridadBajaClicked(object sender, EventArgs e)  { _severidad = 0; ActualizarBotonesSeveridad(); ActualizarTextoBoton(); }
        private void OnSeveridadMediaClicked(object sender, EventArgs e) { _severidad = 1; ActualizarBotonesSeveridad(); ActualizarTextoBoton(); }
        private void OnSeveridadAltaClicked(object sender, EventArgs e)  { _severidad = 2; ActualizarBotonesSeveridad(); ActualizarTextoBoton(); }

        private void ActualizarBotonesSeveridad()
        {
            // Baja
            BtnBaja.BackgroundColor  = _severidad == 0 ? ClrSelected : ClrIdle;
            BtnBaja.Stroke           = _severidad == 0 ? ClrSelected : ClrIdleStroke;
            BtnBaja.StrokeThickness  = _severidad == 0 ? 0 : 1;
            ((Label)BtnBaja.Content).TextColor      = _severidad == 0 ? ClrSelectedTxt : ClrIdleTxt;
            ((Label)BtnBaja.Content).FontAttributes = _severidad == 0 ? FontAttributes.Bold : FontAttributes.None;

            // Media
            BtnMedia.BackgroundColor  = _severidad == 1 ? ClrSelected : ClrIdle;
            BtnMedia.Stroke           = _severidad == 1 ? ClrSelected : ClrIdleStroke;
            BtnMedia.StrokeThickness  = _severidad == 1 ? 0 : 1;
            ((Label)BtnMedia.Content).TextColor      = _severidad == 1 ? ClrSelectedTxt : ClrIdleTxt;
            ((Label)BtnMedia.Content).FontAttributes = _severidad == 1 ? FontAttributes.Bold : FontAttributes.None;

            // Alta
            BtnAlta.BackgroundColor  = _severidad == 2 ? Color.FromArgb("#7B1A1A") : ClrIdle;
            BtnAlta.Stroke           = _severidad == 2 ? Color.FromArgb("#7B1A1A") : ClrIdleStroke;
            BtnAlta.StrokeThickness  = _severidad == 2 ? 0 : 1;
            ((Label)BtnAlta.Content).TextColor      = _severidad == 2 ? Colors.White : ClrIdleTxt;
            ((Label)BtnAlta.Content).FontAttributes = _severidad == 2 ? FontAttributes.Bold : FontAttributes.None;
        }

        private void ActualizarTextoBoton()
        {
            BtnEnviar.Text = _severidad == 2
                ? "Enviar Reporte URGENTE"
                : "Enviar Reporte";

            BtnEnviar.BackgroundColor = _severidad == 2
                ? Color.FromArgb("#A32D2D")
                : Color.FromArgb("#7B1A1A");
        }

        // ─────────────────────────────────────────────────────────────────
        // UBICACIÓN
        // ─────────────────────────────────────────────────────────────────

        private void OnPuntoSeleccionado(object sender, EventArgs e)
        {
            int idx = PickerPunto.SelectedIndex;

            if (idx <= 0)
            {
                // -1 = sin selección,  0 = "Otra ubicación"
                _puntoIdSeleccionado        = null;
                PanelUbicacionLibre.IsVisible = (idx == 0);
            }
            else
            {
                // idx 1..N mapea a _puntos[idx-1]
                _puntoIdSeleccionado          = _puntos[idx - 1].ID;
                PanelUbicacionLibre.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // ENVIAR
        // ─────────────────────────────────────────────────────────────────

        private async void OnEnviarClicked(object sender, EventArgs e)
        {
            // Validaciones
            string descripcion = EditorDescripcion.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(descripcion))
            {
                await ShowToastAsync("La descripción es obligatoria.");
                return;
            }

            if (PickerPunto.SelectedIndex < 0)
            {
                await ShowToastAsync("Selecciona una ubicación.");
                return;
            }

            // Ubicación libre — guardar en descripción si no hay punto
            string ubicacionLibre = "";
            if (_puntoIdSeleccionado == null && PickerPunto.SelectedIndex == 0)
            {
                ubicacionLibre = EntryUbicacionLibre.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(ubicacionLibre))
                {
                    await ShowToastAsync("Escribe la ubicación de la incidencia.");
                    return;
                }
                descripcion = $"[{ubicacionLibre}] {descripcion}";
            }

            BtnEnviar.IsEnabled = false;
            BtnEnviar.Text      = "Enviando...";

            try
            {
                var usuario = _session.UsuarioActual!;

                await _db.CrearIncidenciaAsync(new Incidencia
                {
                    TurnoID          = _turnoId,
                    RondinID         = _rondinId > 0 ? _rondinId : null,
                    PuntoID          = _puntoIdSeleccionado,
                    GuardiaReportaID = usuario.ID,
                    Descripcion      = descripcion,
                    Estado           = 0,
                    FechaReporte     = DateTime.Now,
                    FechaModificacion = DateTime.Now,
                });

                await ShowToastAsync("Incidencia reportada", isError: false);
                await Task.Delay(1200);
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Error: {ex.Message}");
                BtnEnviar.IsEnabled = true;
                ActualizarTextoBoton();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // VOLVER
        // ─────────────────────────────────────────────────────────────────

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("..");
        }

        // ─────────────────────────────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message, bool isError = true)
        {
            ToastLabel.Text            = message;
            ToastFrame.BackgroundColor = isError
                ? Color.FromArgb("#FF5555")
                : Color.FromArgb("#6DBF2E");
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity   = 0;
            await ToastFrame.FadeTo(1, 200);
            await Task.Delay(2200);
            await ToastFrame.FadeTo(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}
