using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;
using static RocLandSecurity.AppConfig;

namespace RocLandSecurity.Services
{
    public class DatabaseService
    {
        private readonly string connectionString;

        public DatabaseService(string connString)
        {
            connectionString = connString;
        }

        /// <summary>Expone el connection string para servicios de sync.</summary>
        public string GetConnectionString() => connectionString;

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

            // Insertar turno usando las variables de AppConfig
            string horaInicio = HoraInicioTurno; // "19:00"
            string horaFin = HoraFinTurno;       // "07:00"

            const string insertTurnoTemplate = @"
                INSERT INTO TBL_ROCLAND_SECURITY_TURNOS (Fecha, HoraInicio, HoraFin, GuardiaID)
                VALUES (CAST(GETDATE() AS DATE), @HoraInicio, @HoraFin, @GuardiaID);
                SELECT SCOPE_IDENTITY();";

            using var cmdInsert = new SqlCommand(insertTurnoTemplate, conn);
            cmdInsert.Parameters.AddWithValue("@HoraInicio", horaInicio);
            cmdInsert.Parameters.AddWithValue("@HoraFin", horaFin);
            cmdInsert.Parameters.AddWithValue("@GuardiaID", guardiaID);

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

            // Devolver objeto Turno usando TimeOnly.Parse
            return new Turno
            {
                ID = turnoID,
                Fecha = DateOnly.FromDateTime(DateTime.Today),
                HoraInicio = TimeOnly.Parse(horaInicio),
                HoraFin = TimeOnly.Parse(horaFin),
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
        // RONDINES — FLUJO ACTIVO
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve HoraProgramada y TurnoID de un rondín en una sola query.
        /// </summary>
        public async Task<(DateTime HoraProgramada, int TurnoID)> GetDatosRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string q = "SELECT HoraProgramada, TurnoID FROM TBL_ROCLAND_SECURITY_RONDINES WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", rondinID);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetDateTime(0), reader.GetInt32(1));
            return (DateTime.Now, 0);
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

            // Ya iniciado o finalizado — solo continuar, no modificar estado
            if (estadoActual >= 1) return;

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
        // INCIDENCIAS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve todos los puntos de control ordenados, para el Picker de ubicación.
        /// </summary>
        public async Task<List<PuntoControl>> GetPuntosControlAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string q = @"
                SELECT ID, Nombre, QRCode, Orden, Latitud, Longitud
                FROM TBL_ROCLAND_SECURITY_PUNTOSCONTROL
                ORDER BY Orden";
            using var cmd = new SqlCommand(q, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var lista = new List<PuntoControl>();
            while (await reader.ReadAsync())
                lista.Add(new PuntoControl
                {
                    ID = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    QRCode = reader.GetString(2),
                    Orden = reader.GetInt32(3),
                    Latitud = reader.GetDouble(4),
                    Longitud = reader.GetDouble(5),
                });
            return lista;
        }

        /// <summary>
        /// Inserta una nueva incidencia. Si PuntoID o RondinID son null los guarda como NULL.
        /// </summary>
        public async Task CrearIncidenciaAsync(Incidencia inc)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            const string q = @"
                INSERT INTO TBL_ROCLAND_SECURITY_INCIDENCIAS
                    (TurnoID, RondinID, PuntoID, GuardiaReportaID,
                     Descripcion, FechaReporte, Estado, FechaModificacion)
                VALUES
                    (@turnoID, @rondinID, @puntoID, @guardiaID,
                     @desc, GETDATE(), 0, GETDATE())";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@turnoID", inc.TurnoID);
            cmd.Parameters.AddWithValue("@rondinID", (object?)inc.RondinID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@puntoID", (object?)inc.PuntoID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@guardiaID", inc.GuardiaReportaID);
            cmd.Parameters.AddWithValue("@desc", inc.Descripcion);
            await cmd.ExecuteNonQueryAsync();
        }

            // Las incidencias son independientes del estado del rondín.
            // El supervisor las ve en su panel sin alterar el flujo del guardia.

            /// <summary>
            /// Obtiene todas las incidencias de la semana actual (últimos 7 días)
            /// para la vista de supervisor, ordenadas por fecha descendente.
            /// </summary>
        public async Task<List<IncidenciaSupervisorItem>> GetIncidenciasSemanaAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
            SELECT 
                i.ID,
                i.TurnoID,
                i.RondinID,
                i.PuntoID,
                i.GuardiaReportaID,
                i.Descripcion,
                i.FechaReporte,
                i.Estado,
                i.NotaResolucion,
                i.FechaResolucion,
                i.SupervisorResuelveID,
                ISNULL(u.Nombre, 'Desconocido') AS NombreGuardia,
                ISNULL(pc.Nombre, '') AS NombrePunto,
                pc.Orden AS OrdenPunto
            FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
            INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON i.GuardiaReportaID = u.ID
            LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
            WHERE i.FechaReporte >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
              AND i.FechaReporte < DATEADD(DAY, 1, CAST(GETDATE() AS DATE))
            ORDER BY i.FechaReporte DESC, i.ID DESC";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var lista = new List<IncidenciaSupervisorItem>();
            while (await reader.ReadAsync())
            {
                lista.Add(new IncidenciaSupervisorItem
                {
                    ID = reader.GetInt32(0),
                    TurnoID = reader.GetInt32(1),
                    RondinID = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    PuntoID = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    GuardiaReportaID = reader.GetInt32(4),
                    Descripcion = reader.GetString(5),
                    FechaReporte = reader.GetDateTime(6),
                    Estado = reader.GetInt32(7),
                    NotaResolucion = reader.IsDBNull(8) ? null : reader.GetString(8),
                    FechaResolucion = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    SupervisorResuelveID = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    NombreGuardia = reader.GetString(11),
                    NombrePunto = reader.GetString(12),
                    OrdenPunto = reader.IsDBNull(13) ? null : reader.GetInt32(13)
                });
            }

            return lista;
        }

        /// <summary>
        /// Resuelve una incidencia (marcar como resuelta).
        /// </summary>
        public async Task ResolverIncidenciaAsync(int incidenciaID, int supervisorID, string notaResolucion)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
        UPDATE TBL_ROCLAND_SECURITY_INCIDENCIAS
        SET Estado = 1,
            SupervisorResuelveID = @supervisorID,
            FechaResolucion = GETDATE(),
            NotaResolucion = @nota,
            FechaModificacion = GETDATE()
        WHERE ID = @incidenciaID AND Estado = 0";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@incidenciaID", incidenciaID);
            cmd.Parameters.AddWithValue("@supervisorID", supervisorID);
            cmd.Parameters.AddWithValue("@nota", notaResolucion);

            await cmd.ExecuteNonQueryAsync();
        }

        // ═══════════════════════════════════════════════════════════════════
        // HISTORIAL GUARDIA
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve los rondines finalizados del guardia (todos sus turnos)
        /// ordenados del más reciente al más antiguo, con sus incidencias anidadas.
        /// </summary>
        /// <summary>
        /// Devuelve el historial del guardia:
        /// - Todos los rondines iniciados (con o sin finalizar) que tengan
        ///   actividad (HoraInicio != null) O incidencias vinculadas.
        /// - Rondines finalizados aunque no tengan incidencias.
        /// - Incidencias reportadas fuera de rondín (RondinID = NULL)
        ///   agrupadas bajo el rondín más cercano del mismo turno.
        /// Ordenado del más reciente al más antiguo.
        /// </summary>
        public async Task<List<RondinHistorialItem>> GetHistorialGuardiaAsync(int guardiaID)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Rondines que tienen actividad real: iniciados o finalizados
            const string qRondines = @"
                SELECT
                    r.ID, r.TurnoID, r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID) AS PuntosTotal,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                     WHERE i.RondinID = r.ID) AS TotalIncidencias
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                WHERE r.GuardiaID = @guardiaID
                  AND (
                      r.Estado >= 2                   -- finalizados
                      OR r.HoraInicio IS NOT NULL      -- iniciados aunque no finalizados
                      OR EXISTS (                      -- tienen incidencias vinculadas
                          SELECT 1 FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                          WHERE i.RondinID = r.ID)
                  )
                ORDER BY r.HoraProgramada DESC";

            using var cmdR = new SqlCommand(qRondines, conn);
            cmdR.Parameters.AddWithValue("@guardiaID", guardiaID);

            var items = new List<RondinHistorialItem>();
            using (var reader = await cmdR.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    items.Add(new RondinHistorialItem
                    {
                        RondinID         = reader.GetInt32(0),
                        TurnoID          = reader.GetInt32(1),
                        HoraProgramada   = reader.GetDateTime(2),
                        HoraInicio       = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        HoraFin          = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        Estado           = reader.GetInt32(5),
                        PuntosVisitados  = reader.GetInt32(6),
                        PuntosTotal      = reader.GetInt32(7),
                        TotalIncidencias = reader.GetInt32(8),
                    });
                }
            }

            // 2. Incidencias de los rondines cargados
            if (items.Count > 0)
            {
                var ids = string.Join(",", items.Select(i => i.RondinID));
                string qIncRondin = $@"
                    SELECT i.ID, i.RondinID, i.Descripcion, i.FechaReporte,
                           ISNULL(pc.Nombre, '') AS NombrePunto
                    FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                    LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                    WHERE i.RondinID IN ({ids})
                    ORDER BY i.FechaReporte";

                using var cmdI = new SqlCommand(qIncRondin, conn);
                var dict = items.ToDictionary(i => i.RondinID);
                using (var reader = await cmdI.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int rondinID = reader.GetInt32(1);
                        if (dict.TryGetValue(rondinID, out var item))
                            item.Incidencias.Add(new Incidencia
                            {
                                ID           = reader.GetInt32(0),
                                Descripcion  = reader.GetString(2),
                                FechaReporte = reader.GetDateTime(3),
                                NombrePunto  = reader.GetString(4),
                            });
                    }
                }
            }

            // 3. Incidencias reportadas FUERA de rondín (RondinID = NULL)
            //    del mismo guardia. Se agrupan bajo el rondín del turno más
            //    cercano en tiempo, o se agregan como entrada independiente.
            const string qIncSueltas = @"
                SELECT i.ID, i.TurnoID, i.Descripcion, i.FechaReporte,
                       ISNULL(pc.Nombre, '') AS NombrePunto
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                WHERE i.RondinID IS NULL
                  AND i.GuardiaReportaID = @guardiaID
                ORDER BY i.FechaReporte DESC";

            using var cmdS = new SqlCommand(qIncSueltas, conn);
            cmdS.Parameters.AddWithValue("@guardiaID", guardiaID);
            var incSueltas = new List<Incidencia>();
            using (var reader = await cmdS.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    incSueltas.Add(new Incidencia
                    {
                        ID           = reader.GetInt32(0),
                        TurnoID      = reader.GetInt32(1),
                        Descripcion  = reader.GetString(2),
                        FechaReporte = reader.GetDateTime(3),
                        NombrePunto  = reader.GetString(4),
                    });
            }

            // Asignar incidencias sueltas al rondín más cercano del mismo turno,
            // o agregarlas como item independiente si no hay rondín del turno
            foreach (var inc in incSueltas)
            {
                var rondinDelTurno = items
                    .Where(r => r.TurnoID == inc.TurnoID)
                    .OrderBy(r => Math.Abs((r.HoraProgramada - inc.FechaReporte).TotalMinutes))
                    .FirstOrDefault();

                if (rondinDelTurno != null)
                {
                    rondinDelTurno.IncidenciasSinRondin.Add(inc);
                    rondinDelTurno.TotalIncidencias++;
                }
                else
                {
                    // No hay rondín del turno — agregar como item independiente
                    items.Add(new RondinHistorialItem
                    {
                        RondinID         = -1,   // -1 = solo incidencias, sin rondín
                        HoraProgramada   = inc.FechaReporte,
                        TurnoID          = inc.TurnoID,
                        TotalIncidencias = 1,
                        IncidenciasSinRondin = new List<Incidencia> { inc },
                    });
                }
            }

            // Ordenar final por fecha desc
            return items.OrderByDescending(i => i.HoraProgramada).ToList();
        }

        /// <summary>
        /// Obtiene el historial completo para una fecha específica (supervisor).
        /// Incluye todos los turnos de ese día, sus rondines e incidencias.
        /// </summary>
        public async Task<HistorialSupervisorDia> GetHistorialPorFechaAsync(DateTime fecha)
        {
            var resultado = new HistorialSupervisorDia
            {
                Fecha = fecha.Date
            };

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Obtener turnos de la fecha
            const string queryTurnos = @"
        SELECT 
            t.ID,
            t.GuardiaID,
            t.HoraInicio,
            t.HoraFin,
            u.Nombre AS NombreGuardia
        FROM TBL_ROCLAND_SECURITY_TURNOS t
        INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON t.GuardiaID = u.ID
        WHERE t.Fecha = @fecha
        ORDER BY t.HoraInicio";

            using var cmdTurnos = new SqlCommand(queryTurnos, conn);
            cmdTurnos.Parameters.AddWithValue("@fecha", fecha.Date);

            var turnos = new List<HistorialTurnoDia>();
            using (var reader = await cmdTurnos.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    turnos.Add(new HistorialTurnoDia
                    {
                        TurnoID = reader.GetInt32(0),
                        GuardiaID = reader.GetInt32(1),
                        HoraInicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),
                        HoraFin = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                        NombreGuardia = reader.GetString(4)
                    });
                }
            }

            // Para cada turno, obtener sus rondines e incidencias
            foreach (var turno in turnos)
            {
                // Obtener rondines del turno
                const string queryRondines = @"
            SELECT 
                r.ID,
                r.HoraProgramada,
                r.HoraInicio,
                r.HoraFin,
                r.Estado,
                (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                 WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
                (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                 WHERE rp.RondinID = r.ID) AS PuntosTotal,
                (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                 WHERE i.RondinID = r.ID) AS IncidenciasCount
            FROM TBL_ROCLAND_SECURITY_RONDINES r
            WHERE r.TurnoID = @turnoID
            ORDER BY r.HoraProgramada";

                using var cmdRondines = new SqlCommand(queryRondines, conn);
                cmdRondines.Parameters.AddWithValue("@turnoID", turno.TurnoID);

                using (var reader = await cmdRondines.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        turno.Rondines.Add(new HistorialRondinDia
                        {
                            ID = reader.GetInt32(0),
                            HoraProgramada = reader.GetDateTime(1),
                            HoraInicio = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                            HoraFin = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            Estado = reader.GetInt32(4),
                            PuntosVisitados = reader.GetInt32(5),
                            PuntosTotal = reader.GetInt32(6),
                            IncidenciasCount = reader.GetInt32(7)
                        });
                    }
                }

                // Cargar incidencias de cada rondín para mostrar descripción
                foreach (var rondin in turno.Rondines.Where(r => r.TieneIncidencias))
                {
                    const string qIncRondin = @"
                        SELECT i.ID, i.Descripcion, i.FechaReporte,
                               ISNULL(pc.Nombre, '') AS NombrePunto,
                               i.Estado, i.NotaResolucion
                        FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                        LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                        WHERE i.RondinID = @rID
                        ORDER BY i.FechaReporte";
                    using var cmdRI = new SqlCommand(qIncRondin, conn);
                    cmdRI.Parameters.AddWithValue("@rID", rondin.ID);
                    using var riReader = await cmdRI.ExecuteReaderAsync();
                    while (await riReader.ReadAsync())
                    {
                        rondin.Incidencias.Add(new IncidenciaSupervisorItem
                        {
                            ID           = riReader.GetInt32(0),
                            Descripcion  = riReader.GetString(1),
                            FechaReporte = riReader.GetDateTime(2),
                            NombrePunto  = riReader.GetString(3),
                            Estado       = riReader.GetInt32(4),
                            NotaResolucion = riReader.IsDBNull(5) ? null : riReader.GetString(5),
                        });
                    }
                }

                // Obtener incidencias del turno (sin rondín asociado)
                const string queryIncidencias = @"
            SELECT 
                i.ID,
                i.TurnoID,
                i.RondinID,
                i.PuntoID,
                i.GuardiaReportaID,
                i.Descripcion,
                i.FechaReporte,
                i.Estado,
                i.NotaResolucion,
                i.FechaResolucion,
                i.SupervisorResuelveID,
                ISNULL(u.Nombre, 'Desconocido') AS NombreGuardia,
                ISNULL(pc.Nombre, '') AS NombrePunto,
                pc.Orden AS OrdenPunto
            FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
            INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON i.GuardiaReportaID = u.ID
            LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
            WHERE i.TurnoID = @turnoID AND i.RondinID IS NULL
            ORDER BY i.FechaReporte DESC";

                using var cmdInc = new SqlCommand(queryIncidencias, conn);
                cmdInc.Parameters.AddWithValue("@turnoID", turno.TurnoID);

                using (var reader = await cmdInc.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        turno.Incidencias.Add(new IncidenciaSupervisorItem
                        {
                            ID = reader.GetInt32(0),
                            TurnoID = reader.GetInt32(1),
                            RondinID = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                            PuntoID = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                            GuardiaReportaID = reader.GetInt32(4),
                            Descripcion = reader.GetString(5),
                            FechaReporte = reader.GetDateTime(6),
                            Estado = reader.GetInt32(7),
                            NotaResolucion = reader.IsDBNull(8) ? null : reader.GetString(8),
                            FechaResolucion = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                            SupervisorResuelveID = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                            NombreGuardia = reader.GetString(11),
                            NombrePunto = reader.GetString(12),
                            OrdenPunto = reader.IsDBNull(13) ? null : reader.GetInt32(13)
                        });
                    }
                }

                resultado.Turnos.Add(turno);
            }

            return resultado;
        }

        /// <summary>
        /// Obtiene las fechas que tienen actividad (turnos o incidencias) para el calendario.
        /// </summary>
        public async Task<List<DateTime>> GetFechasConActividadAsync()
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT DISTINCT CAST(Fecha AS DATE) as FechaActividad
                FROM TBL_ROCLAND_SECURITY_TURNOS
                UNION
                SELECT DISTINCT CAST(FechaReporte AS DATE) as FechaActividad
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS
                ORDER BY FechaActividad DESC";

            using var cmd = new SqlCommand(query, conn);
            var fechas = new List<DateTime>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                fechas.Add(reader.GetDateTime(0));
            }

            return fechas;
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

        public async Task<Turno?> GetTurnoActivoAsync()
        {
            try
            {
                const string query = @"
            SELECT 
                t.ID,
                t.Fecha,
                t.HoraInicio,
                t.HoraFin,
                t.GuardiaID,
                u.Nombre as NombreGuardia
            FROM TBL_ROCLAND_SECURITY_TURNOS t
            INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON t.GuardiaID = u.ID
            WHERE CAST(t.Fecha AS DATETIME) + CAST(t.HoraInicio AS DATETIME) <= GETDATE()
                AND CAST(t.Fecha AS DATETIME) + CAST(t.HoraFin AS DATETIME) >= GETDATE()
            ORDER BY t.ID DESC";

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new Turno
                    {
                        ID = reader.GetInt32(reader.GetOrdinal("ID")),
                        Fecha = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("Fecha"))),
                        HoraInicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan(reader.GetOrdinal("HoraInicio"))),
                        HoraFin = TimeOnly.FromTimeSpan(reader.GetTimeSpan(reader.GetOrdinal("HoraFin"))),
                        GuardiaID = reader.GetInt32(reader.GetOrdinal("GuardiaID")),
                        NombreGuardia = reader.GetString(reader.GetOrdinal("NombreGuardia"))
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetTurnoActivoAsync: {ex.Message}");
                return null;
            }
        }
    }
}