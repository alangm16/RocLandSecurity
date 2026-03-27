namespace RocLandSecurity.Models
{
    public class IncidenciaResumen
    {
        public int ID { get; set; }
        public string Descripcion { get; set; } = "";
        public DateTime FechaReporte { get; set; }
        public int Estado { get; set; } 
        public string? NotaResolucion { get; set; }
        public string NombrePunto { get; set; } = "";
        public bool TieneFoto { get; set; }
    }
}
