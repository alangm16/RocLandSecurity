using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class Usuario
    {
        public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UsuarioLogin { get; set; } = string.Empty;
        public string Contraseña { get; set; } = string.Empty;
        public string QRCode { get; set; }
        public int Rol { get; set; } // 0 = Guardia, 1 = Supervisor;
        public DateTime FechaCreacion {  get; set; }
        public bool Activo { get; set; }

        // Propiedades de navegacion
        public bool EsGuardia() => Rol == 0;
        public bool EsSupervisor() => Rol == 1;
        public string RolTexto => Rol == 1 ? "Supervisor" : "Guardia";
    }
}
