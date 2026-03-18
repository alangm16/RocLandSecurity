namespace RocLandSecurity
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Activa el TabBar del guardia y navega a su home.
        /// Llamado desde MainPage después de login exitoso con Rol = 0.
        /// </summary>
        public void MostrarTabBarGuardia()
        {
            TabBarGuardia.IsVisible = true;
            TabBarSupervisor.IsVisible = false;
            Shell.Current.CurrentItem = TabBarGuardia;
        }

        /// <summary>
        /// Activa el TabBar del supervisor y navega a su home.
        /// Llamado desde MainPage después de login exitoso con Rol = 1.
        /// </summary>
        public void MostrarTabBarSupervisor()
        {
            TabBarSupervisor.IsVisible = true;
            TabBarGuardia.IsVisible = false;
            Shell.Current.CurrentItem = TabBarSupervisor;
        }

        /// <summary>
        /// Cierra sesión: oculta ambos TabBars y regresa al login.
        /// Puede llamarse desde cualquier página con:
        ///   (Shell.Current as AppShell)?.Logout();
        /// </summary>
        public void Logout()
        {
            TabBarGuardia.IsVisible = false;
            TabBarSupervisor.IsVisible = false;
            Shell.Current.CurrentItem = LoginContent;
        }
    }
}
