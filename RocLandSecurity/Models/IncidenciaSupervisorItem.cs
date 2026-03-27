namespace RocLandSecurity.Models
{
    /// <summary>
    /// DTO para mostrar incidencias en la vista de supervisor.
    /// Agrupa incidencias por día de la semana.
    /// </summary>
    public class IncidenciaSupervisorItem
    {
        public int ID { get; set; }
        public int TurnoID { get; set; }
        public int? RondinID { get; set; }
        public int? PuntoID { get; set; }
        public int GuardiaReportaID { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaReporte { get; set; }
        public int Estado { get; set; }
        public string NombreGuardia { get; set; } = string.Empty;
        public string NombrePunto { get; set; } = string.Empty;
        public int? OrdenPunto { get; set; }
        public string? NotaResolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public int? SupervisorResuelveID { get; set; }

        public bool EsAbierta => Estado == 0;
        public bool EsResuelta => Estado == 1;

        public string EstadoTexto => Estado == 0 ? "Abierta" : "Resuelta";
        public string EstadoColor => Estado == 0 ? "#F09595" : "#97C459";
        public string EstadoColorFondo => Estado == 0 ? "#2a0a0a" : "#0a1a0a";

        public string HoraStr => FechaReporte.ToString("HH:mm");
        public string FechaStr => FechaReporte.ToString("dd/MM/yyyy");

        public string UbicacionStr => string.IsNullOrEmpty(NombrePunto)
            ? "Sin ubicación específica"
            : NombrePunto;

        public bool TieneFoto { get; set; }
    }

    /// <summary>
    /// Agrupación de incidencias por día de la semana.
    /// </summary>
    public class IncidenciasPorDia
    {
        public DateTime Fecha { get; set; }
        public string DiaSemana { get; set; } = string.Empty;
        public string FechaStr => Fecha.ToString("dd/MM/yyyy");
        public List<IncidenciaSupervisorItem> Incidencias { get; set; } = new();
        public int Total => Incidencias.Count;

        public string TituloDia
        {
            get
            {
                if (Fecha.Date == DateTime.Today)
                    return $"HOY · {Fecha:dd/MM/yyyy}";
                if (Fecha.Date == DateTime.Today.AddDays(-1))
                    return $"AYER · {Fecha:dd/MM/yyyy}";
                return $"{DiaSemana.ToUpper()} · {Fecha:dd/MM/yyyy}";
            }
        }
    }
}
