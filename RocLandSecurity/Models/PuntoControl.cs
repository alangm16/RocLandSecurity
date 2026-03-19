using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class PuntoControl
    {
        public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string QRCode { get; set; } = string.Empty;
        public int Orden { get; set; }

        // Propiedades de navegacion
        public ICollection<Incidencia> Incidencias { get; set; }
    }
}
