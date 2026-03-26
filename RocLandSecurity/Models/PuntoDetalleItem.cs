namespace RocLandSecurity.Models
{
    public class PuntoDetalleItem
    {
        public int RondinPuntoID { get; set; }           // Nuevo: ID del registro en RONDINESPUNTOS
        public int Orden { get; set; }
        public string Nombre { get; set; } = "";
        public int Estado { get; set; }   // 0=Pendiente,1=Visitado,2=Omitido
        public DateTime? HoraVisita { get; set; }
        public TimeSpan? Intervalo { get; set; }   // Tiempo desde el QR anterior visitado
        public byte[]? FotoBytes { get; set; }     // Nuevo: bytes de la foto

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
