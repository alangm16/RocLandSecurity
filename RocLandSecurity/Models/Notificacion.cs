using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public enum TipoNotificacion
    {
        RondinProximo = 0,
        RondinPendiente = 1,
        RondinNoIniciado = 2,
        IncidenciaReportada = 3
    }
    class Notificacion
    {
        public int ID { get; set; }
        public int UsuarioID { get; set; }
        public int? RondinID { get; set; }
        public int? IncidenciaID { get; set; }
        public TipoNotificacion Tipo {  get; set; }
        public string Mensaje { get; set; }
        public DateTime FechaEnvio { get; set; }
        public bool Leida { get; set; }
        public DateTime? FechaLeida { get; set; }

        // Propiedades de navegacion
        public Usuario Usuario { get; set; }
        public Rondin Rondin { get; set; }
        public Incidencia Incidencia { get; set; }
    }
}
