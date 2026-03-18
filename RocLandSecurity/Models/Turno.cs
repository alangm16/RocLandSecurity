using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class Turno
    {
        public int ID { get; set; }
        public DateOnly Fecha { get; set; }
        public TimeOnly HoraInicio { get; set; }
        public TimeOnly HoraFin { get; set; }
        public int GuardiaID { get; set; } 
        public int? SupervisorID { get; set; }

        // Propiedades de navegación
        public Usuario Guardia { get; set; }
        public Usuario Supervisor { get; set; }
    }
}
