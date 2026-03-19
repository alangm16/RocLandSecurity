using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// <summary>
    /// Representa un punto de control dentro de un rondín específico.
    /// Estado: 0 = Pendiente, 1 = Visitado, 2 = Omitido
    /// </summary>
    public class RondinPunto
    {
        public int ID { get; set; }
        public int RondinID { get; set; }
        public int PuntoID { get; set; }
        public DateTime? HoraVisita { get; set; }
        public int Estado { get; set; }  // 0=Pendiente 1=Visitado 2=Omitido

        // Datos del punto (se cargan con JOIN, no son FK directas)
        public string NombrePunto { get; set; } = string.Empty;
        public int OrdenPunto { get; set; }

        // ── Propiedades calculadas para la UI ──────────────────────────
        public bool EsPendiente => Estado == 0;
        public bool EsVisitado => Estado == 1;
        public bool EsOmitido => Estado == 2;

        public string HoraVisitaStr =>
            HoraVisita.HasValue ? HoraVisita.Value.ToString("HH:mm") : "--";

        public string EstadoTexto => Estado switch
        {
            0 => "Pendiente",
            1 => "Visitado",
            2 => "Omitido",
            _ => "Desconocido"
        };

        public string EstadoColor => Estado switch
        {
            0 => "#FAC775",  // Amarillo
            1 => "#97C459",  // Verde
            2 => "#F09595",  // Rojo
            _ => "#888888"
        };
    }
}
