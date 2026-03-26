using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class DetalleRondinSupervisor
    {
        public int RondinID { get; set; }
        public int TurnoID { get; set; }
        public DateTime HoraProgramada { get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public int Estado { get; set; }
        public string NombreGuardia { get; set; } = "";
        public int PuntosVisitados { get; set; }
        public int PuntosTotal { get; set; }
        public int TotalIncidencias { get; set; }
        public List<PuntoDetalleItem> Puntos { get; set; } = new();
        public List<IncidenciaResumen> Incidencias { get; set; } = new();

        // Helpers de presentación
        public string DuracionStr
        {
            get
            {
                if (HoraInicio == null || HoraFin == null) return "--";
                var d = HoraFin.Value - HoraInicio.Value;
                return d.TotalMinutes < 60
                    ? $"{(int)d.TotalMinutes} min"
                    : $"{(int)d.TotalHours}h {d.Minutes:D2}m";
            }
        }

        public string EstadoTexto => Estado switch
        {
            0 => "Pendiente",
            1 => "En progreso",
            2 => "Completado",
            3 => "Incompleto",
            4 => "Con incidencia",
            _ => "Desconocido"
        };

        public string EstadoColor => Estado switch
        {
            0 => "#888888",
            1 => "#FAC775",
            2 => "#97C459",
            3 => "#F09595",
            4 => "#FF8C00",
            _ => "#888888"
        };
    }
}
