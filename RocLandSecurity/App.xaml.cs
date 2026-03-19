using RocLandSecurity.Services;

namespace RocLandSecurity
{
    public partial class App : Application
    {
        private readonly MainPage _loginPage;
        private readonly SessionService _session;

        public App(MainPage loginPage, SessionService session)
        {
            InitializeComponent();
            _loginPage = loginPage;
            _session = session;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // 1. El Shell (con los TabBars) es la raíz de la ventana
            var shell = new AppShell();
            var window = new Window(shell);

            // 2. Mostramos el login como modal encima del Shell
            //    El Shell queda vivo debajo pero tapado por el login.
            //    Cuando el login hace PopModalAsync, el Shell aparece
            //    ya con el TabBar correcto abajo.
            shell.Loaded += async (s, e) =>
            {
                await shell.Navigation.PushModalAsync(_loginPage, animated: false);
            };

            return window;
        }
    }
}