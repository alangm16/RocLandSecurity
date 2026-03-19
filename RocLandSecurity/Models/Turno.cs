using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_TURNOS
    public class Turno
    {
        public int ID { get; set; }
        public DateOnly Fecha { get; set; }
        public TimeOnly HoraInicio { get; set; }
        public TimeOnly HoraFin { get; set; }
        public int GuardiaID { get; set; }
        public int? SupervisorID { get; set; }

        public string NombreGuardia { get; set; } = string.Empty;
        public string NombreSupervisor { get; set; } = string.Empty;

        public string FechaStr => Fecha.ToString("dd/MM/yyyy");
        public string HoraInicioStr => HoraInicio.ToString("HH:mm");
        public string HoraFinStr => HoraFin.ToString("HH:mm");
        public string RangoHorario => $"{HoraInicioStr} – {HoraFinStr}";
    }
}