using RocLandSecurity.Views.Guardia;
using RocLandSecurity.Views.Supervisor;

namespace RocLandSecurity
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Rutas de navegación programática (no declaradas en XAML)
            Routing.RegisterRoute("rondinactivo", typeof(RondinActivoPage));
            Routing.RegisterRoute("reportarincidencia", typeof(ReportarIncidenciaPage));
            Routing.RegisterRoute("supervisorincidencias", typeof(SupervisorIncidenciasPage));
            Routing.RegisterRoute("admGuardias", typeof(admGuardias));
        }

        public async Task MostrarTabBarGuardiaAsync()
        {
            Current.CurrentItem = TabBarGuardia;
            if (TabBarGuardia.CurrentItem != TabBarGuardia.Items[0])
                TabBarGuardia.CurrentItem = TabBarGuardia.Items[0];
            await Navigation.PopModalAsync(animated: true);
        }

        public async Task MostrarTabBarSupervisorAsync()
        {
            Current.CurrentItem = TabBarSupervisor;
            if (TabBarSupervisor.CurrentItem != TabBarSupervisor.Items[0])
                TabBarSupervisor.CurrentItem = TabBarSupervisor.Items[0];
            await Navigation.PopModalAsync(animated: true);
        }

        public async Task LogoutAsync()
        {
            TabBarGuardia.CurrentItem = TabBarGuardia.Items[0];
            TabBarSupervisor.CurrentItem = TabBarSupervisor.Items[0];

            var loginPage = IPlatformApplication.Current?.Services.GetService<MainPage>();
            if (loginPage != null)
            {
                loginPage.ResetearVista();
                await Navigation.PushModalAsync(loginPage, animated: true);
            }
        }

        public async Task ManejarNotificacion(string type, int rondinId)
        {
            if (type == "inicio" && rondinId > 0)
            {
                // Navegar al rondín activo
                await Current.GoToAsync($"rondinactivo?rondinId={rondinId}");
            }
            else if (type == "fin" && rondinId > 0)
            {
                // Mostrar mensaje o navegar al rondín
                var page = Current.CurrentPage;
                if (page is Views.Guardia.GuardiaHomePage homePage)
                {
                    // Puedes mostrar un toast o navegar
                    await homePage.DisplayAlertAsync("Aviso", "El rondín está por finalizar", "OK");
                }
            }
        }
    }
}