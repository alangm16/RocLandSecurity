using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class Usuario
    {
        public int ID { get; set; }
        public string Nombre { get; set; }
        public string UsuarioLogin { get; set; }
        public string Contraseña { get; set; }
        public string QRCode { get; set; }
        public int Rol { get; set; }
        public DateTime FechaCreacion {  get; set; }
        public bool Activo { get; set; }
    }
}
