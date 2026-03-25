using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_RONDINES
    public class Rondin
    {
        public int ID { get; set; }
        public int TurnoID { get; set; }
        public int GuardiaID { get; set; }
        public DateTime HoraProgramada { get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public int Estado { get; set; }

        public bool Sincronizado { get; set; }

        public DateTime FechaModificacion { get; set; }

        public int PuntosVisitados { get; set; }
        public int PuntosTotal { get; set; } = 20;

        public string NombreGuardia { get; set; } = string.Empty;

        public string HoraProgramadaStr => HoraProgramada.ToString("HH:mm");

        public int? DuracionMinutos =>
            HoraInicio.HasValue && HoraFin.HasValue
                ? (int)(HoraFin.Value - HoraInicio.Value).TotalMinutes
                : null;

        public string DuracionStr =>
            DuracionMinutos.HasValue ? $"{DuracionMinutos} min" : "--";

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
            0 => "#FAC775",
            1 => "#FFA500",
            2 => "#97C459",
            3 => "#F09595",
            4 => "#F09595",
            _ => "#888888"
        };

        public string EstadoColorFondo => Estado switch
        {
            0 => "#2a2400",
            1 => "#0a1a2e",
            2 => "#0a1a0a",
            3 => "#2a0a0a",
            4 => "#2a0a0a",
            _ => "#1a1a1a"
        };

        public string PuntosStr => $"{PuntosVisitados}/{PuntosTotal} pts";

        public bool EsIniciable => Estado == 0;
        public bool EstaEnProgreso => Estado == 1;
        public bool EstaFinalizado => Estado >= 2;

        public bool PendienteDeSincronizar => !Sincronizado;
        public int IncidenciasCount { get; set; } = 0;

        public bool TieneIncidencias => IncidenciasCount > 0 || Estado == 4;
    }
}