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

            // Sincronizar cuando se recupere la conexión
            _connectivity.ConnectivityChanged += async (_, online) =>
            {
                if (online) await SincronizarAsync(SyncReason.Reconexion);
            };
        }

        // ARRANQUE DEL TIMER

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

        // SINCRONIZACIÓN PRINCIPAL

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

                // 1. DESCARGAR: catálogo de puntos y datos del servidor → local
                await DescargarPuntosControlAsync(conn);
                result.PuntosDescargados = true;

                // 2. SUBIR: rondines modificados offline → servidor
                result.RondinesSincronizados = await SubirRondinesAsync(conn);

                // 3. SUBIR: visitas a puntos offline → servidor
                result.PuntosSincronizados = await SubirVisitasPuntosAsync(conn);

                // 4. SUBIR: incidencias creadas offline → servidor
                result.IncidenciasSincronizadas = await SubirIncidenciasAsync(conn);

                // 5. LIMPIAR: datos viejos ya sincronizados
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

        // DESCARGA: puntos de control (catálogo base)

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

        // SUBIDA: rondines con estado modificado offline

        private async Task<int> SubirRondinesAsync(SqlConnection conn)
        {
            var pendientes = await _local.GetRondinesPendientesSyncAsync();
            int count = 0;

            foreach (var r in pendientes)
            {
                try
                {
                    // UPDATE: el rondín ya existe en el servidor (se crea siempre online)
                    const string upd = @"
                        UPDATE TBL_ROCLAND_SECURITY_RONDINES
                        SET Estado = @estado,
                            HoraInicio = @horaInicio,
                            HoraFin    = @horaFin,
                            FechaModificacion = @fechaMod,
                            Sincronizado = 1
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
                catch { /* Reintento en próximo ciclo */ }
            }
            return count;
        }

        // SUBIDA: visitas a puntos escaneados offline

        private async Task<int> SubirVisitasPuntosAsync(SqlConnection conn)
        {
            var pendientes = await _local.GetPuntosPendientesSyncAsync(); 
            int count = 0;

            foreach (var rp in pendientes)
            {
                try
                {
                    if (rp.ServerID > 0)
                    {
                        const string upd = @"
                    UPDATE TBL_ROCLAND_SECURITY_RONDINESPUNTOS
                    SET Estado = @estado, HoraVisita = @hora,
                        LatitudG = @lat, LongitudG = @lon,
                        FotoPath = @foto,
                        Sincronizado = 1, FechaModificacion = @fechaMod
                    WHERE ID = @id";
                        using var cmd = new SqlCommand(upd, conn);
                        cmd.Parameters.AddWithValue("@id", rp.ServerID);
                        cmd.Parameters.AddWithValue("@estado", rp.Estado);
                        cmd.Parameters.AddWithValue("@hora", (object?)rp.HoraVisita ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@lat", (object?)rp.LatitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@lon", (object?)rp.LongitudG ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@foto", (object?)rp.FotoPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fechaMod", rp.FechaModificacion);
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
                        cmd.Parameters.AddWithValue("@foto", (object?)rp.FotoPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fechaMod", rp.FechaModificacion);
                        var serverID = (int)(await cmd.ExecuteScalarAsync() ?? 0);
                        rp.ServerID = serverID;
                    }

                    await _local.MarcarPuntoSincronizadoAsync(rp.LocalID);
                    count++;
                }
                catch { /* Reintento en próximo ciclo */ }
            }
            return count;
        }

        // SUBIDA: incidencias creadas offline

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
                             Descripcion, FechaReporte, Estado, Sincronizado, FechaModificacion)
                        OUTPUT INSERTED.ID
                        VALUES (@turnoID, @rondinID, @puntoID, @guardiaID,
                                @desc, @fecha, @estado, 1, @fechaMod)";

                    using var cmd = new SqlCommand(ins, conn);
                    cmd.Parameters.AddWithValue("@turnoID", inc.TurnoID);
                    cmd.Parameters.AddWithValue("@rondinID", (object?)inc.RondinID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@puntoID", (object?)inc.PuntoID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@guardiaID", inc.GuardiaReportaID);
                    cmd.Parameters.AddWithValue("@desc", inc.Descripcion);
                    cmd.Parameters.AddWithValue("@fecha", inc.FechaReporte);
                    cmd.Parameters.AddWithValue("@estado", inc.Estado);
                    cmd.Parameters.AddWithValue("@fechaMod", inc.FechaModificacion);

                    int serverID = (int)(await cmd.ExecuteScalarAsync() ?? 0);
                    await _local.MarcarIncidenciaSincronizadaAsync(inc.LocalID, serverID);
                    count++;
                }
                catch { /* Reintento en próximo ciclo */ }
            }
            return count;
        }

        // CACHÉ DE USUARIO para login offline

        /// Descarga y cachea las credenciales del usuario autenticado
        /// para permitir login offline la próxima vez.

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

    // DTO de resultado de sincronización

    public class SyncResult
    {
        public bool Exitoso { get; set; }
        public bool Omitido { get; set; }
        public bool PuntosDescargados { get; set; }
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