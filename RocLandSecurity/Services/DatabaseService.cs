using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    public class DatabaseService
    {
        private readonly string connectionString;

        public DatabaseService(string connString)
        {
            connectionString = connString;
        }

        // ═══════════════════════════════════════════════════════════════════
        // USUARIOS
        // ═══════════════════════════════════════════════════════════════════

        public async Task<Usuario?> GetUsuarioByLoginAsync(string usuario, string hashContrasena)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM TBL_ROCLAND_SECURITY_USUARIOS
                WHERE Usuario = @usuario AND Contrasena = @contrasena AND Activo = 1";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@usuario", usuario);
            cmd.Parameters.AddWithValue("@contrasena", hashContrasena);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapUsuario(reader);

            return null;
        }

        public async Task<Usuario?> GetUsuarioByQRAsync(string qrCode)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM TBL_ROCLAND_SECURITY_USUARIOS
                WHERE QRCode = @qrCode";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@qrCode", qrCode);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapUsuario(reader);

            return null;
        }

        // Todos los guardias (para filtro del historial del supervisor)
        public async Task<List<Usuario>> GetGuardiasAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM TBL_ROCLAND_SECURITY_USUARIOS
                WHERE Rol = 0 AND Activo = 1
                ORDER BY Nombre";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var lista = new List<Usuario>();
            while (await reader.ReadAsync())
                lista.Add(MapUsuario(reader));

            return lista;
        }

        // ═══════════════════════════════════════════════════════════════════
        // TURNOS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve el turno activo del guardia para el día de hoy.
        /// Busca por Fecha = hoy (el turno nocturno inicia hoy aunque termine mañana).
        /// </summary>
        public async Task<Turno?> GetTurnoActivoAsync(int guardiaID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Fecha, HoraInicio, HoraFin, GuardiaID, SupervisorID
                FROM TBL_ROCLAND_SECURITY_TURNOS
                WHERE GuardiaID = @guardiaID
                  AND Fecha = CAST(GETDATE() AS DATE)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@guardiaID", guardiaID);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapTurno(reader);

            return null;
        }

        /// <summary>
        /// Crea un nuevo turno y ejecuta el SP que genera los 5 rondines con sus 20 puntos.
        /// Devuelve el turno creado.
        /// </summary>
        public async Task<Turno> CrearTurnoYRondinesAsync(int guardiaID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Insertar el turno
            const string insertTurno = @"
                INSERT INTO TBL_ROCLAND_SECURITY_TURNOS (Fecha, HoraInicio, HoraFin, GuardiaID)
                VALUES (CAST(GETDATE() AS DATE), '20:00', '06:00', @guardiaID);
                SELECT SCOPE_IDENTITY();";

            using var cmdInsert = new SqlCommand(insertTurno, conn);
            cmdInsert.Parameters.AddWithValue("@guardiaID", guardiaID);
            var result = await cmdInsert.ExecuteScalarAsync();
            int turnoID = Convert.ToInt32(result);

            // 2. Llamar al SP que genera los 5 rondines con los 20 puntos cada uno
            using var cmdSP = new SqlCommand("sp_GenerarRondinesTurno", conn)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            cmdSP.Parameters.AddWithValue("@TurnoID", turnoID);
            cmdSP.Parameters.AddWithValue("@Fecha", DateOnly.FromDateTime(DateTime.Today));
            cmdSP.Parameters.AddWithValue("@GuardiaID", guardiaID);
            await cmdSP.ExecuteNonQueryAsync();

            // 3. Devolver el turno recién creado
            return new Turno
            {
                ID = turnoID,
                Fecha = DateOnly.FromDateTime(DateTime.Today),
                HoraInicio = new TimeOnly(20, 0),
                HoraFin = new TimeOnly(6, 0),
                GuardiaID = guardiaID
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // RONDINES — GUARDIA
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve los 5 rondines del turno con conteo de puntos visitados.
        /// </summary>
        public async Task<List<Rondin>> GetRondinesPorTurnoAsync(int turnoID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT
                    r.ID, r.TurnoID, r.GuardiaID,
                    r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID) AS PuntosTotal
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                WHERE r.TurnoID = @turnoID
                ORDER BY r.HoraProgramada";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@turnoID", turnoID);

            var lista = new List<Rondin>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lista.Add(MapRondin(reader));

            return lista;
        }

        // ═══════════════════════════════════════════════════════════════════
        // RONDINES — SUPERVISOR
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rondines del turno nocturno activo de hoy para el panel del supervisor.
        /// Agrupa todos los guardias del turno de hoy.
        /// </summary>
        public async Task<List<Rondin>> GetRondinesTurnoActivoAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT
                    r.ID, r.TurnoID, r.GuardiaID,
                    r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID) AS PuntosTotal
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
                WHERE t.Fecha = CAST(GETDATE() AS DATE)
                ORDER BY r.HoraProgramada, r.GuardiaID";

            using var cmd = new SqlCommand(query, conn);

            var lista = new List<Rondin>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lista.Add(MapRondin(reader));

            return lista;
        }

        /// <summary>
        /// Métricas rápidas del turno activo para las tarjetas del panel supervisor.
        /// </summary>
        public async Task<(int Completados, int EnProgreso, int Pendientes, int Incidencias)>
            GetMetricasTurnoActivoAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT
                    SUM(CASE WHEN r.Estado = 2 THEN 1 ELSE 0 END) AS Completados,
                    SUM(CASE WHEN r.Estado = 1 THEN 1 ELSE 0 END) AS EnProgreso,
                    SUM(CASE WHEN r.Estado = 0 THEN 1 ELSE 0 END) AS Pendientes,
                    SUM(CASE WHEN r.Estado = 4 THEN 1 ELSE 0 END) AS Incidencias
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
                WHERE t.Fecha = CAST(GETDATE() AS DATE)";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return (
                    reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                );
            }

            return (0, 0, 0, 0);
        }

        // ═══════════════════════════════════════════════════════════════════
        // MAPPERS
        // ═══════════════════════════════════════════════════════════════════

        private static Usuario MapUsuario(SqlDataReader r) => new()
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

        private static Turno MapTurno(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            Fecha = DateOnly.FromDateTime(r.GetDateTime(1)),
            HoraInicio = TimeOnly.FromTimeSpan(r.GetTimeSpan(2)),
            HoraFin = TimeOnly.FromTimeSpan(r.GetTimeSpan(3)),
            GuardiaID = r.GetInt32(4),
            SupervisorID = r.IsDBNull(5) ? null : r.GetInt32(5)
        };

        private static Rondin MapRondin(SqlDataReader r) => new()
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
            // Sincronizado y FechaModificacion no se cargan en listados
            // generales para mantener las queries ligeras. Se cargan
            // solo cuando se necesita la cola de sync.
        };
    }
}