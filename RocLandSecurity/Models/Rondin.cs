using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    public class Rondin
    {
        public int ID { get; set; }
        public int TurnoID { get; set; }
        public int GuardiaID { get; set; }
        public DateTime HoraProgramada {  get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public int Estado { get; set; } // 0= Pendiente, 1 = En Progreso, 2 = Completado, 3 = Incompleto, 4 = Incidencia

        // Propiedades de navegación
        public Turno Turno { get; set; }
        public Usuario Guardia { get; set; }

        // Puntos
        public int PuntosVisitados { get; set; }
        public int PuntosTotal { get; set; } = 20;

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

        // Colores según el esquema definido en el diseño
        public string EstadoColor => Estado switch
        {
            0 => "#FAC775",  // Amarillo — pendiente
            1 => "#85B7EB",  // Azul     — en progreso
            2 => "#97C459",  // Verde    — completado
            3 => "#F09595",  // Rojo     — incompleto
            4 => "#F09595",  // Rojo     — con incidencia
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

        public bool EsInicialbe => Estado == 0;
        public bool EstaEnProgreso => Estado == 1;
        public bool EstaFinalizado => Estado >= 2;
    }
}
