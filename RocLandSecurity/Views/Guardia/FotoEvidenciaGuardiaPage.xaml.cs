using RocLandSecurity.Services;
using System.IO;

namespace RocLandSecurity.Views.Guardia
{
    /// <summary>
    /// Página exclusiva del GUARDIA para capturar la foto de evidencia de un punto.
    /// Solo usa OfflineDatabaseService (local + sync).
    /// Se navega desde RondinActivoPage vía Shell:
    ///   Shell.Current.GoToAsync($"fotoevidenciaguardia?localId={puntoLocal.LocalID}")
    /// </summary>
    [QueryProperty(nameof(LocalId), "localId")]
    public partial class FotoEvidenciaGuardiaPage : ContentPage
    {
        public string? LocalId { get; set; }

        private int _localId;
        private byte[]? _fotoBytes;
        private readonly OfflineDatabaseService _offline;

        public FotoEvidenciaGuardiaPage(OfflineDatabaseService offline)
        {
            InitializeComponent();
            _offline = offline;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Parsear el localId recibido por QueryProperty
            if (int.TryParse(LocalId, out var lid))
                _localId = lid;

            // Estado inicial limpio: sin foto, lista para capturar
            ImgEvidencia.Source = null;
            LblSinFoto.IsVisible = true;
            BtnTomarFoto.IsVisible = true;
            PanelConfirmacion.IsVisible = false;
            _fotoBytes = null;
        }

        private async void OnTomarFotoClicked(object sender, EventArgs e)
        {
            try
            {
                var status = await Permissions.RequestAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permiso requerido", "Se necesita permiso de cámara.", "OK");
                    return;
                }

                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    await DisplayAlert("No disponible", "Este dispositivo no soporta captura de fotos.", "OK");
                    return;
                }

                var photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo == null) return;

                using var stream = await photo.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                _fotoBytes = memoryStream.ToArray();

                ImgEvidencia.Source = ImageSource.FromStream(() => new MemoryStream(_fotoBytes));
                LblSinFoto.IsVisible = false;
                BtnTomarFoto.IsVisible = false;
                PanelConfirmacion.IsVisible = true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async void OnContinuarClicked(object sender, EventArgs e)
        {
            if (_fotoBytes != null && _localId > 0)
            {
                try
                {
                    await _offline.GuardarFotoPuntoAsync(_localId, _fotoBytes);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo guardar la foto: {ex.Message}", "OK");
                }
            }
            // Siempre volver, con o sin foto guardada
            await Shell.Current.GoToAsync("..");
        }
    }
}
