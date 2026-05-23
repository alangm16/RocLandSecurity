using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;
using static RocLandSecurity.AppConfig;

namespace RocLandSecurity.Services
{

    /// Operaciones online del guardia: turnos, rondines activos,
    /// puntos, fotos, incidencias (crear) e historial propio.
    /// Trabaja tanto online como offline (a través de SyncService).
    /// Registrado como Singleton en MauiProgram.cs.

    public class GuardiaDatabaseService : DatabaseServiceBase
    {
        public GuardiaDatabaseService(string connectionString) : base(connectionString) { }

        // TURNOS

        public async Task<Turno?> GetTurnoActivoAsync(int guardiaID)
        {
            using var conn = new SqlConnection(AppConfig.ConnectionString);
            await conn.OpenAsync();

            // Solo traemos turnos Pendientes (0) o En Progreso (1)
            const string query = @"
                SELECT ID, Fecha, HoraInicio, HoraFin, GuardiaID, Estado
                FROM TBL_ROCLAND_SECURITY_TURNOS
                WHERE GuardiaID = @guardiaID AND Estado IN (0, 1)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@guardiaID", guardiaID);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var turno = MapTurno(reader); // Asegúrate de mapear turno.Estado

            // 💡 MEJORA ESTRELLA: Auto-finalización por tiempo pasado
            DateTime limiteFin = CalcularLimiteFinDeTurno(turno.Fecha);

            if (DateTime.Now >= limiteFin)
            {
                // El tiempo ya pasó. Forzamos el estado a Finalizado (2)
                await FinalizarTurnoAsync(turno.ID);

                // Retornamos null porque ya no hay turno activo válido para operar
                return null;
            }

            return turno;
        }

        // Método para finalizar (usado manual y automáticamente)
        public async Task FinalizarTurnoAsync(int turnoID)
        {
            using var conn = new SqlConnection(AppConfig.ConnectionString);
            await conn.OpenAsync();
            const string query = "UPDATE TBL_ROCLAND_SECURITY_TURNOS SET Estado = 2 WHERE ID = @ID";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ID", turnoID);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<Turno> CrearTurnoYRondinesAsync(int guardiaID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // 1. Validamos que no tenga un turno Pendiente (0) o En Progreso (1) actualmente
            const string checkQuery = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_TURNOS
                WHERE GuardiaID = @guardiaID 
                  AND Fecha = CAST(GETDATE() AS DATE)";

            using var cmdCheck = new SqlCommand(checkQuery, conn);
            cmdCheck.Parameters.AddWithValue("@guardiaID", guardiaID);

            var count = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0);
            if (count > 0)
                throw new InvalidOperationException("Ya existe un turno pendiente o activo para hoy.");

            string horaInicio = HoraInicioTurno;
            string horaFin = HoraFinTurno;

            // 2. Insertamos el turno y forzamos el Estado = 1 (En progreso)
            const string insertTurno = @"
                INSERT INTO TBL_ROCLAND_SECURITY_TURNOS (Fecha, HoraInicio, HoraFin, Estado, GuardiaID)
                VALUES (CAST(GETDATE() AS DATE), @HoraInicio, @HoraFin, 1, @GuardiaID);
                SELECT SCOPE_IDENTITY();";

            using var cmdInsert = new SqlCommand(insertTurno, conn);
            cmdInsert.Parameters.AddWithValue("@HoraInicio", horaInicio);
            cmdInsert.Parameters.AddWithValue("@HoraFin", horaFin);
            cmdInsert.Parameters.AddWithValue("@GuardiaID", guardiaID);

            int turnoID = Convert.ToInt32(await cmdInsert.ExecuteScalarAsync());

            // Generamos los rondines
            using var cmdSP = new SqlCommand("sp_GenerarRondinesTurno", conn)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            cmdSP.Parameters.AddWithValue("@TurnoID", turnoID);
            cmdSP.Parameters.AddWithValue("@Fecha", DateOnly.FromDateTime(DateTime.Today));
            cmdSP.Parameters.AddWithValue("@GuardiaID", guardiaID);

            await cmdSP.ExecuteNonQueryAsync();

            // 3. Retornamos el objeto con su Estado actualizado
            return new Turno
            {
                ID = turnoID,
                Fecha = DateOnly.FromDateTime(DateTime.Today),
                HoraInicio = TimeOnly.Parse(horaInicio),
                HoraFin = TimeOnly.Parse(horaFin),
                Estado = 1, // Lo marcamos como En Progreso
                GuardiaID = guardiaID
            };
        }

        // RONDINES — LISTA DEL TURNO

        public async Task<List<Rondin>> GetRondinesPorTurnoAsync(int turnoID)
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

        // RONDINES — FLUJO ACTIVO

        public async Task<(DateTime HoraProgramada, int TurnoID)> GetDatosRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string q = "SELECT HoraProgramada, TurnoID FROM TBL_ROCLAND_SECURITY_RONDINES WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", rondinID);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetDateTime(0), reader.GetInt32(1));
            return (DateTime.Now, 0);
        }

        public async Task IniciarRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

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

            if (estadoActual >= 1) return;

            if (AppConfig.ModoEstrictoRondines)
            {
                var ahora = DateTime.Now;
                var apertura = horaProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
                var cierre = horaProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);

                if (ahora < apertura)
                    throw new InvalidOperationException(
                        $"El rondín aún no está disponible. Disponible desde las {apertura:HH:mm} hrs.");
                if (ahora > cierre)
                    throw new InvalidOperationException(
                        $"El rondín de las {horaProgramada:HH:mm} ya no puede iniciarse. " +
                        $"El tiempo límite fue {cierre:HH:mm} hrs.");
            }

            const string checkProgreso = @"
                SELECT COUNT(*)
                FROM TBL_ROCLAND_SECURITY_RONDINES
                WHERE TurnoID = (
                    SELECT TurnoID FROM TBL_ROCLAND_SECURITY_RONDINES WHERE ID = @rondinID
                )
                  AND Estado = 1
                  AND ID != @rondinID";
            using var cmdCheck = new SqlCommand(checkProgreso, conn);
            cmdCheck.Parameters.AddWithValue("@rondinID", rondinID);
            var enProgreso = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0);
            if (enProgreso > 0)
                throw new InvalidOperationException(
                    "Ya hay un rondín en progreso. Finalízalo antes de iniciar otro.");

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
            using var conn = new SqlConnection(ConnectionString);
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

        public async Task<bool> RegistrarVisitaPuntoAsync(int rondinPuntoID, double? latitud, double? longitud)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                SET Estado = 1, HoraVisita = GETDATE(),
                    LatitudG = @lat, LongitudG = @lon,
                    FechaModificacion = GETDATE()
                WHERE ID = @id AND Estado = 0";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", rondinPuntoID);
            cmd.Parameters.AddWithValue("@lat", (object?)latitud ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lon", (object?)longitud ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task<RondinPunto?> GetRondinPuntoPorQRAsync(int rondinID, string qrCode)
        {
            using var conn = new SqlConnection(ConnectionString);
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

        public async Task FinalizarRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string omitir = @"
        UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
        SET Estado = 2, FechaModificacion = GETDATE()
        WHERE RondinID = @rondinID AND Estado = 0";
            using var cmdOmitir = new SqlCommand(omitir, conn);
            cmdOmitir.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdOmitir.ExecuteNonQueryAsync();

            // 💡 SOLUCIÓN 2: Agregamos la columna "TotalPuntos" a la consulta
            const string estadoQuery = @"
        SELECT
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS WHERE RondinID = @rondinID) AS TotalPuntos,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS WHERE RondinID = @rondinID AND Estado = 2) AS Omitidos,
            (SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_INCIDENCIAS WHERE RondinID = @rondinID) AS Incidencias";

            using var cmdEstado = new SqlCommand(estadoQuery, conn);
            cmdEstado.Parameters.AddWithValue("@rondinID", rondinID);

            using var reader = await cmdEstado.ExecuteReaderAsync();
            await reader.ReadAsync();
            int totalPuntos = reader.GetInt32(0);
            int omitidos = reader.GetInt32(1);
            int incidencias = reader.GetInt32(2);
            reader.Close();

            // 💡 LÓGICA CORREGIDA: Si nunca se generaron puntos (0), es porque no se hizo.
            int estadoFinal = 2; // Asumimos Completado por defecto
            if (totalPuntos == 0 || omitidos > 0) estadoFinal = 3; // Incompleto
            if (incidencias > 0) estadoFinal = 4; // Con Incidencia

            const string update = @"
        UPDATE TBL_ROCLAND_SECURITY_RONDINES
        SET Estado = @estado, HoraFin = ISNULL(HoraFin, GETDATE()), FechaModificacion = GETDATE()
        WHERE ID = @rondinID";
            using var cmdUpdate = new SqlCommand(update, conn);
            cmdUpdate.Parameters.AddWithValue("@estado", estadoFinal);
            cmdUpdate.Parameters.AddWithValue("@rondinID", rondinID);
            await cmdUpdate.ExecuteNonQueryAsync();
        }

        public async Task<int> AsegurarPuntosRondinAsync(int rondinID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string countQuery = @"
                SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                WHERE RondinID = @rondinID";
            using var cmdCount = new SqlCommand(countQuery, conn);
            cmdCount.Parameters.AddWithValue("@rondinID", rondinID);
            int puntosActuales = (int)(await cmdCount.ExecuteScalarAsync() ?? 0);
            if (puntosActuales > 0) return puntosActuales;

            const string countPuntos = "SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_PUNTOSCONTROL";
            using var cmdPuntos = new SqlCommand(countPuntos, conn);
            int totalPuntos = (int)(await cmdPuntos.ExecuteScalarAsync() ?? 0);
            if (totalPuntos == 0)
                throw new InvalidOperationException(
                    "No hay puntos de control registrados. Contacta al administrador.");

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

        // FOTOS

        public async Task ActualizarFotoPuntoAsync(int rondinPuntoServerID, byte[] fotoBytes)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string query = @"
                UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                SET FotoPath = @foto, FechaModificacion = GETDATE()
                WHERE ID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", rondinPuntoServerID);
            cmd.Parameters.AddWithValue("@foto", fotoBytes ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // INCIDENCIAS (guardia crea)
        public async Task CrearIncidenciaAsync(Incidencia inc)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            const string q = @"
        INSERT INTO TBL_ROCLAND_SECURITY_INCIDENCIAS
            (TurnoID, RondinID, PuntoID, GuardiaReportaID,
             Descripcion, FotoPath, FechaReporte, Estado, FechaModificacion)
        VALUES
            (@turnoID, @rondinID, @puntoID, @guardiaID,
             @desc, @foto, GETDATE(), 0, GETDATE())";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@turnoID", inc.TurnoID);
            cmd.Parameters.AddWithValue("@rondinID", (object?)inc.RondinID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@puntoID", (object?)inc.PuntoID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@guardiaID", inc.GuardiaReportaID);
            cmd.Parameters.AddWithValue("@desc", inc.Descripcion);

            var fotoParam = cmd.Parameters.Add("@foto", System.Data.SqlDbType.VarBinary);
            fotoParam.Value = inc.FotoPath != null && inc.FotoPath.Length > 0
                ? inc.FotoPath
                : DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        // HISTORIAL GUARDIA

        public async Task<List<RondinHistorialItem>> GetHistorialGuardiaAsync(int guardiaID)
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

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
                      r.Estado >= 2
                      OR r.HoraInicio IS NOT NULL
                      OR EXISTS (
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
                    items.Add(new RondinHistorialItem
                    {
                        RondinID = reader.GetInt32(0),
                        TurnoID = reader.GetInt32(1),
                        HoraProgramada = reader.GetDateTime(2),
                        HoraInicio = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        HoraFin = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        Estado = reader.GetInt32(5),
                        PuntosVisitados = reader.GetInt32(6),
                        PuntosTotal = reader.GetInt32(7),
                        TotalIncidencias = reader.GetInt32(8),
                    });
            }

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
                                ID = reader.GetInt32(0),
                                Descripcion = reader.GetString(2),
                                FechaReporte = reader.GetDateTime(3),
                                NombrePunto = reader.GetString(4),
                            });
                    }
                }
            }

            const string qIncSueltas = @"
                SELECT i.ID, i.TurnoID, i.Descripcion, i.FechaReporte,
                       ISNULL(pc.Nombre, '') AS NombrePunto
                FROM TBL_ROCLAND_SECURITY_INCIDENCIAS i
                LEFT JOIN TBL_ROCLAND_SECURITY_PUNTOSCONTROL pc ON i.PuntoID = pc.ID
                WHERE i.RondinID IS NULL AND i.GuardiaReportaID = @guardiaID
                ORDER BY i.FechaReporte DESC";
            using var cmdS = new SqlCommand(qIncSueltas, conn);
            cmdS.Parameters.AddWithValue("@guardiaID", guardiaID);
            var incSueltas = new List<Incidencia>();
            using (var reader = await cmdS.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    incSueltas.Add(new Incidencia
                    {
                        ID = reader.GetInt32(0),
                        TurnoID = reader.GetInt32(1),
                        Descripcion = reader.GetString(2),
                        FechaReporte = reader.GetDateTime(3),
                        NombrePunto = reader.GetString(4),
                    });
            }

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
                    items.Add(new RondinHistorialItem
                    {
                        RondinID = -1,
                        HoraProgramada = inc.FechaReporte,
                        TurnoID = inc.TurnoID,
                        TotalIncidencias = 1,
                        IncidenciasSinRondin = new List<Incidencia> { inc },
                    });
                }
            }

            return items.OrderByDescending(i => i.HoraProgramada).ToList();
        }
    }
}