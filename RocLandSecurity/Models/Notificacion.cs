using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// <summary>
    /// Notificación enviada a un usuario.
    /// Tipo: 0=Rondín próximo, 1=Rondín pendiente, 2=Rondín no iniciado, 3=Incidencia reportada
    /// </summary>
    public class Notificacion
    {
        public int ID { get; set; }
        public int UsuarioID { get; set; }
        public int? RondinID { get; set; }
        public int? IncidenciaID { get; set; }
        public int Tipo { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public DateTime FechaEnvio { get; set; }
        public bool Leida { get; set; }
        public DateTime? FechaLeida { get; set; }

        // ── Propiedades calculadas para la UI ──────────────────────────
        public string TipoTexto => Tipo switch
        {
            0 => "Rondín próximo",
            1 => "Rondín pendiente",
            2 => "Rondín no iniciado",
            3 => "Incidencia reportada",
            _ => "Notificación"
        };

        public string TipoIcono => Tipo switch
        {
            0 => "🔔",
            1 => "⏰",
            2 => "⚠️",
            3 => "🚨",
            _ => "📢"
        };

        public string FechaEnvioStr =>
            FechaEnvio.ToString("dd/MM HH:mm");
    }
}
