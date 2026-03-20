namespace RocLandSecurity.Models
{
    /// <summary>
    /// DTO para mostrar el historial de un día específico para supervisor.
    /// </summary>
    public class HistorialSupervisorDia
    {
        public DateTime Fecha { get; set; }
        public string FechaStr => Fecha.ToString("dd/MM/yyyy");
        public string DiaSemana => Fecha.ToString("dddd", new System.Globalization.CultureInfo("es-MX"));

        public List<HistorialTurnoDia> Turnos { get; set; } = new();
        public bool TieneRegistros => Turnos.Count > 0;

        public string Titulo
        {
            get
            {
                if (Fecha.Date == DateTime.Today)
                    return $"HOY · {FechaStr}";
                if (Fecha.Date == DateTime.Today.AddDays(-1))
                    return $"AYER · {FechaStr}";
                return $"{DiaSemana.ToUpper()} · {FechaStr}";
            }
        }
    }

    /// <summary>
    /// Información de un turno específico en un día.
    /// </summary>
    public class HistorialTurnoDia
    {
        public int TurnoID { get; set; }
        public int GuardiaID { get; set; }
        public string NombreGuardia { get; set; } = string.Empty;
        public TimeOnly HoraInicio { get; set; }
        public TimeOnly HoraFin { get; set; }
        public string RangoHorario => $"{HoraInicio:HH:mm} – {HoraFin:HH:mm}";

        public List<HistorialRondinDia> Rondines { get; set; } = new();
        public List<IncidenciaSupervisorItem> Incidencias { get; set; } = new();

        public int TotalRondines => Rondines.Count;
        public int TotalIncidencias => Incidencias.Count;
        public int RondinesCompletados => Rondines.Count(r => r.Estado == 2 || r.Estado == 4);

        public double Cumplimiento => TotalRondines > 0
            ? (double)RondinesCompletados / TotalRondines * 100
            : 0;
    }

    /// <summary>
    /// Información de un rondín en el historial.
    /// </summary>
    public class HistorialRondinDia
    {
        public int ID { get; set; }
        public DateTime HoraProgramada { get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public int Estado { get; set; }
        public int PuntosVisitados { get; set; }
        public int PuntosTotal { get; set; }
        public int IncidenciasCount { get; set; }

        /// <summary>Incidencias vinculadas a este rondín, cargadas para mostrar descripción.</summary>
        public List<IncidenciaSupervisorItem> Incidencias { get; set; } = new();

        public string HoraStr => HoraProgramada.ToString("HH:mm");
        public string DuracionStr
        {
            get
            {
                if (HoraInicio.HasValue && HoraFin.HasValue)
                    return $"{(int)(HoraFin.Value - HoraInicio.Value).TotalMinutes} min";
                return "--";
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
        public bool TieneIncidencias => IncidenciasCount > 0;
    }
}