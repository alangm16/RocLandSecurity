using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{

    /// Mantiene al usuario autenticado en memoria durante la sesión.
    /// Registrado como Singleton en MauiProgram.cs.
    public class SessionService
    {
        public Usuario? UsuarioActual { get; private set; }

        public bool EstaAutenticado => UsuarioActual != null;
        public bool EsGuardia => UsuarioActual?.Rol == 0;
        public bool EsSupervisor => UsuarioActual?.Rol == 1;

        public void IniciarSesion(Usuario usuario)
        {
            UsuarioActual = usuario;
        }

        public void CerrarSesion()
        {
            UsuarioActual = null;
        }
    }
}
