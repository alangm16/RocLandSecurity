using System;
using System.Collections.Generic;
using System.Text;
using RocLandSecurity.Services;

namespace RocLandSecurity.Views.Shared
{
    public partial class PerfilPage : ContentPage
    {
        private readonly SessionService _session;

        public PerfilPage(SessionService session)
        {
            InitializeComponent();
            _session = session;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarDatos();
        }

        private void CargarDatos()
        {
            var u = _session.UsuarioActual;
            if (u == null) return;

            // Iniciales para el avatar (ya calculadas en el modelo)
            LblIniciales.Text = u.Iniciales;

            LblNombreCompleto.Text = u.Nombre;
            LblUsuario.Text = u.UsuarioLogin;
            LblFechaCreacion.Text = u.FechaCreacion.ToString("dd/MM/yyyy");

            if (u.EsSupervisor())
            {
                LblRol.Text = "Supervisor";
                LblRolDetalle.Text = "Supervisor";
                BadgeRol.BackgroundColor = Color.FromArgb("#0a1a2e");
                BadgeRol.Stroke = Color.FromArgb("#185FA5");
                LblRol.TextColor = Color.FromArgb("#85B7EB");
            }
            else
            {
                LblRol.Text = "Guardia";
                LblRolDetalle.Text = "Guardia de seguridad";
                BadgeRol.BackgroundColor = Color.FromArgb("#0a1a0a");
                BadgeRol.Stroke = Color.FromArgb("#3B6D11");
                LblRol.TextColor = Color.FromArgb("#97C459");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirmar = await DisplayAlertAsync(
                "Cerrar sesión",
                "¿Estás seguro que deseas cerrar tu sesión?",
                "Sí, salir",
                "Cancelar");

            if (!confirmar) return;

            _session.CerrarSesion();
            await ((Shell.Current as AppShell)?.LogoutAsync() ?? Task.CompletedTask);
        }
    }
}