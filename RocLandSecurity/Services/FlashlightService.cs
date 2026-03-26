namespace RocLandSecurity.Services
{
    public interface IFlashlightService
    {
        Task<bool> IsAvailableAsync();
        Task TurnOnAsync();
        Task TurnOffAsync();
        Task ToggleAsync();
        bool IsOn { get; }
    }

    public class FlashlightService : IFlashlightService
    {
        private bool _isOn = false;

        public bool IsOn => _isOn;

        public async Task<bool> IsAvailableAsync()
        {
            return await Flashlight.IsSupportedAsync();
        }

        public async Task TurnOnAsync()
        {
            try
            {
                await Flashlight.TurnOnAsync();
                _isOn = true;
            }
            catch (FeatureNotSupportedException)
            {
                throw new Exception("La linterna no es compatible con este dispositivo");
            }
            catch (PermissionException)
            {
                throw new Exception("Permiso de cámara no concedido");
            }
            catch (Exception)
            {
                throw new Exception("No se pudo encender la linterna");
            }
        }

        public async Task TurnOffAsync()
        {
            try
            {
                await Flashlight.TurnOffAsync();
                _isOn = false;
            }
            catch (Exception)
            {
                throw new Exception("No se pudo apagar la linterna");
            }
        }

        public async Task ToggleAsync()
        {
            if (_isOn)
                await TurnOffAsync();
            else
                await TurnOnAsync();
        }
    }
}
