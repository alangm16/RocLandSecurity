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

            TituloFoto.Text = _titulo;

            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            ImgEvidencia.IsVisible = false;
            LblEstado.IsVisible = false;

            try
            {
                byte[]? fotoBytes = _incidenciaID > 0
                    ? await _db.GetFotoIncidenciaAsync(_incidenciaID)
                    : await _db.GetFotoPuntoAsync(_rondinPuntoID);

                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;

                if (fotoBytes != null && fotoBytes.Length > 0)
                {
                    ImgEvidencia.Source = ImageSource.FromStream(() => new MemoryStream(fotoBytes));
                    ImgEvidencia.IsVisible = true;
                }
                else
                {
                    LblEstado.Text = "No hay foto registrada\npara esta incidencia.";
                    LblEstado.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;
                LblEstado.Text = $"No se pudo cargar la foto:\n{ex.Message}";
                LblEstado.IsVisible = true;
            }
        }

        private async void OnCerrarClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}
