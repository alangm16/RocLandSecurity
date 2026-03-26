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

        public FotoEvidenciaSupervisorPage(SupervisorDatabaseService db, int rondinPuntoID)
        {
            InitializeComponent();
            _db = db;
            _rondinPuntoID = rondinPuntoID;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Estado inicial: cargando
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            ImgEvidencia.IsVisible = false;
            LblEstado.IsVisible = false;

            try
            {
                var fotoBytes = await _db.GetFotoPuntoAsync(_rondinPuntoID);

                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;

                if (fotoBytes != null && fotoBytes.Length > 0)
                {
                    ImgEvidencia.Source = ImageSource.FromStream(() => new MemoryStream(fotoBytes));
                    ImgEvidencia.IsVisible = true;
                }
                else
                {
                    LblEstado.Text = "No hay foto registrada\npara este punto.";
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
