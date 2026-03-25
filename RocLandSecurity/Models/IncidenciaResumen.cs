using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class IncidenciaResumen
    {
        public int ID { get; set; }
        public string Descripcion { get; set; } = "";
        public DateTime FechaReporte { get; set; }
        public int Estado { get; set; } // 0=Abierta, 1=Resuelta
        public string? NotaResolucion { get; set; }
    }
}
