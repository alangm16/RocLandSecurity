using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    class PuntoControl
    {
        public int ID { get; set; }
        public string Nombre { get; set; }
        public string QRCode { get; set; }
        public int Orden { get; set; }

        // Propiedades de navegacion
        public ICollection<Incidencia> Incidencias { get; set; }
    }
}
