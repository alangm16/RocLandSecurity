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

            const string qRondines = @"
                SELECT
                    SUM(CASE WHEN r.Estado = 2 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN r.Estado = 1 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN r.Estado = 0 THEN 1 ELSE 0 END)
                FROM TBL_ROCLAND_SECURITY_RONDINES r
                INNER JOIN TBL_ROCLAND_SECURITY_TURNOS t ON r.TurnoID = t.ID
                WHERE t.Fecha = CAST(GETDATE() AS DATE)";

            int completados = 0, enProgreso = 0, pendientes = 0;
            using var cmdR = new SqlCommand(qRondines, conn);
            using (var reader = await cmdR.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    completados = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    enProgreso = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    pendientes = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                }
            }

            const string qIncidencias = @"
                SELECT COUNT(*)
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                WHERE i.TurnoID IN (
                    SELECT ID FROM TBL_ROCLAND_SECURITY_TURNOS
                    WHERE Fecha = CAST(GETDATE() AS DATE)
                )";
            using var cmdI = new SqlCommand(qIncidencias, conn);
            var result = await cmdI.ExecuteScalarAsync();
            int incidencias = result == DBNull.Value ? 0 : Convert.ToInt32(result);

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
                SELECT ID, Descripcion, FechaReporte, Estado, NotaResolucion
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS
                WHERE RondinID = @rondinID
                ORDER BY FechaReporte";
            using var cmdI = new SqlCommand(qIncidencias, conn);
            cmdI.Parameters.AddWithValue("@rondinID", rondinID);
            using var readerI = await cmdI.ExecuteReaderAsync();
            while (await readerI.ReadAsync())
                detalle.Incidencias.Add(new IncidenciaResumen
                {
                    ID = readerI.GetInt32(0),
                    Descripcion = readerI.GetString(1),
                    FechaReporte = readerI.GetDateTime(2),
                    Estado = readerI.GetInt32(3),
                    NotaResolucion = readerI.IsDBNull(4) ? null : readerI.GetString(4)
                });

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
                    pc.Orden                         AS OrdenPunto
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
                    OrdenPunto = reader.IsDBNull(13) ? null : reader.GetInt32(13)
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
            // — sin cambios respecto al original —
            // (cuerpo idéntico al DatabaseService original)
            var resultado = new HistorialSupervisorDia { Fecha = fecha.Date };
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string queryTurnos = @"
                SELECT t.ID, t.GuardiaID, t.HoraInicio, t.HoraFin, u.Nombre AS NombreGuardia
                FROM TBL_ROCLAND_SECURITY_TURNOS t
                INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON t.GuardiaID = u.ID
                WHERE t.Fecha = @fecha
                ORDER BY t.HoraInicio";
            using var cmdTurnos = new SqlCommand(queryTurnos, conn);
            cmdTurnos.Parameters.AddWithValue("@fecha", fecha.Date);

            var turnos = new List<HistorialTurnoDia>();
            using (var reader = await cmdTurnos.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    turnos.Add(new HistorialTurnoDia
                    {
                        TurnoID = reader.GetInt32(0),
                        GuardiaID = reader.GetInt32(1),
                        HoraInicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),
                        HoraFin = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                        NombreGuardia = reader.GetString(4)
                    });

            foreach (var turno in turnos)
            {
                const string queryRondines = @"
                    SELECT r.ID, r.HoraProgramada, r.HoraInicio, r.HoraFin, r.Estado,
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
                    while (await reader.ReadAsync())
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

                foreach (var rondin in turno.Rondines.Where(r => r.TieneIncidencias))
                {
                    const string qIncRondin = @"
                        SELECT i.ID, i.Descripcion, i.FechaReporte,
                               ISNULL(pc.Nombre, '') AS NombrePunto, i.Estado, i.NotaResolucion
                        FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                        LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                        WHERE i.RondinID = @rID ORDER BY i.FechaReporte";
                    using var cmdRI = new SqlCommand(qIncRondin, conn);
                    cmdRI.Parameters.AddWithValue("@rID", rondin.ID);
                    using var riReader = await cmdRI.ExecuteReaderAsync();
                    while (await riReader.ReadAsync())
                        rondin.Incidencias.Add(new IncidenciaSupervisorItem
                        {
                            ID = riReader.GetInt32(0),
                            Descripcion = riReader.GetString(1),
                            FechaReporte = riReader.GetDateTime(2),
                            NombrePunto = riReader.GetString(3),
                            Estado = riReader.GetInt32(4),
                            NotaResolucion = riReader.IsDBNull(5) ? null : riReader.GetString(5),
                        });
                }

                const string queryIncidencias = @"
                    SELECT i.ID, i.TurnoID, i.RondinID, i.PuntoID, i.GuardiaReportaID,
                           i.Descripcion, i.FechaReporte, i.Estado,
                           i.NotaResolucion, i.FechaResolucion, i.SupervisorResuelveID,
                           ISNULL(u.Nombre, 'Desconocido') AS NombreGuardia,
                           ISNULL(pc.Nombre, '')            AS NombrePunto,
                           pc.Orden                         AS OrdenPunto
                    FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                    INNER JOIN TBL_ROCLAND_SECURITY_USUARIOS u ON i.GuardiaReportaID = u.ID
                    LEFT  JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                    WHERE i.TurnoID = @turnoID AND i.RondinID IS NULL
                    ORDER BY i.FechaReporte DESC";
                using var cmdInc = new SqlCommand(queryIncidencias, conn);
                cmdInc.Parameters.AddWithValue("@turnoID", turno.TurnoID);
                using (var reader = await cmdInc.ExecuteReaderAsync())
                    while (await reader.ReadAsync())
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

                resultado.Turnos.Add(turno);
            }
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
    }
}