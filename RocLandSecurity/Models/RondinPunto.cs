using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public enum EstadoVisitaPunto
    {
        Pendiente = 0,
        Visitado = 1,
        Omitido = 2
    }
    class RondinPunto
    {
        public int ID { get; set; }
        public int RondinID { get; set; }
        public int PuntoID { get; set; }
        public DateTime? HoraVisita { get; set; }
        public EstadoVisitaPunto Estado { get; set; }

        // Propiedades de Navegacion
        public Rondin Rondin { get; set; }
        public PuntoControl Punto { get; set; }

    }
}
