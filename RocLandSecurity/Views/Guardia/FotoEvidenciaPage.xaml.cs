using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Guardia
{
    [QueryProperty(nameof(LocalId), "localId")]
    public partial class FotoEvidenciaPage : ContentPage
    {   
        public string LocalId { get; set; }

        private int _localId;
        private byte[]? _fotoBytes;
        private readonly OfflineDatabaseService _offline;

        public FotoEvidenciaPage(OfflineDatabaseService offline)
        {
            InitializeComponent();
            _offline = offline;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (int.TryParse(LocalId, out var id))
                _localId = id;
            else
                _localId = 0;
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

                        // Mostrar la imagen
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