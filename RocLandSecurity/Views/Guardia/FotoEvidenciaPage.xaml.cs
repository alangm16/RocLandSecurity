using RocLandSecurity.Services;
using System.IO;

namespace RocLandSecurity.Views.Guardia
{
    [QueryProperty(nameof(LocalId), "localId")]
    [QueryProperty(nameof(ServerId), "serverId")]
    public partial class FotoEvidenciaPage : ContentPage
    {
        public string LocalId { get; set; }
        public string ServerId { get; set; }

        // Propiedades para uso directo (no por QueryProperty)
        public bool ModoVisualizacion { get; set; }
        public int PuntoServerID { get; set; }

        private int _localId;
        private byte[]? _fotoBytes;
        private readonly OfflineDatabaseService _offline;
        private readonly DatabaseService _server;

        public FotoEvidenciaPage(OfflineDatabaseService offline, DatabaseService server)
        {
            InitializeComponent();
            _offline = offline;
            _server = server;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Intentar obtener parámetros de QueryProperty
            if (int.TryParse(LocalId, out var lid))
                _localId = lid;
            if (int.TryParse(ServerId, out var sid))
                PuntoServerID = sid;

            // ── Configurar UI inicial según modo ANTES de cualquier llamada async ──
            // Esto evita el parpadeo donde se muestra BtnTomarFoto mientras carga la foto.
            if (ModoVisualizacion && PuntoServerID > 0)
            {
                // Modo supervisor: ocultar controles de guardia de inmediato
                BtnTomarFoto.IsVisible = false;
                PanelConfirmacion.IsVisible = false;
                BtnCerrar.IsVisible = false; // se mostrará cuando la foto cargue

                try
                {
                    _fotoBytes = await _server.GetFotoPuntoAsync(PuntoServerID);

                    if (_fotoBytes != null)
                    {
                        ImgEvidencia.Source = ImageSource.FromStream(() => new MemoryStream(_fotoBytes));
                        BtnCerrar.IsVisible = true;
                    }
                    else
                    {
                        await DisplayAlertAsync("Sin foto", "No hay foto registrada para este punto.", "OK");
                        await Navigation.PopModalAsync();
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("Error", $"No se pudo cargar la foto: {ex.Message}", "OK");
                    await Navigation.PopModalAsync();
                }
            }
            else
            {
                // Modo guardia: controles normales de captura
                BtnTomarFoto.IsVisible = true;
                PanelConfirmacion.IsVisible = false;
                BtnCerrar.IsVisible = false;
            }
        }

        private async void OnCerrarClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnTomarFotoClicked(object sender, EventArgs e)
        {
            try
            {
                var status = await Permissions.RequestAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlertAsync("Permiso requerido", "Se necesita permiso de cámara.", "OK");
                    return;
                }

                if (MediaPicker.Default.IsCaptureSupported)
                {
                    var photo = await MediaPicker.Default.CapturePhotoAsync();
                    if (photo != null)
                    {
                        using var stream = await photo.OpenReadAsync();
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        _fotoBytes = memoryStream.ToArray();

                        ImgEvidencia.Source = ImageSource.FromStream(() => new MemoryStream(_fotoBytes));

                        BtnTomarFoto.IsVisible = false;
                        PanelConfirmacion.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }

        private async void OnContinuarClicked(object sender, EventArgs e)
        {
            if (_fotoBytes != null && _localId > 0)
            {
                try
                {
                    await _offline.GuardarFotoPuntoAsync(_localId, _fotoBytes);
                    await Shell.Current.GoToAsync("..");
                }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("Error", $"No se pudo guardar la foto: {ex.Message}", "OK");
                }
            }
            else
            {
                await Shell.Current.GoToAsync("..");
            }
        }
    }
}