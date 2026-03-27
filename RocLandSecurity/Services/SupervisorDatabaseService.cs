using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    /// Operaciones exclusivas del supervisor: monitoreo del turno activo,
    /// métricas, detalle de rondines, historial por fecha y gestión de
    /// incidencias. Requiere conexión permanente al servidor.
    /// Registrado como Singleton en MauiProgram.cs.
    public class SupervisorDatabaseService : DatabaseServiceBase
    {
        public SupervisorDatabaseService(string connectionString) : base(connectionString) { }

        // TURNO ACTIVO vista supervisor
        public async Task<Turno?> GetTurnoActivoAsync()
        {
            try
            {
                const string query = @"
                    SELECT t.ID, t.Fecha, t.HoraInicio, t.HoraFin, t.GuardiaID,
                           u.Nombre AS NombreGuardia
                    FROM TBL_ROCLAND_SECURITY_TURNOS t
                    INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON t.GuardiaID = u.ID
                    WHERE
                        (
                            t.Fecha = CAST(GETDATE() AS DATE)
                            AND CAST(GETDATE() AS TIME) >= t.HoraInicio
                            AND (
                                (t.HoraFin > t.HoraInicio AND CAST(GETDATE() AS TIME) <= t.HoraFin)
                                OR t.HoraFin < t.HoraInicio
                            )
                        )
                        OR
                        (
                            t.Fecha = CAST(DATEADD(DAY, -1, GETDATE()) AS DATE)
                            AND t.HoraFin < t.HoraInicio
                            AND CAST(GETDATE() AS TIME) <= t.HoraFin
                        )
                    ORDER BY t.ID DESC";

                using var conn = new SqlConnection(ConnectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) return null;

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetTurnoActivoAsync: {ex.Message}");
                return null;
            }
        }

        // RONDINES DEL TURNO ACTIVO

        public async Task<List<Rondin>> GetRondinesTurnoActivoAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT
                    r.ID, r.TurnoID, r.GuardiaID,
                    r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                     WHERE rp.RondinID = r.ID) AS PuntosTotal,
                    (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                     WHERE i.RondinID = r.ID) AS IncidenciasCount
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
                WHERE t.Fecha = CAST(GETDATE() AS DATE)
                ORDER BY r.HoraProgramada, r.GuardiaID";
            using var cmd = new SqlCommand(query, conn);
            var lista = new List<Rondin>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lista.Add(MapRondinConIncidencias(reader));
            return lista;
        }

        public async Task<(int Completados, int EnProgreso, int Pendientes, int Incidencias)>
    GetMetricasTurnoActivoAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // Todo en un solo roundtrip con UNION
            const string q = @"
        SELECT 'rondines' AS tipo,
            SUM(CASE WHEN r.Estado = 2 THEN 1 ELSE 0 END) AS completados,
            SUM(CASE WHEN r.Estado = 1 THEN 1 ELSE 0 END) AS en_progreso,
            SUM(CASE WHEN r.Estado = 0 THEN 1 ELSE 0 END) AS pendientes,
            0 AS incidencias
        FROM TBL_ROCLAND_SECURITY_RONDINES r
        INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
        WHERE t.Fecha = CAST(GETDATE() AS DATE)
        UNION ALL
        SELECT 'incidencias', 0, 0, 0,
            COUNT(*)
        FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
        WHERE i.TurnoID IN (
            SELECT ID FROM TBL_ROCLAND_SECURITY_TURNOS
            WHERE Fecha = CAST(GETDATE() AS DATE)
        )";

            int completados = 0, enProgreso = 0, pendientes = 0, incidencias = 0;
            using var cmd = new SqlCommand(q, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(0) == "rondines")
                {
                    completados = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    enProgreso = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    pendientes = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                }
                else incidencias = reader.GetInt32(4);
            }
            return (completados, enProgreso, pendientes, incidencias);
        }

        // DETALLE RONDÍN (supervisor)

        public async Task<DetalleRondinSupervisor?> GetDetalleRondinSupervisorAsync(int rondinID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string qRondin = @"
        SELECT
            r.ID, r.TurnoID, r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
            u.Nombre AS NombreGuardia,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS
             WHERE RondinID = r.ID AND Estado = 1) AS PuntosVisitados,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS
             WHERE RondinID = r.ID)                AS PuntosTotal,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS
             WHERE RondinID = r.ID)                AS TotalIncidencias
        FROM TBL_ROCLAND_SECURITY_RONDINES r
        INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON r.GuardiaID = u.ID
        WHERE r.ID = @rondinID";

            using var cmdR = new SqlCommand(qRondin, conn);
            cmdR.Parameters.AddWithValue("@rondinID", rondinID);
            using var readerR = await cmdR.ExecuteReaderAsync();
            if (!await readerR.ReadAsync()) return null;

            var detalle = new DetalleRondinSupervisor
            {
                RondinID = readerR.GetInt32(0),
                TurnoID = readerR.GetInt32(1),
                HoraProgramada = readerR.GetDateTime(2),
                HoraInicio = readerR.IsDBNull(3) ? null : readerR.GetDateTime(3),
                HoraFin = readerR.IsDBNull(4) ? null : readerR.GetDateTime(4),
                Estado = readerR.GetInt32(5),
                NombreGuardia = readerR.GetString(6),
                PuntosVisitados = readerR.GetInt32(7),
                PuntosTotal = readerR.GetInt32(8),
                TotalIncidencias = readerR.GetInt32(9),
            };
            readerR.Close();

            const string qPuntos = @"
                SELECT rp.ID, pc.Orden, pc.Nombre, rp.Estado, rp.HoraVisita, rp.FotoPath
                FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
                INNER JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON rp.PuntoID = pc.ID
                WHERE rp.RondinID = @rondinID
                ORDER BY pc.Orden";
            using var cmdP = new SqlCommand(qPuntos, conn);
            cmdP.Parameters.AddWithValue("@rondinID", rondinID);
            using var readerP = await cmdP.ExecuteReaderAsync();

            DateTime? horaAnterior = null;
            while (await readerP.ReadAsync())
            {
                var horaVisita = readerP.IsDBNull(4) ? (DateTime?)null : readerP.GetDateTime(4);
                TimeSpan? intervalo = (horaVisita.HasValue && horaAnterior.HasValue)
                    ? horaVisita.Value - horaAnterior.Value : null;

                detalle.Puntos.Add(new PuntoDetalleItem
                {
                    RondinPuntoID = readerP.GetInt32(0),
                    Orden = readerP.GetInt32(1),
                    Nombre = readerP.GetString(2),
                    Estado = readerP.GetInt32(3),
                    HoraVisita = horaVisita,
                    Intervalo = intervalo,
                    FotoBytes = readerP.IsDBNull(5) ? null : (byte[])readerP.GetValue(5)
                });
                if (horaVisita.HasValue) horaAnterior = horaVisita;
            }
            readerP.Close();

            const string qIncidencias = @"
        SELECT i.ID, i.Descripcion, i.FechaReporte, i.Estado, i.NotaResolucion,
               ISNULL(pc.Nombre, '') AS NombrePunto,
               CASE WHEN i.FotoPath IS NOT NULL AND DATALENGTH(i.FotoPath) > 0
                    THEN 1 ELSE 0 END AS TieneFoto
        FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
        LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
        WHERE i.RondinID = @rondinID
        ORDER BY i.FechaReporte";

            using var cmdI = new SqlCommand(qIncidencias, conn);
            cmdI.Parameters.AddWithValue("@rondinID", rondinID);
            using var readerI = await cmdI.ExecuteReaderAsync();

            while (await readerI.ReadAsync())
            {
                detalle.Incidencias.Add(new IncidenciaResumen
                {
                    ID = readerI.GetInt32(0),
                    Descripcion = readerI.GetString(1),
                    FechaReporte = readerI.GetDateTime(2),
                    Estado = readerI.GetInt32(3),
                    NotaResolucion = readerI.IsDBNull(4) ? null : readerI.GetString(4),
                    NombrePunto = readerI.GetString(5),
                    TieneFoto = readerI.GetInt32(6) == 1  // ← nuevo
                });
            }

            return detalle;
        }

        public async Task<byte[]?> GetFotoPuntoAsync(int rondinPuntoID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string q = "SELECT FotoPath FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", rondinPuntoID);
            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? null : (byte[])result;
        }

        public async Task<byte[]?> GetFotoIncidenciaAsync(int incidenciaID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string q = "SELECT FotoPath FROM TBL_ROCLAND_SECURITY_INCIDENCIAS WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", incidenciaID);
            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value || result == null ? null : (byte[])result;
        }

        // INCIDENCIAS (supervisor ve y resuelve)

        public async Task<List<IncidenciaSupervisorItem>> GetIncidenciasSemanaAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
        SELECT
            i.ID, i.TurnoID, i.RondinID, i.PuntoID, i.GuardiaReportaID,
            i.Descripcion, i.FechaReporte, i.Estado,
            i.NotaResolucion, i.FechaResolucion, i.SupervisorResuelveID,
            ISNULL(u.Nombre, 'Desconocido') AS NombreGuardia,
            ISNULL(pc.Nombre, '')            AS NombrePunto,
            pc.Orden                         AS OrdenPunto,
            CASE WHEN i.FotoPath IS NOT NULL
                  AND DATALENGTH(i.FotoPath) > 0
                 THEN 1 ELSE 0 END           AS TieneFoto
        FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
        INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON i.GuardiaReportaID = u.ID
        LEFT  JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
        WHERE i.FechaReporte >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
          AND i.FechaReporte <  DATEADD(DAY,  1, CAST(GETDATE() AS DATE))
        ORDER BY i.FechaReporte DESC, i.ID DESC";
            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var lista = new List<IncidenciaSupervisorItem>();
            while (await reader.ReadAsync())
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
                    OrdenPunto = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    TieneFoto = reader.GetInt32(14) == 1   // ← nuevo
                });
            return lista;
        }

        public async Task ResolverIncidenciaAsync(int incidenciaID, int supervisorID, string notaResolucion)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
                UPDATE TBL_ROCLAND_SECURITY_INCIDENCIAS
                SET Estado = 1,
                    SupervisorResuelveID = @supervisorID,
                    FechaResolucion      = GETDATE(),
                    NotaResolucion       = @nota,
                    FechaModificacion    = GETDATE()
                WHERE ID = @incidenciaID AND Estado = 0";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@incidenciaID", incidenciaID);
            cmd.Parameters.AddWithValue("@supervisorID", supervisorID);
            cmd.Parameters.AddWithValue("@nota", notaResolucion);
            await cmd.ExecuteNonQueryAsync();
        }

        // HISTORIAL (supervisor — por fecha)

        public async Task<HistorialSupervisorDia> GetHistorialPorFechaAsync(DateTime fecha)
        {
            var resultado = new HistorialSupervisorDia { Fecha = fecha.Date };
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // ── 1. Turnos + rondines en UN solo query ───────────────────────────
            const string qTodoEnUno = @"
        SELECT
            t.ID         AS TurnoID,
            t.GuardiaID,
            t.HoraInicio AS TurnoHoraInicio,
            t.HoraFin    AS TurnoHoraFin,
            u.Nombre     AS NombreGuardia,
            r.ID         AS RondinID,
            r.HoraProgramada,
            r.HoraInicio AS RondinHoraInicio,
            r.HoraFin    AS RondinHoraFin,
            r.Estado     AS RondinEstado,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
             WHERE rp.RondinID = r.ID AND rp.Estado = 1) AS PuntosVisitados,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS rp
             WHERE rp.RondinID = r.ID)                   AS PuntosTotal,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
             WHERE i.RondinID = r.ID)                    AS IncidenciasCount
        FROM TBL_ROCLAND_SECURITY_TURNOS t
        INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON t.GuardiaID = u.ID
        LEFT  JOIN TBL_ROCLAND_SECURITY_RONDINES r ON r.TurnoID = t.ID
        WHERE t.Fecha = @fecha
        ORDER BY t.ID, r.HoraProgramada";

            var turnosDict = new Dictionary<int, HistorialTurnoDia>();
            using (var cmd = new SqlCommand(qTodoEnUno, conn))
            {
                cmd.Parameters.AddWithValue("@fecha", fecha.Date);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int turnoID = reader.GetInt32(0);
                    if (!turnosDict.TryGetValue(turnoID, out var turno))
                    {
                        turno = new HistorialTurnoDia
                        {
                            TurnoID = turnoID,
                            GuardiaID = reader.GetInt32(1),
                            HoraInicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),
                            HoraFin = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                            NombreGuardia = reader.GetString(4)
                        };
                        turnosDict[turnoID] = turno;
                    }
                    if (!reader.IsDBNull(5)) // Puede no haber rondines
                    {
                        turno.Rondines.Add(new HistorialRondinDia
                        {
                            ID = reader.GetInt32(5),
                            HoraProgramada = reader.GetDateTime(6),
                            HoraInicio = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                            HoraFin = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                            Estado = reader.GetInt32(9),
                            PuntosVisitados = reader.GetInt32(10),
                            PuntosTotal = reader.GetInt32(11),
                            IncidenciasCount = reader.GetInt32(12)
                        });
                    }
                }
            }

            // ── 2. Incidencias de TODOS los rondines con incidencias en 1 query ─
            var rondinIds = turnosDict.Values
                .SelectMany(t => t.Rondines)
                .Where(r => r.TieneIncidencias)
                .Select(r => r.ID)
                .ToList();

            if (rondinIds.Any())
            {
                var parametros = string.Join(",", rondinIds.Select((_, i) => $"@r{i}"));
                var qInc = $@"
            SELECT i.RondinID, i.ID, i.Descripcion, i.FechaReporte,
                   ISNULL(pc.Nombre,'') AS NombrePunto, i.Estado, i.NotaResolucion
            FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
            LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
            WHERE i.RondinID IN ({parametros})
            ORDER BY i.RondinID, i.FechaReporte";

                using var cmdInc = new SqlCommand(qInc, conn);
                for (int i = 0; i < rondinIds.Count; i++)
                    cmdInc.Parameters.AddWithValue($"@r{i}", rondinIds[i]);

                var incByRondin = turnosDict.Values
                    .SelectMany(t => t.Rondines)
                    .ToDictionary(r => r.ID);

                using var reader = await cmdInc.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int rId = reader.GetInt32(0);
                    if (incByRondin.TryGetValue(rId, out var rondin))
                        rondin.Incidencias.Add(new IncidenciaSupervisorItem
                        {
                            ID = reader.GetInt32(1),
                            Descripcion = reader.GetString(2),
                            FechaReporte = reader.GetDateTime(3),
                            NombrePunto = reader.GetString(4),
                            Estado = reader.GetInt32(5),
                            NotaResolucion = reader.IsDBNull(6) ? null : reader.GetString(6),
                        });
                }
            }

            // ── 3. Incidencias sin rondín (del turno directo) ────────────────────
            var turnoIds = turnosDict.Keys.ToList();
            if (turnoIds.Any())
            {
                var parametros = string.Join(",", turnoIds.Select((_, i) => $"@t{i}"));
                var qIncTurno = $@"
            SELECT i.TurnoID, i.ID, i.TurnoID AS tID, i.RondinID, i.PuntoID,
                   i.GuardiaReportaID, i.Descripcion, i.FechaReporte, i.Estado,
                   i.NotaResolucion, i.FechaResolucion, i.SupervisorResuelveID,
                   ISNULL(u.Nombre,'Desconocido') AS NombreGuardia,
                   ISNULL(pc.Nombre,'')           AS NombrePunto,
                   pc.Orden                       AS OrdenPunto
            FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
            INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON i.GuardiaReportaID = u.ID
            LEFT  JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
            WHERE i.TurnoID IN ({parametros}) AND i.RondinID IS NULL
            ORDER BY i.FechaReporte DESC";

                using var cmdIT = new SqlCommand(qIncTurno, conn);
                for (int i = 0; i < turnoIds.Count; i++)
                    cmdIT.Parameters.AddWithValue($"@t{i}", turnoIds[i]);

                using var reader = await cmdIT.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int turnoID = reader.GetInt32(0);
                    if (turnosDict.TryGetValue(turnoID, out var turno))
                        turno.Incidencias.Add(new IncidenciaSupervisorItem
                        {
                            ID = reader.GetInt32(1),
                            TurnoID = reader.GetInt32(2),
                            RondinID = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                            PuntoID = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                            GuardiaReportaID = reader.GetInt32(5),
                            Descripcion = reader.GetString(6),
                            FechaReporte = reader.GetDateTime(7),
                            Estado = reader.GetInt32(8),
                            NotaResolucion = reader.IsDBNull(9) ? null : reader.GetString(9),
                            FechaResolucion = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                            SupervisorResuelveID = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                            NombreGuardia = reader.GetString(12),
                            NombrePunto = reader.GetString(13),
                            OrdenPunto = reader.IsDBNull(14) ? null : reader.GetInt32(14)
                        });
                }
            }

            foreach (var t in turnosDict.Values.OrderBy(t => t.HoraInicio))
                resultado.Turnos.Add(t);

            return resultado;
        }

        public async Task<List<DateTime>> GetFechasConActividadAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
                SELECT DISTINCT CAST(Fecha AS DATE)
                FROM TBL_ROCLAND_SECURITY_TURNOS
                UNION
                SELECT DISTINCT CAST(FechaReporte AS DATE)
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS
                ORDER BY 1 DESC";
            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var fechas = new List<DateTime>();
            while (await reader.ReadAsync()) fechas.Add(reader.GetDateTime(0));
            return fechas;
        }

        // CRUD USUARIOS

        public async Task<List<Usuario>> GetUsuarios(string? filtro = null)
        {
            var lista = new List<Usuario>();
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion,
                    Activo FROM TBL_ROCLAND_SECURITY_USUARIOS
                    Where Activo = 1 and Rol = 0";

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                query += "AND (Nombre LIKE @filtro OR Usuario LIKE @filtro)";
            }

            using var cmd = new SqlCommand(query, conn);

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                cmd.Parameters.AddWithValue("@filtro", $"%{filtro}%");
            }

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(MapUsuario(reader));
            }
            return lista;
        }

        public async Task<int> AgregarGuardiaAsync(string nombre, string usuarioLogin, string contrasena, string? qrCode)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string query = @"
        INSERT INTO TBL_ROCLAND_SECURITY_USUARIOS
            (Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo)
        VALUES
            (@Nombre, @Usuario,
             CONVERT(NVARCHAR(255), HASHBYTES('SHA2_256', CONVERT(VARCHAR(255), @Contrasena)), 2),
             @QRCode, 0, GETDATE(), 1);
        SELECT SCOPE_IDENTITY();";

            using var cmdInsert = new SqlCommand(query, conn);
            cmdInsert.Parameters.AddWithValue("@Nombre", nombre);
            cmdInsert.Parameters.AddWithValue("@Usuario", usuarioLogin);
            cmdInsert.Parameters.AddWithValue("@Contrasena", contrasena); // Texto plano
            cmdInsert.Parameters.AddWithValue("@QRCode", (object?)qrCode ?? DBNull.Value);

            var result = await cmdInsert.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task EditarGuardiaAsync(int id, string nombre, string usuarioLogin,
                                              string? nuevaContrasena, string? qrCode)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            string query;
            if (!string.IsNullOrWhiteSpace(nuevaContrasena))
            {
                query = @"
                    UPDATE TBL_ROCLAND_SECURITY_USUARIOS
                    SET Nombre     = @Nombre,
                        Usuario    = @Usuario,
                        Contrasena = CONVERT(NVARCHAR(255), HASHBYTES('SHA2_256', CONVERT(VARCHAR(255), @Contrasena)), 2),
                        QRCode     = @QRCode
                    WHERE ID = @ID";
            }
            else
            {
                query = @"
                    UPDATE TBL_ROCLAND_SECURITY_USUARIOS
                    SET Nombre  = @Nombre,
                        Usuario = @Usuario,
                        QRCode  = @QRCode
                    WHERE ID = @ID";
            }

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ID", id);
            cmd.Parameters.AddWithValue("@Nombre", nombre);
            cmd.Parameters.AddWithValue("@Usuario", usuarioLogin);
            cmd.Parameters.AddWithValue("@QRCode", (object?)qrCode ?? DBNull.Value);

            if (!string.IsNullOrWhiteSpace(nuevaContrasena))
                cmd.Parameters.AddWithValue("@Contrasena", nuevaContrasena);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EliminarGuardiaAsync(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string query = @"
                UPDATE TBL_ROCLAND_SECURITY_USUARIOS
                SET Activo = 0
                WHERE ID = @ID";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ID", id);
            await cmd.ExecuteNonQueryAsync();
        }

    }
}