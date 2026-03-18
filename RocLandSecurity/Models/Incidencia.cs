using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public enum EstadoIncidencia
    {
        Abierta = 0,
        Resuelta = 1
    }
    class Incidencia
    {
        public int ID { get; set; }
        public int TurnoID { get; set; }
        public int? RondinID { get; set; }
        public int? PuntoID { get; set; }
        public int GuardiaReportaID { get; set; }
        public string Descripcion { get; set; }
        public string FotoPath { get; set; }
        public DateTime FechaReporte { get; set; }
        public EstadoIncidencia Estado { get; set; }

        // Resolucion
        public int? SupervisorResuelveID { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string? NotaResolucion { get; set; }
        public int? TurnoResolucionID { get; set; }
        
        // Propiedades de navegacion
        public Turno Turno { get; set; }
        public Usuario GuardiaReporta { get; set; }
        public Rondin Rondin { get; set; }
        public PuntoControl Punto { get; set; }
        public Usuario SupervisorResuelve { get; set; }
        public Turno TurnoResolucion { get; set; }
    }
}
