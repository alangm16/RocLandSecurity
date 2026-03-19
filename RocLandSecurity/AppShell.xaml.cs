namespace RocLandSecurity
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
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

            var loginPage = IPlatformApplication.Current?.Services
                .GetService<MainPage>();

            if (loginPage != null)
            {
                loginPage.ResetearVista();
                await Navigation.PushModalAsync(loginPage, animated: true);
            }
        }
    }
}