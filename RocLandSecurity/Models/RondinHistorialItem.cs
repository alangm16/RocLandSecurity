namespace RocLandSecurity.Models
{
    /// <summary>
    /// DTO para el historial del guardia.
    /// RondinID = -1 indica una entrada que solo tiene incidencias sin rondín asociado.
    /// </summary>
    public class RondinHistorialItem
    {
        public int       RondinID         { get; set; }
        public int       TurnoID          { get; set; }
        public DateTime  HoraProgramada   { get; set; }
        public DateTime? HoraInicio       { get; set; }
        public DateTime? HoraFin          { get; set; }
        public int       Estado           { get; set; }
        public int       PuntosVisitados  { get; set; }
        public int       PuntosTotal      { get; set; }
        public int       TotalIncidencias { get; set; }

        /// <summary>Incidencias vinculadas al rondín (RondinID conocido).</summary>
        public List<Incidencia> Incidencias { get; set; } = new();

        /// <summary>Incidencias reportadas fuera de rondín pero del mismo turno.</summary>
        public List<Incidencia> IncidenciasSinRondin { get; set; } = new();

        /// <summary>Todas las incidencias a mostrar en la tarjeta.</summary>
        public IEnumerable<Incidencia> TodasLasIncidencias =>
            Incidencias.Concat(IncidenciasSinRondin);

        public bool EsSoloIncidencias => RondinID == -1;

        public string DuracionStr
        {
            get
            {
                if (HoraInicio.HasValue && HoraFin.HasValue)
                    return $"{(int)(HoraFin.Value - HoraInicio.Value).TotalMinutes} min";
                return "--";
            }
        }
    }
}
