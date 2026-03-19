using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// <summary>
    /// Representa una incidencia reportada por el guardia.
    /// Puede estar vinculada a un rondín y/o a un punto de control,
    /// o ser completamente general (RondinID y PuntoID nulos).
    /// Estado: 0 = Abierta, 1 = Resuelta
    /// </summary>
    public class Incidencia
    {
        public int ID { get; set; }
        public int TurnoID { get; set; }
        public int? RondinID { get; set; }   // null si se reportó fuera de rondín
        public int? PuntoID { get; set; }   // null si es incidencia general
        public int GuardiaReportaID { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string? FotoPath { get; set; }
        public DateTime FechaReporte { get; set; }
        public int Estado { get; set; }   // 0=Abierta 1=Resuelta
        public int? SupervisorResuelveID { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string? NotaResolucion { get; set; }
        public int? TurnoResolucionID { get; set; }

        // Datos relacionados (cargados con JOIN)
        public string NombreGuardia { get; set; } = string.Empty;
        public string NombrePunto { get; set; } = string.Empty; // vacío si no aplica
        public int? OrdenPunto { get; set; }

        // ── Propiedades calculadas para la UI ──────────────────────────
        public bool EsAbierta => Estado == 0;
        public bool EsResuelta => Estado == 1;

        public string EstadoTexto => Estado == 0 ? "Abierta" : "Resuelta";

        public string EstadoColor => Estado == 0 ? "#F09595" : "#97C459";

        public string FechaReporteStr =>
            FechaReporte.ToString("dd/MM/yyyy HH:mm");

        public string DiasAbiertaStr
        {
            get
            {
                if (EsResuelta) return "Resuelta";
                int dias = (DateTime.Now - FechaReporte).Days;
                return dias == 0 ? "Hoy" : $"Hace {dias} día{(dias == 1 ? "" : "s")}";
            }
        }

        public string UbicacionStr =>
            string.IsNullOrEmpty(NombrePunto) ? "General (sin punto)" : NombrePunto;
    }
}
