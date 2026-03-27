using RocLandSecurity.Services;
using System.IO;

namespace RocLandSecurity.Views.Supervisor
{
    /// <summary>
    /// Página exclusiva del SUPERVISOR para visualizar la foto de evidencia de un punto.
    /// Consulta ÚNICAMENTE SQL Server a través de SupervisorDatabaseService.
    /// No toca SQLite local en ningún momento.
    /// Se navega desde RondinDetalleSupervisorPage vía Navigation.PushModalAsync.
    /// </summary>
    public partial class FotoEvidenciaSupervisorPage : ContentPage
    {
        private readonly SupervisorDatabaseService _db;
        private readonly int _rondinPuntoID;
        private readonly int _incidenciaID;
        private readonly string _titulo;
        private static readonly Dictionary<string, byte[]> _fotoCache = new();

        // Constructor original — foto de punto de rondín
        public FotoEvidenciaSupervisorPage(SupervisorDatabaseService db, int rondinPuntoID)
        {
            InitializeComponent();
            _db = db;
            _rondinPuntoID = rondinPuntoID;
            _incidenciaID = 0;
            _titulo = "Foto de Evidencia";
        }

        // Constructor nuevo — foto de incidencia
        public FotoEvidenciaSupervisorPage(SupervisorDatabaseService db, int incidenciaID, bool esIncidencia)
        {
            InitializeComponent();
            _db = db;
            _rondinPuntoID = 0;
            _incidenciaID = incidenciaID;
            _titulo = "Foto de Incidencia";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            Task.Run(CargarFotoAsync);
        }

        public async Task CargarFotoAsync ()
        {
            string cacheKey = _incidenciaID > 0 ? $"inc_{_incidenciaID}" : $"punto_{_rondinPuntoID}";

            // Actualiza UI desde el hilo principal
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TituloFoto.Text = _titulo;
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                ImgEvidencia.IsVisible = false;
                LblEstado.IsVisible = false;
            });

            try
            {
                byte[]? fotoBytes;

                // ¿Ya la tenemos en caché?
                if (_fotoCache.TryGetValue(cacheKey, out var cached))
                    fotoBytes = cached;
                else
                {
                    fotoBytes = _incidenciaID > 0
                        ? await _db.GetFotoIncidenciaAsync(_incidenciaID)
                        : await _db.GetFotoPuntoAsync(_rondinPuntoID);

                    if (fotoBytes != null && fotoBytes.Length > 0)
                        _fotoCache[cacheKey] = fotoBytes; // guarda para la próxima vez
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsRunning = false;
                    LoadingIndicator.IsVisible = false;

                    if (fotoBytes != null && fotoBytes.Length > 0)
                    {
                        ImgEvidencia.Source = ImageSource.FromStream(
                            () => new System.IO.MemoryStream(fotoBytes));
                        ImgEvidencia.IsVisible = true;
                    }
                    else
                    {
                        LblEstado.Text = "No hay foto registrada.";
                        LblEstado.IsVisible = true;
                    }
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsRunning = false;
                    LoadingIndicator.IsVisible = false;
                    LblEstado.Text = $"Error: {ex.Message}";
                    LblEstado.IsVisible = true;
                });
            }
        }

        private async void OnCerrarClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}
