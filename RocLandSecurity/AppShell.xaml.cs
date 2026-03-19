namespace RocLandSecurity
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Activa el TabBar del guardia y cierra el modal de login.
        /// Llamado desde MainPage tras login exitoso con Rol = 0.
        /// </summary>
        public async Task MostrarTabBarGuardiaAsync()
        {
            Current.CurrentItem = TabBarGuardia;
            await Navigation.PopModalAsync(animated: true);
        }

        /// <summary>
        /// Activa el TabBar del supervisor y cierra el modal de login.
        /// Llamado desde MainPage tras login exitoso con Rol = 1.
        /// </summary>
        public async Task MostrarTabBarSupervisorAsync()
        {
            Current.CurrentItem = TabBarSupervisor;
            await Navigation.PopModalAsync(animated: true);
        }

        /// <summary>
        /// Cierra sesión: vuelve a mostrar el login como modal.
        /// Llamado desde PerfilPage con:
        ///   (Shell.Current as AppShell)?.LogoutAsync();
        /// </summary>
        public async Task LogoutAsync()
        {
            // Resolver MainPage desde el contenedor de DI
            var loginPage = IPlatformApplication.Current?.Services
                .GetService<MainPage>();

            if (loginPage != null)
                await Navigation.PushModalAsync(loginPage, animated: true);
        }
    }
}