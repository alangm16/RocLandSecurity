namespace RocLandSecurity.Views.Guardia
{
    public partial class FotoEvidenciaPage : ContentPage
    {
        public FotoEvidenciaPage()
        {
            InitializeComponent();
        }

        private async void OnTomarFotoClicked(object sender, EventArgs e)
        {
            try
            {
                // 1. Verificar/Solicitar permisos manualmente para asegurar compatibilidad en Android 11
                var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.StorageWrite>();
                }

                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlertAsync("Permiso Requerido", "Se necesita permiso de almacenamiento para guardar la foto temporalmente.", "OK");
                    return;
                }

                // 2. Ejecutar captura
                if (MediaPicker.Default.IsCaptureSupported)
                {
                    FileResult photo = await MediaPicker.Default.CapturePhotoAsync();

                    if (photo != null)
                    {
                        var stream = await photo.OpenReadAsync();
                        ImgEvidencia.Source = ImageSource.FromStream(() => stream);

                        BtnTomarFoto.IsVisible = false;
                        PanelConfirmacion.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Esto evitará que la app se cierre si ocurre otro error
                await DisplayAlertAsync("Error de Cámara", ex.Message, "OK");
            }
        }

        private async void OnContinuarClicked(object sender, EventArgs e)
        {
            // Aquí podrías guardar la foto localmente o en BD antes de volver
            await Shell.Current.GoToAsync("..");
        }
    }
}