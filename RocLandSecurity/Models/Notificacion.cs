using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    /// Tabla: TBL_ROCLAND_SECURITY_NOTIFICACIONES
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
            0 => "tab_rondin.png",
            1 => "tab_rondin.png",
            2 => "tab_incidencia.png",
            3 => "tab_incidencia.png",
            _ => "tab_panel.png"
        };

        public string FechaEnvioStr => FechaEnvio.ToString("dd/MM HH:mm");

        public string TiempoTranscurrido
        {
            get
            {
                var diff = DateTime.Now - FechaEnvio;
                if (diff.TotalMinutes < 1) return "Ahora";
                if (diff.TotalMinutes < 60) return $"Hace {(int)diff.TotalMinutes} min";
                if (diff.TotalHours < 24) return $"Hace {(int)diff.TotalHours} h";
                return FechaEnvioStr;
            }
        }
    }
}