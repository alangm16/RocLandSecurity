using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_RONDINESPUNTOS
    public class RondinPunto
    {
        public int ID { get; set; }
        public int RondinID { get; set; }
        public int PuntoID { get; set; }
        public DateTime? HoraVisita { get; set; }
        public int Estado { get; set; }
        public double? LatitudG { get; set; }
        public double? LongitudG { get; set; }
        public byte[]? FotoPath { get; set; }
        public bool Sincronizado { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string NombrePunto { get; set; } = string.Empty;
        public int OrdenPunto { get; set; }
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
            0 => "#FAC775",
            1 => "#97C459",
            2 => "#F09595",
            _ => "#888888"
        };

        public bool TieneUbicacionEscaneo => LatitudG.HasValue && LongitudG.HasValue;

        public string UbicacionEscaneoStr =>
            TieneUbicacionEscaneo
                ? $"{LatitudG:F6}, {LongitudG:F6}"
                : "Sin GPS";

        public string SincronizadoStr => Sincronizado ? "Sincronizado" : "Pendiente de sync";
    }
}