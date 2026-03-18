using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    class Turno
    {
        public int ID { get; set; }
        public DateTime Fecha { get; set; }
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFin { get; set; }
        public int GuardiaID { get; set; } 
        public int SupervisorID { get; set; }

        // Propiedades de navegación
        public Usuario Guardia { get; set; }
        public Usuario Supervisor { get; set; }
    }
}
