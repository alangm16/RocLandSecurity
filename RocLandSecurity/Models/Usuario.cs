using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_USUARIOS
    public class Usuario
    {
        public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UsuarioLogin { get; set; } = string.Empty;
        public string Contraseña { get; set; } = string.Empty;
        public string? QRCode { get; set; }
        public int Rol { get; set; }   // 0 = Guardia, 1 = Supervisor
        public DateTime FechaCreacion { get; set; }
        public bool Activo { get; set; }

        public bool EsGuardia() => Rol == 0;
        public bool EsSupervisor() => Rol == 1;
        public string RolTexto => Rol == 1 ? "Supervisor" : "Guardia";

        public string Iniciales
        {
            get
            {
                var partes = Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return partes.Length >= 2
                    ? $"{partes[0][0]}{partes[1][0]}".ToUpper()
                    : Nombre[..Math.Min(2, Nombre.Length)].ToUpper();
            }
        }

        public bool EsActivo => Activo;
        public bool EsInactivo => !Activo;

        public string Codigo => $"G--{ID:D3}";

        public string FechaIngreso => FechaCreacion.ToString("dd/MM/yyyy");
    }
}