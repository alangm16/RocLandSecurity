using RocLandSecurity.Services;

namespace RocLandSecurity
{
    public partial class MainPage : ContentPage
    {
        private readonly DatabaseService db;
        public MainPage(DatabaseService databaseService)
        {
            InitializeComponent();
            db = databaseService;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string usuario = UsuarioEntry.Text?.Trim();
            string contrasena = ContrasenaEntry.Text?.Trim();

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(contrasena))
            {
                await DisplayAlert("Error", "Ingrese usuario y contraseña", "OK");
                return;
            }

            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(contrasena));
            string hashContrasena = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            var user = await db.GetUsuarioByLoginAsync(usuario, hashContrasena);

            if (user != null)
                await DisplayAlert("Éxito", $"Bienvenido {user.Nombre}", "OK");
            else
                await DisplayAlert("Error", "Usuario o contraseña incorrectos", "OK");
        }
    }
}
