using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    /// Orquesta la sincronización entre SQLite local y SQL Server.
    ///
    /// POLÍTICA:
    /// - Guardia escribe SIEMPRE en local primero.
    /// - Sync sube al servidor lo que está pendiente.
    /// - "Local gana" para rondines (el guardia es el único escritor).
    /// - Datos del servidor (supervisor, puntos) sobrescriben local.
    /// - No se pierden datos aunque se cierre la app o se apague el teléfono.
    ///
    /// MOMENTOS DE SYNC:
    ///   1. Al abrir la app si hay conexión.
    ///   2. Al completar una acción crítica (finalizar rondín, incidencia).
    ///   3. Al reconectar (ConnectivityService.ConnectivityChanged).
    ///   4. Timer cada 5 minutos si hay conexión.
    ///   5. Antes de crear un nuevo turno (OfflineDatabaseService.CrearTurnoYRondinesAsync).

    public class SyncService
    {
        private readonly LocalDatabase _local;
        private readonly ConnectivityService _connectivity;
        private readonly string _connectionString;

        private bool _syncInProgress = false;
        private Timer? _timer;

        public event EventHandler<SyncResult>? SyncCompleted;

        public SyncService(LocalDatabase local, ConnectivityService connectivity,
            string connectionString)
        {
            _local = local;
            _connectivity = connectivity;
            _connectionString = connectionString;

            _connectivity.ConnectivityChanged += async (_, online) =>
            {
                if (online) await SincronizarAsync(SyncReason.Reconexion);
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // ARRANQUE DEL TIMER
        // ═══════════════════════════════════════════════════════════════

        public void IniciarTimerSync(int intervalMinutos = AppConfig.SyncTimerIntervaloMinutos)
        {
            _timer?.Dispose();
            _timer = new Timer(async _ =>
            {
                if (_connectivity.IsOnline)
                    await SincronizarAsync(SyncReason.Timer);
            }, null,
            TimeSpan.FromMinutes(intervalMinutos),
            TimeSpan.FromMinutes(intervalMinutos));
        }

        public void DetenerTimer() => _timer?.Dispose();

        // ═══════════════════════════════════════════════════════════════
        // SINCRONIZACIÓN PRINCIPAL
        // ═══════════════════════════════════════════════════════════════

        public async Task<SyncResult> SincronizarAsync(SyncReason razon = SyncReason.Manual)
        {
            if (_syncInProgress)
                return new SyncResult { Omitido = true };

            _syncInProgress = true;
            var result = new SyncResult { Razon = razon };

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // 1. DESCARGAR: catálogo de puntos → local
                await DescargarPuntosControlAsync(conn);
                result.PuntosDescargados = true;

                // 2. SUBIR: turnos finalizados offline → servidor
                //    (DEBE ir antes que rondines para que el servidor
                //     vea el turno en Estado=2 antes de recibir sus rondines)
                result.TurnosSincronizados = await SubirTurnosAsync(conn);

                // 3. SUBIR: rondines modificados offline → servidor
                result.RondinesSincronizados = await SubirRondinesAsync(conn);

                // 4. SUBIR: visitas a puntos offline → servidor
                result.PuntosSincronizados = await SubirVisitasPuntosAsync(conn);

                // 5. SUBIR: incidencias creadas offline → servidor
                result.IncidenciasSincronizadas = await SubirIncidenciasAsync(conn);

                // 6. LIMPIAR: datos viejos ya sincronizados
                await _local.LimpiarDatosViejosAsync();

                result.Exitoso = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                _syncInProgress = false;
                SyncCompleted?.Invoke(this, result);
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // DESCARGA: puntos de control
        // ═══════════════════════════════════════════════════════════════

        private async Task DescargarPuntosControlAsync(SqlConnection conn)
        {
            const string q = @"
                SELECT ID, Nombre, QRCode, Orden, Latitud, Longitud
                FROM TBL_ROCLAND_SECURITY_PUNTOSCONTROL ORDER BY Orden";
            using var cmd = new SqlCommand(q, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                await _local.UpsertPuntoControlAsync(new PuntoControlLocal
                {
                    ID = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    QRCode = reader.GetString(2),
                    Orden = reader.GetInt32(3),
                    Latitud = reader.GetDouble(4),
                    Longitud = reader.GetDouble(5),
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SUBIDA: turnos finalizados offline
        // ═══════════════════════════════════════════════════════════════

        private async Task<int> SubirTurnosAsync(SqlConnection conn)
        {
            // ─────────────────────────────────────────────────────────
            // FIX G: SYNC DE TURNOS — CRÍTICO PARA EL CRUCE DE DÍA
            //
            // PROBLEMA: Si el guardia termina el turno sin internet (o el
            // último rondín se auto-finalizó), el TurnoLocal queda con
            // Estado=2 y Sincronizado=false. El SyncService original ya
            // tenía este método, pero es fundamental que se ejecute ANTES
            // que SubirRondinesAsync para que cuando el servidor reciba los
            // rondines del turno, el turno ya esté en Estado=2. Así, al día
            // siguiente, la validación de CrearTurnoYRondinesAsync en el
            // servidor no encontrará ningún turno Estado IN (0,1).
            //
            // ADICIONALMENTE: Si el turno existe en el servidor pero con un
            // estado anterior (ej. Estado=1 cuando local tiene Estado=2),
            // forzamos el update. Esto resuelve el caso en que el servidor
            // y local quedaron desincronizados.
            // ─────────────────────────────────────────────────────────
            var pendientes = await _local.GetTurnosPendientesSyncAsync();
            int count = 0;

            foreach (var t in pendientes)
            {
                try
                {
                    // UPSERT defensivo: si el turno existe en servidor, actualizar.
                    // Si no existe (caso extremo de turno creado offline), insertar.
                    const string checkExiste = @"
                        SELECT COUNT(*) FROM TBL_ROCLAND_SECURITY_TURNOS WHERE ID = @id";
                    using var cmdCheck = new SqlCommand(checkExiste, conn);
                    cmdCheck.Parameters.AddWithValue("@id", t.ID);
                    int existe = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0);

                    if (existe > 0)
                    {
                        // El turno existe en servidor: solo actualizamos Estado
                        const string upd = @"
                            UPDATE TBL_ROCLAND_SECURITY_TURNOS
                            SET Estado = @estado
                            WHERE ID = @id";
                        using var cmdUpd = new SqlCommand(upd, conn);
                        cmdUpd.Parameters.AddWithValue("@id", t.ID);
                        cmdUpd.Parameters.AddWithValue("@estado", t.Estado);
                        await cmdUpd.ExecuteNonQueryAsync();
                    }
                    // Si no existe, no hacemos nada (los turnos siempre se crean online)

                    t.Sincronizado = true;
                    await _local.UpsertTurnoAsync(t);
                    count++;
                }
                catch { /* Reintento en próximo ciclo */ }
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // SUBIDA: rondines con estado modificado offline
        // ═══════════════════════════════════════════════════════════════

        private async Task<int> SubirRondinesAsync(SqlConnection conn)
        {
            var pendientes = await _local.GetRondinesPendientesSyncAsync();
            int count = 0;

            foreach (var r in pendientes)
            {
                try
                {
                    const string upd = @"
                        UPDATE TBL_ROCLAND_SECURITY_RONDINES
                        SET Estado            = @estado,
                            HoraInicio        = @horaInicio,
                            HoraFin           = @horaFin,
                            FechaModificacion = @fechaMod,
                            Sincronizado      = 1
                        WHERE ID = @id
                          AND (FechaModificacion IS NULL OR FechaModificacion <= @fechaMod)";

                    using var cmd = new SqlCommand(upd, conn);
                    cmd.Parameters.AddWithValue("@id", r.ID);
                    cmd.Parameters.AddWithValue("@estado", r.Estado);
                    cmd.Parameters.AddWithValue("@horaInicio", (object?)r.HoraInicio ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@horaFin", (object?)r.HoraFin ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fechaMod", r.FechaModificacion);
                    await cmd.ExecuteNonQueryAsync();

                    await _local.MarcarRondinSincronizadoAsync(r.ID);
                    count++;
                }
                catch { }
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // SUBIDA: visitas a puntos escaneados offline
        // ═══════════════════════════════════════════════════════════════

        private async Task<int> SubirVisitasPuntosAsync(SqlConnection conn)
        {
            var pendientes = await _local.GetPuntosPendientesSyncAsync();
            System.Diagnostics.Debug.WriteLine($"Subiendo {pendientes.Count} puntos pendientes.");
            int count = 0;

            foreach (var rp in pendientes)
            {
                try
                {
                    if (rp.ServerID > 0)
                    {
                        const string upd = @"
                            UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                            SET Estado            = @estado,
                                HoraVisita        = @hora,
                                LatitudG          = @lat,
                                LongitudG         = @lon,
                                FotoPath          = @foto,
                                Sincronizado      = 1,
                                FechaModificacion = @fechaMod
                            WHERE ID = @id";
                        using var cmd = new SqlCommand(upd, conn);
                        cmd.Parameters.AddWithValue("@id", rp.ServerID);
                        cmd.Parameters.AddWithValue("@estado", rp.Estado);
                        cmd.Parameters.AddWithValue("@hora", (object?)rp.HoraVisita ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@lat", (object?)rp.LatitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@lon", (object?)rp.LongitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fechaMod", rp.FechaModificacion);
                        var fotoParam = cmd.Parameters.Add("@foto", System.Data.SqlDbType.VarBinary);
                        fotoParam.Value = rp.FotoPath != null && rp.FotoPath.Length > 0
                            ? (object)rp.FotoPath : DBNull.Value;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        const string ins = @"
                            INSERT INTO TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                                (RondinID, PuntoID, HoraVisita, Estado, LatitudG, LongitudG, FotoPath,
                                 Sincronizado, FechaModificacion)
                            OUTPUT INSERTED.ID
                            VALUES (@rondinID, @puntoID, @hora, @estado, @lat, @lon, @foto, 1, @fechaMod)";
                        using var cmd = new SqlCommand(ins, conn);
                        cmd.Parameters.AddWithValue("@rondinID", rp.RondinID);
                        cmd.Parameters.AddWithValue("@puntoID", rp.PuntoID);
                        cmd.Parameters.AddWithValue("@hora", (object?)rp.HoraVisita ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@estado", rp.Estado);
                        cmd.Parameters.AddWithValue("@lat", (object?)rp.LatitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@lon", (object?)rp.LongitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fechaMod", rp.FechaModificacion);
                        var fotoParam = cmd.Parameters.Add("@foto", System.Data.SqlDbType.VarBinary);
                        fotoParam.Value = rp.FotoPath != null && rp.FotoPath.Length > 0
                            ? (object)rp.FotoPath : DBNull.Value;
                        var serverID = (int)(await cmd.ExecuteScalarAsync() ?? 0);
                        rp.ServerID = serverID;
                        rp.Sincronizado = true;
                        await _local.UpsertRondinPuntoAsync(rp);
                    }
                    await _local.MarcarPuntoSincronizadoAsync(rp.LocalID);
                    count++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Error subiendo punto {rp.LocalID}: {ex.Message} | " +
                        $"RondinID={rp.RondinID}, PuntoID={rp.PuntoID}, Estado={rp.Estado}");
                }
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // SUBIDA: incidencias creadas offline
        // ═══════════════════════════════════════════════════════════════

        private async Task<int> SubirIncidenciasAsync(SqlConnection conn)
        {
            var pendientes = await _local.GetIncidenciasPendientesSyncAsync();
            int count = 0;

            foreach (var inc in pendientes)
            {
                try
                {
                    const string ins = @"
                        INSERT INTO TBL_ROCLAND_SECURITY_INCIDENCIAS
                            (TurnoID, RondinID, PuntoID, GuardiaReportaID,
                             Descripcion, FotoPath, FechaReporte, Estado, Sincronizado, FechaModificacion)
                        OUTPUT INSERTED.ID
                        VALUES (@turnoID, @rondinID, @puntoID, @guardiaID,
                                @desc, @foto, @fecha, @estado, 1, @fechaMod)";

                    using var cmd = new SqlCommand(ins, conn);
                    cmd.Parameters.AddWithValue("@turnoID", inc.TurnoID);
                    cmd.Parameters.AddWithValue("@rondinID", (object?)inc.RondinID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@puntoID", (object?)inc.PuntoID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@guardiaID", inc.GuardiaReportaID);
                    cmd.Parameters.AddWithValue("@desc", inc.Descripcion);
                    cmd.Parameters.AddWithValue("@fecha", inc.FechaReporte);
                    cmd.Parameters.AddWithValue("@estado", inc.Estado);
                    cmd.Parameters.AddWithValue("@fechaMod", inc.FechaModificacion);
                    var fotoParam = cmd.Parameters.Add("@foto", System.Data.SqlDbType.VarBinary);
                    fotoParam.Value = inc.FotoPath != null && inc.FotoPath.Length > 0
                        ? (object)inc.FotoPath : DBNull.Value;

                    int serverID = (int)(await cmd.ExecuteScalarAsync() ?? 0);
                    await _local.MarcarIncidenciaSincronizadaAsync(inc.LocalID, serverID);
                    count++;
                }
                catch { }
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // CACHÉ DE USUARIO para login offline
        // ═══════════════════════════════════════════════════════════════

        public async Task CachearUsuarioAsync(SqlConnection conn, int usuarioID)
        {
            const string q = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, Activo
                FROM TBL_ROCLAND_SECURITY_USUARIOS WHERE ID = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", usuarioID);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                await _local.UpsertUsuarioAsync(new UsuarioLocal
                {
                    ID = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    UsuarioLogin = reader.GetString(2),
                    Contrasena = reader.GetString(3),
                    QRCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Rol = reader.GetInt32(5),
                    Activo = reader.GetBoolean(6),
                    FechaCacheada = DateTime.Now,
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DTO de resultado de sincronización
    // ═══════════════════════════════════════════════════════════════

    public class SyncResult
    {
        public bool Exitoso { get; set; }
        public bool Omitido { get; set; }
        public bool PuntosDescargados { get; set; }
        public int TurnosSincronizados { get; set; }
        public int RondinesSincronizados { get; set; }
        public int PuntosSincronizados { get; set; }
        public int IncidenciasSincronizadas { get; set; }
        public string? Error { get; set; }
        public SyncReason Razon { get; set; }

        public bool TienePendientes =>
            RondinesSincronizados + PuntosSincronizados + IncidenciasSincronizadas > 0;

        public string ResumenTexto =>
            Exitoso
                ? TienePendientes
                    ? $"Sync: {RondinesSincronizados}R · {PuntosSincronizados}P · {IncidenciasSincronizadas}I"
                    : "Sincronizado"
                : $"Error de sync: {Error}";
    }

    public enum SyncReason { Manual, Reconexion, AccionCritica, Timer, AlAbrir }
}