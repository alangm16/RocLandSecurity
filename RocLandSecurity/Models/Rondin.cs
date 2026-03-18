using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Models
{
    // Estados del Rondin
    public enum EstadoRondin
    {
        Pendiente = 0,        // Aún no iniciado
        EnProgreso = 1,       // Iniciado, no finalizado
        Completado = 2,       // Todos los puntos visitados
        Incompleto = 3,       // Finalizado con puntos sin visitar
        ConIncidencia = 4     // Completado pero hay incidencias
    }

    class Rondin
    {
        public int ID { get; set; }
        public int TurnoId { get; set; }
        public int GuardiaID { get; set; }
        public DateTime HoraProgramada {  get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public EstadoRondin Estado { get; set; }

        // Propiedades de navegación
        public Turno Turno { get; set; }
        public Usuario Guardia { get; set; }
    }
}
