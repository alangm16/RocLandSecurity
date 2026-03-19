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
            return await reader.ReadAsync() ? MapUsuario(reader) : null;
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
            return await reader.ReadAsync() ? MapUsuario(reader) : null;
        }

        public async Task<List<Usuario>> GetGuardiasAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM TBL_ROCLAND_SECURITY_USUARIOS
                WHERE Rol = 0 AND Activo = 1 ORDER BY Nombre";
            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var lista = new List<Usuario>();
            while (await reader.ReadAsync()) lista.Add(MapUsuario(reader));
            return lista;
        }

        // ═══════════════════════════════════════════════════════════════════
        // TURNOS
        // ═══════════════════════════════════════════════════════════════════

        public async Task<Turno?> GetTurnoActivoAsync(int guardiaID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Si son las 00:00–05:59, el turno activo comenzó AYER
            const string query = @"
                SELECT ID, Fecha, HoraInicio, HoraFin, GuardiaID
                FROM TBL_ROCLAND_SECURITY_TURNOS
                WHERE GuardiaID = @guardiaID
                  AND (
                      -- Turno que empezó hoy
                      Fecha = CAST(GETDATE() AS DATE)
                      OR
                      -- Turno que empezó ayer y su fin cae en madrugada de hoy
                      (Fecha = CAST(DATEADD(DAY,-1,GETDATE()) AS DATE)
                       AND HoraFin <= '06:00'
                       AND CAST(GETDATE() AS TIME) <= '06:00')
                  )";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@guardiaID", guardiaID);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? MapTurno(reader) : null;
        }

        public async Task<Turno> CrearTurnoYRondinesAsync(int guardiaID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Verificar duplicado
            const string checkQuery = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_TURNOS
                WHERE GuardiaID = @guardiaID AND Fecha = CAST(GETDATE() AS DATE)";
            using var cmdCheck = new SqlCommand(checkQuery, conn);
            cmdCheck.Parameters.AddWithValue("@guardiaID", guardiaID);
            var count = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0);
            if (count > 0)
                throw new InvalidOperationException("Ya existe un turno para hoy.");

            // Insertar turno
            const string insertTurno = @"
                INSERT INTO TBL_ROCLAND_SECURITY_TURNOS (Fecha, HoraInicio, HoraFin, GuardiaID)
                VALUES (CAST(GETDATE() AS DATE), '20:00', '06:00', @guardiaID);
                SELECT SCOPE_IDENTITY();";
            using var cmdInsert = new SqlCommand(insertTurno, conn);
            cmdInsert.Parameters.AddWithValue("@guardiaID", guardiaID);
            var result = await cmdInsert.ExecuteScalarAsync();
            int turnoID = Convert.ToInt32(result);

            // Ejecutar SP
            using var cmdSP = new SqlCommand("sp_GenerarRondinesTurno", conn)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            cmdSP.Parameters.AddWithValue("@TurnoID", turnoID);
            cmdSP.Parameters.AddWithValue("@Fecha", DateOnly.FromDateTime(DateTime.Today));
            cmdSP.Parameters.AddWithValue("@GuardiaID", guardiaID);
            await cmdSP.ExecuteNonQueryAsync();

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
        // RONDINES — GUARDIA (lista del turno)
        // ═══════════════════════════════════════════════════════════════════

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
            while (await reader.ReadAsync()) lista.Add(MapRondin(reader));
            return lista;
        }

        // ═══════════════════════════════════════════════════════════════════
        // RONDINES — FLUJO ACTIVO (Sprint 3)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve la HoraProgramada de un rondín para mostrar en el título.
        /// </summary>
        public async Task<DateTime> GetHoraProgramadaRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string q = "SELECT HoraProgramada FROM TBL_ROCLAND_SECURITY_RONDINES WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", rondinID);
            var result = await cmd.ExecuteScalarAsync();
            return result is DateTime dt ? dt : DateTime.Now;
        }

        /// <summary>
        /// Valida que el rondín se pueda iniciar según la hora programada.
        /// modoEstricto=true aplica la ventana horaria (±5 min antes, máx 90 min después).
        /// modoEstricto=false permite iniciar siempre (para pruebas).
        /// Lanza InvalidOperationException si no es posible iniciar.
        /// </summary>
        public async Task IniciarRondinAsync(int rondinID, bool modoEstricto = false)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Obtener HoraProgramada y Estado actual
            const string selectQuery = @"
                SELECT HoraProgramada, Estado
                FROM TBL_ROCLAND_SECURITY_RONDINES
                WHERE ID = @rondinID";
            using var cmdSelect = new SqlCommand(selectQuery, conn);
            cmdSelect.Parameters.AddWithValue("@rondinID", rondinID);
            using var reader = await cmdSelect.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Rondín no encontrado.");

            var horaProgramada = reader.GetDateTime(0);
            var estadoActual = reader.GetInt32(1);
            reader.Close();

            // Si ya está en progreso, solo continuar (retomar)
            if (estadoActual == 1) return;

            if (estadoActual != 0)
                throw new InvalidOperationException("Este rondín ya fue finalizado.");

            // Validación de horario (solo en modo estricto)
            if (modoEstricto)
            {
                var ahora = DateTime.Now;
                var apertura = horaProgramada.AddMinutes(-5);   // 5 min antes
                var cierre = horaProgramada.AddMinutes(90);   // 90 min después

                if (ahora < apertura)
                    throw new InvalidOperationException(
                        $"El rondín aún no está disponible. " +
                        $"Disponible desde las {apertura:HH:mm} hrs.");

                if (ahora > cierre)
                    throw new InvalidOperationException(
                        $"El rondín de las {horaProgramada:HH:mm} ya no puede iniciarse. " +
                        $"El tiempo límite fue {cierre:HH:mm} hrs.");
            }

            // Verificar que no haya otro rondín en progreso en el mismo turno
            const string checkProgreso = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINES
                WHERE TurnoID = (SELECT TurnoID FROM TBL_ROCLAND_SECURITY_RONDINES WHERE ID = @rondinID)
                  AND Estado = 1
                  AND ID != @rondinID";
            using var cmdCheck = new SqlCommand(checkProgreso, conn);
            cmdCheck.Parameters.AddWithValue("@rondinID", rondinID);
            var enProgreso = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0);
            if (enProgreso > 0)
                throw new InvalidOperationException(
                    "Ya hay un rondín en progreso. Finalízalo antes de iniciar otro.");

            // Marcar como iniciado
            const string updateQuery = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINES
                SET Estado = 1, HoraInicio = GETDATE(), FechaModificacion = GETDATE()
                WHERE ID = @rondinID AND Estado = 0";
            using var cmdUpdate = new SqlCommand(updateQuery, conn);
            cmdUpdate.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdUpdate.ExecuteNonQueryAsync();
        }

        public async Task<List<RondinPunto>> GetPuntosDeRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT
                    rp.ID, rp.RondinID, rp.PuntoID,
                    rp.HoraVisita, rp.Estado,
                    rp.LatitudG, rp.LongitudG,
                    rp.Sincronizado, rp.FechaModificacion,
                    pc.Nombre AS NombrePunto,
                    pc.Orden  AS OrdenPunto
                FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                INNER JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON rp.PuntoID = pc.ID
                WHERE rp.RondinID = @rondinID
                ORDER BY pc.Orden";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@rondinID", rondinID);
            var lista = new List<RondinPunto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) lista.Add(MapRondinPunto(reader));
            return lista;
        }

        public async Task<bool> RegistrarVisitaPuntoAsync(
            int rondinPuntoID, double? latitud, double? longitud)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string query = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                SET Estado            = 1,
                    HoraVisita        = GETDATE(),
                    LatitudG          = @lat,
                    LongitudG         = @lon,
                    FechaModificacion = GETDATE()
                WHERE ID = @id AND Estado = 0";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", rondinPuntoID);
            cmd.Parameters.AddWithValue("@lat", (object?)latitud ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lon", (object?)longitud ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task FinalizarRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Omitir puntos pendientes
            const string omitir = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                SET Estado = 2, FechaModificacion = GETDATE()
                WHERE RondinID = @rondinID AND Estado = 0";
            using var cmdOmitir = new SqlCommand(omitir, conn);
            cmdOmitir.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdOmitir.ExecuteNonQueryAsync();

            // Determinar estado final
            const string estadoQuery = @"
                SELECT
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                     WHERE RondinID = @rondinID AND Estado = 2) AS Omitidos,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS
                     WHERE RondinID = @rondinID) AS Incidencias";
            using var cmdEstado = new SqlCommand(estadoQuery, conn);
            cmdEstado.Parameters.AddWithValue("@rondinID", rondinID);
            using var reader = await cmdEstado.ExecuteReaderAsync();
            await reader.ReadAsync();
            int omitidos = reader.GetInt32(0);
            int incidencias = reader.GetInt32(1);
            reader.Close();

            int estadoFinal = incidencias > 0 ? 4 : omitidos > 0 ? 3 : 2;

            const string update = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINES
                SET Estado = @estado, HoraFin = GETDATE(), FechaModificacion = GETDATE()
                WHERE ID = @rondinID";
            using var cmdUpdate = new SqlCommand(update, conn);
            cmdUpdate.Parameters.AddWithValue("@estado", estadoFinal);
            cmdUpdate.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdUpdate.ExecuteNonQueryAsync();
        }
        public async Task<RondinPunto?> GetRondinPuntoPorQRAsync(int rondinID, string qrCode)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT
                    rp.ID, rp.RondinID, rp.PuntoID,
                    rp.HoraVisita, rp.Estado,
                    rp.LatitudG, rp.LongitudG,
                    rp.Sincronizado, rp.FechaModificacion,
                    pc.Nombre AS NombrePunto,
                    pc.Orden  AS OrdenPunto
                FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                INNER JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON rp.PuntoID = pc.ID
                WHERE rp.RondinID = @rondinID AND pc.QRCode = @qrCode";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@rondinID", rondinID);
            cmd.Parameters.AddWithValue("@qrCode", qrCode);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? MapRondinPunto(reader) : null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // RONDINES — SUPERVISOR
        // ═══════════════════════════════════════════════════════════════════

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
            while (await reader.ReadAsync()) lista.Add(MapRondin(reader));
            return lista;
        }

        public async Task<(int Completados, int EnProgreso, int Pendientes, int Incidencias)>
            GetMetricasTurnoActivoAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT
                    SUM(CASE WHEN r.Estado = 2 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN r.Estado = 1 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN r.Estado = 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN r.Estado = 4 THEN 1 ELSE 0 END)
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
                WHERE t.Fecha = CAST(GETDATE() AS DATE)";
            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (
                    reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
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
            GuardiaID = r.GetInt32(4)
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
        };

        /// <summary>
        /// Verifica si un rondín tiene sus puntos generados en RONDINESPUNTOS.
        /// Si no los tiene (turno creado cuando PUNTOSCONTROL estaba vacía),
        /// los genera en el momento. Devuelve la cantidad de puntos disponibles.
        /// </summary>
        public async Task<int> AsegurarPuntosRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Contar puntos actuales del rondín
            const string countQuery = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                WHERE RondinID = @rondinID";
            using var cmdCount = new SqlCommand(countQuery, conn);
            cmdCount.Parameters.AddWithValue("@rondinID", rondinID);
            int puntosActuales = (int)(await cmdCount.ExecuteScalarAsync() ?? 0);

            if (puntosActuales > 0)
                return puntosActuales; // Ya tiene puntos

            // Contar puntos físicos disponibles
            const string countPuntos = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_PUNTOSCONTROL";
            using var cmdPuntos = new SqlCommand(countPuntos, conn);
            int totalPuntos = (int)(await cmdPuntos.ExecuteScalarAsync() ?? 0);

            if (totalPuntos == 0)
                throw new InvalidOperationException(
                    "No hay puntos de control registrados en el sistema. " +
                    "Contacta al administrador.");

            // Generar los puntos faltantes
            const string insertQuery = @"
                INSERT INTO TBL_ROCLAND_SECURITY_RONDINESPUNTOS (RondinID, PuntoID, Estado)
                SELECT @rondinID, pc.ID, 0
                FROM TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc
                WHERE NOT EXISTS (
                    SELECT 1 FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                    WHERE rp.RondinID = @rondinID AND rp.PuntoID = pc.ID
                )
                ORDER BY pc.Orden";
            using var cmdInsert = new SqlCommand(insertQuery, conn);
            cmdInsert.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdInsert.ExecuteNonQueryAsync();

            return totalPuntos;
        }

        private static RondinPunto MapRondinPunto(SqlDataReader r) => new()
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