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

    // Models/PuntoDetalleItem.cs
    public class PuntoDetalleItem
    {
        public int Orden { get; set; }
        public string Nombre { get; set; } = "";
        public int Estado { get; set; }   // 0=Pendiente,1=Visitado,2=Omitido
        public DateTime? HoraVisita { get; set; }
        public TimeSpan? Intervalo { get; set; }   // Tiempo desde el QR anterior visitado

        public string HoraStr => HoraVisita.HasValue ? HoraVisita.Value.ToString("HH:mm:ss") : "--:--";
        public string IntervaloStr => Intervalo.HasValue
            ? Intervalo.Value.TotalSeconds < 60
                ? $"+{(int)Intervalo.Value.TotalSeconds}s"
                : $"+{(int)Intervalo.Value.TotalMinutes}m {Intervalo.Value.Seconds:D2}s"
            : "";

        public string EstadoColor => Estado switch
        {
            1 => "#97C459",   // Visitado — verde
            2 => "#F09595",   // Omitido  — rojo
            _ => "#888888"    // Pendiente — gris
        };

        public string EstadoIcon => Estado switch
        {
            1 => "✓",
            2 => "✗",
            _ => "○"
        };
    }
}
