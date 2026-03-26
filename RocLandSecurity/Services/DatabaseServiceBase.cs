using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    // Clase base con el connection string y los mappers compartidos.
    public abstract class DatabaseServiceBase
    {
        protected readonly string ConnectionString;

        protected DatabaseServiceBase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        // Expone el connection string para SyncService.
        public string GetConnectionString() => ConnectionString;

        // ── MAPPERS ──────────────────────────────────────────────────────

        protected static Usuario MapUsuario(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            Nombre = r.GetString(1),
            UsuarioLogin = r.GetString(2),
            Contraseña = r.GetString(3),
            QRCode = r.IsDBNull(4) ? null : r.GetString(4),
            Rol = r.GetInt32(5),
            FechaCreacion = r.GetDateTime(6),
            Activo = r.GetBoolean(7)
        };

        protected static Turno MapTurno(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            Fecha = DateOnly.FromDateTime(r.GetDateTime(1)),
            HoraInicio = TimeOnly.FromTimeSpan(r.GetTimeSpan(2)),
            HoraFin = TimeOnly.FromTimeSpan(r.GetTimeSpan(3)),
            GuardiaID = r.GetInt32(4)
        };

        protected static Rondin MapRondin(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            TurnoID = r.GetInt32(1),
            GuardiaID = r.GetInt32(2),
            HoraProgramada = r.GetDateTime(3),
            HoraInicio = r.IsDBNull(4) ? null : r.GetDateTime(4),
            HoraFin = r.IsDBNull(5) ? null : r.GetDateTime(5),
            Estado = r.GetInt32(6),
            PuntosVisitados = r.GetInt32(7),
            PuntosTotal = r.GetInt32(8)
        };

        protected static Rondin MapRondinConIncidencias(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            TurnoID = r.GetInt32(1),
            GuardiaID = r.GetInt32(2),
            HoraProgramada = r.GetDateTime(3),
            HoraInicio = r.IsDBNull(4) ? null : r.GetDateTime(4),
            HoraFin = r.IsDBNull(5) ? null : r.GetDateTime(5),
            Estado = r.GetInt32(6),
            PuntosVisitados = r.GetInt32(7),
            PuntosTotal = r.GetInt32(8),
            IncidenciasCount = r.GetInt32(9)
        };

        protected static RondinPunto MapRondinPunto(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            RondinID = r.GetInt32(1),
            PuntoID = r.GetInt32(2),
            HoraVisita = r.IsDBNull(3) ? null : r.GetDateTime(3),
            Estado = r.GetInt32(4),
            LatitudG = r.IsDBNull(5) ? null : r.GetDouble(5),
            LongitudG = r.IsDBNull(6) ? null : r.GetDouble(6),
            Sincronizado = r.GetBoolean(7),
            FechaModificacion = r.GetDateTime(8),
            NombrePunto = r.GetString(9),
            OrdenPunto = r.GetInt32(10)
        };
    }
}