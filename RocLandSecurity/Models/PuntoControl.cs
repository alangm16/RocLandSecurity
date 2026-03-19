using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_PUNTOSCONTROL
    public class PuntoControl
    {
        public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string QRCode { get; set; } = string.Empty;
        public int Orden { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }

        public bool TieneUbicacion => Latitud != 0 || Longitud != 0;

        public string UbicacionStr =>
            TieneUbicacion
                ? $"{Latitud:F6}, {Longitud:F6}"
                : "Sin coordenadas";
    }
}