using RocLandSecurity.Models;
using RocLandSecurity.Services;
using System.Collections.ObjectModel;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class AdminUsuarios : ContentPage
    {
        private readonly SupervisorDatabaseService _db;
        public ObservableCollection<Usuario> Usuarios { get; } = new();

        // Usuario que está siendo editado/eliminado
        private Usuario? _usuarioSeleccionado;

        private string _textoBusqueda = string.Empty;
        public string TextoBusqueda
        {
            get => _textoBusqueda;
            set
            {
                if (_textoBusqueda != value)
                {
                    _textoBusqueda = value;
                    OnPropertyChanged();
                    _ = BuscarUsuariosAsync();
                }
            }
        }

        public AdminUsuarios(SupervisorDatabaseService db)
        {
            InitializeComponent();
            _db = db;
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarDatosAsync();
        }

        // ── CARGA / BÚSQUEDA ─────────────────────────────────────────────

        private async Task CargarDatosAsync()
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            Usuarios.Clear();
            var lista = await _db.GetUsuarios();
            foreach (var u in lista) Usuarios.Add(u);

            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;

            ActualizarSubtitulo();
        }

        public async Task BuscarUsuariosAsync()
        {
            Usuarios.Clear();
            var lista = await _db.GetUsuarios(TextoBusqueda);
            foreach (var u in lista) Usuarios.Add(u);
            ActualizarSubtitulo();
        }

        private void ActualizarSubtitulo()
        {
            int total = Usuarios.Count;
            int activos = Usuarios.Count(u => u.Activo);
            LblSubtitulo.Text = $"{total} registrados · {activos} activos";
        }

        // ── HELPERS DE MODAL ─────────────────────────────────────────────

        private void AbrirModal(Border modal)
        {
            ModalOverlay.IsVisible = true;
            modal.IsVisible = true;
        }

        private void CerrarTodosLosModales()
        {
            ModalAgregar.IsVisible = false;
            ModalEditar.IsVisible = false;
            ModalEliminar.IsVisible = false;
            ModalOverlay.IsVisible = false;
            _usuarioSeleccionado = null;
        }

        private void OnCerrarModal(object sender, EventArgs e) => CerrarTodosLosModales();

        // ── MODAL AGREGAR ────────────────────────────────────────────────

        private void OnAgregarUsuarios(object sender, EventArgs e)
        {
            // Limpiar campos
            EntryAgregarNombre.Text = string.Empty;
            EntryAgregarUsuario.Text = string.Empty;
            EntryAgregarContrasena.Text = string.Empty;
            EntryAgregarQR.Text = string.Empty;

            AbrirModal(ModalAgregar);
        }

        private async void OnConfirmarAgregar(object sender, EventArgs e)
        {
            string nombre = EntryAgregarNombre.Text?.Trim() ?? string.Empty;
            string usuario = EntryAgregarUsuario.Text?.Trim() ?? string.Empty;
            string contrasena = EntryAgregarContrasena.Text?.Trim() ?? string.Empty;
            string? qr = string.IsNullOrWhiteSpace(EntryAgregarQR.Text)
                                ? null : EntryAgregarQR.Text.Trim();

            // Validación básica
            if (string.IsNullOrWhiteSpace(nombre) ||
                string.IsNullOrWhiteSpace(usuario) ||
                string.IsNullOrWhiteSpace(contrasena))
            {
                await ShowToastAsync("Nombre, usuario y contraseña son obligatorios.", isError: true);
                return;
            }

            if (contrasena.Length < 8)
            {
                await ShowToastAsync("La contraseña debe tener al menos 8 caracteres.", isError: true);
                return;
            }

            try
            {
                int nuevoId = await _db.AgregarGuardiaAsync(nombre, usuario, contrasena, qr);

                // Agregar directamente a la lista sin recargar todo
                var nuevo = new Usuario
                {
                    ID = nuevoId,
                    Nombre = nombre,
                    UsuarioLogin = usuario,
                    Contraseña = string.Empty, // no exponemos hash en UI
                    QRCode = qr,
                    Rol = 0,
                    FechaCreacion = DateTime.Now,
                    Activo = true
                };
                Usuarios.Add(nuevo);
                ActualizarSubtitulo();

                CerrarTodosLosModales();
                await ShowToastAsync("Guardia agregado correctamente.", isError: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al agregar guardia: {ex.Message}");
                await ShowToastAsync("Error al guardar. Verifica los datos.", isError: true);
            }
        }

        // ── MODAL EDITAR ─────────────────────────────────────────────────

        private void OnEditarUsuario(object sender, EventArgs e)
        {
            if (sender is Image img && img.BindingContext is Usuario usuario)
            {
                _usuarioSeleccionado = usuario;

                EntryEditarNombre.Text = usuario.Nombre;
                EntryEditarUsuario.Text = usuario.UsuarioLogin;
                EntryEditarContrasena.Text = string.Empty;
                EntryEditarQR.Text = usuario.QRCode ?? string.Empty;

                AbrirModal(ModalEditar);
            }
        }


        private async void OnConfirmarEditar(object sender, EventArgs e)
        {
            if (_usuarioSeleccionado is null) return;

            string nombre = EntryEditarNombre.Text?.Trim() ?? string.Empty;
            string usuario = EntryEditarUsuario.Text?.Trim() ?? string.Empty;
            string contrasena = EntryEditarContrasena.Text?.Trim() ?? string.Empty;
            string? qr = string.IsNullOrWhiteSpace(EntryEditarQR.Text)
                                ? null : EntryEditarQR.Text.Trim();

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(usuario))
            {
                await ShowToastAsync("Nombre y usuario son obligatorios.", isError: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(contrasena) && contrasena.Length < 6)
            {
                await ShowToastAsync("La contraseña debe tener al menos 6 caracteres.", isError: true);
                return;
            }

            try
            {
                await _db.EditarGuardiaAsync(
                    _usuarioSeleccionado.ID,
                    nombre,
                    usuario,
                    string.IsNullOrWhiteSpace(contrasena) ? null : contrasena,
                    qr);

                // Actualizar modelo en memoria
                _usuarioSeleccionado.Nombre = nombre;
                _usuarioSeleccionado.UsuarioLogin = usuario;
                _usuarioSeleccionado.QRCode = qr;

                // Forzar refresco visual del item
                int idx = Usuarios.IndexOf(_usuarioSeleccionado);
                if (idx >= 0)
                {
                    Usuarios.RemoveAt(idx);
                    Usuarios.Insert(idx, _usuarioSeleccionado);
                }

                CerrarTodosLosModales();
                await ShowToastAsync("Guardia actualizado correctamente.", isError: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al editar guardia: {ex.Message}");
                await ShowToastAsync("Error al actualizar. Intenta de nuevo.", isError: true);
            }
        }

        // ── MODAL ELIMINAR ───────────────────────────────────────────────

        private void OnEliminarUsuario(object sender, EventArgs e)
        {
            if (sender is Image img && img.BindingContext is Usuario usuario)
            {
                _usuarioSeleccionado = usuario;
                LblEliminarMensaje.Text =
                    $"¿Estás seguro de eliminar a {usuario.Nombre}? Esta acción no se puede deshacer.";
                AbrirModal(ModalEliminar);
            }
        }

        private async void OnConfirmarEliminar(object sender, EventArgs e)
        {
            if (_usuarioSeleccionado is null) return;

            try
            {
                await _db.EliminarGuardiaAsync(_usuarioSeleccionado.ID);

                Usuarios.Remove(_usuarioSeleccionado);
                ActualizarSubtitulo();

                CerrarTodosLosModales();
                await ShowToastAsync("Guardia eliminado correctamente.", isError: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al eliminar guardia: {ex.Message}");
                await ShowToastAsync("Error al eliminar. Intenta de nuevo.", isError: true);
            }
        }

        // ── TOAST ────────────────────────────────────────────────────────

        private async Task ShowToastAsync(string message, bool isError = true)
        {
            ToastLabel.Text = message;
            ToastFrame.BackgroundColor = isError
                ? Color.FromArgb("#FF5555")
                : Color.FromArgb("#6DBF2E");
            ToastFrame.IsVisible = true;
            ToastFrame.Opacity = 0;

            await ToastFrame.FadeToAsync(1, 200);
            await Task.Delay(2500);
            await ToastFrame.FadeToAsync(0, 200);
            ToastFrame.IsVisible = false;
        }
    }
}