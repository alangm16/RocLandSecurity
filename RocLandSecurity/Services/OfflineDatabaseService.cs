using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    /// Fachada que reemplaza el uso directo de DatabaseService en las páginas.
    /// 
    /// REGLA PRINCIPAL:
    ///   - LECTURA:  Local primero. Si hay server, también.
    ///   - ESCRITURA: Siempre local. Si hay server, también. Si no, quedará en local.
    ///
    /// Las páginas solo llaman OfflineDatabaseService — no distinguen si hay internet.
    public class OfflineDatabaseService
    {
        private readonly GuardiaDatabaseService _server;
        private readonly SharedDatabaseService _sharedDatabase;
        private readonly LocalDatabase _local;
        private readonly ConnectivityService _connectivity;
        private readonly SyncService _sync;
        private readonly INotificationManagerService? _notificationService;

        public OfflineDatabaseService(GuardiaDatabaseService server, SharedDatabaseService sharedDatabase, LocalDatabase local,
            ConnectivityService connectivity, SyncService sync, INotificationManagerService? notificationService = null)
        {
            _server = server;
            _sharedDatabase = sharedDatabase;
            _local = local;
            _connectivity = connectivity;
            _sync = sync;
            _notificationService = notificationService;
        }

        // Añade este método para programar notificaciones
        private async Task ProgramarNotificacionesRondinesAsync(List<Rondin> rondines)
        {
            if (_notificationService == null) return;

            var ahora = DateTime.Now;

            foreach (var rondin in rondines.Where(r => r.Estado == 0)) // Solo pendientes
            {
                // Notificación de inicio (5 minutos antes)
                var horaInicioNotif = rondin.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
                if (horaInicioNotif > ahora)
                {
                    _notificationService.SendNotification(
                        "⏰ Rondín próximo a iniciar",
                        $"El rondín de las {rondin.HoraProgramada:HH:mm} hrs comenzará en {AppConfig.VentanaInicioAntesMinutos} minutos.",
                        horaInicioNotif,
                        "inicio",
                        rondin.ID);
                }

                // Notificación de finalización (5 minutos antes de la ventana de cierre)
                var horaFinNotif = rondin.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos - 5);
                if (horaFinNotif > ahora && horaFinNotif < rondin.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos))
                {
                    _notificationService.SendNotification(
                        "⚠️ Rondín por finalizar",
                        $"El rondín de las {rondin.HoraProgramada:HH:mm} hrs finaliza en 5 minutos. No olvides completarlo.",
                        horaFinNotif,
                        "fin",
                        rondin.ID);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // AUTENTICACIÓN
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Login con fallback offline.
        /// Online: valida con servidor y cachea credenciales.
        /// Offline: usa credenciales cacheadas en SQLite.
        /// </summary>
        public async Task<(Usuario? usuario, bool fueOffline)> LoginAsync(
            string usuario, string hashContrasena)
        {
            bool online = await _connectivity.CheckServerAsync();

            if (online)
            {
                var user = await _sharedDatabase.GetUsuarioByLoginAsync(usuario, hashContrasena);
                if (user != null)
                {
                    // Cachear para login offline futuro
                    try
                    {
                        using var conn = new SqlConnection(
                            _server.GetConnectionString());
                        await conn.OpenAsync();
                        await _sync.CachearUsuarioAsync(conn, user.ID);
                    }
                    catch { /* No crítico */ }
                }
                return (user, false);
            }
            else
            {
                // Offline: buscar en caché local
                var local = await _local.GetUsuarioByLoginAsync(usuario, hashContrasena);
                if (local == null) return (null, true);

                return (MapUsuario(local), true);
            }
        }

        /// <summary>Login por QR con fallback offline.</summary>
        public async Task<(Usuario? usuario, bool fueOffline)> LoginQRAsync(string qrCode)
        {
            bool online = await _connectivity.CheckServerAsync();

            if (online)
            {
                var user = await _sharedDatabase.GetUsuarioByQRAsync(qrCode);
                if (user != null)
                {
                    try
                    {
                        using var conn = new SqlConnection(_server.GetConnectionString());
                        await conn.OpenAsync();
                        await _sync.CachearUsuarioAsync(conn, user.ID);
                    }
                    catch { }
                }
                return (user, false);
            }
            else
            {
                var local = await _local.GetUsuarioByQRAsync(qrCode);
                return local == null ? (null, true) : (MapUsuario(local), true);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // TURNO
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Obtiene turno activo. Requiere conexión para crear turnos nuevos.
        /// Si hay turno en local, lo devuelve aunque esté offline.
        /// </summary>
        public async Task<Turno?> GetTurnoActivoAsync(int guardiaID)
        {
            // Siempre verificar local primero (tiene el estado más actualizado)
            var local = await _local.GetTurnoActivoAsync(guardiaID);
            if (local != null) return MapTurno(local);

            // Si no hay local, intentar del servidor
            if (await _connectivity.CheckServerAsync())
            {
                var turno = await _server.GetTurnoActivoAsync(guardiaID);
                if (turno != null)
                    await _local.UpsertTurnoAsync(MapTurnoLocal(turno));
                return turno;
            }

            return null;
        }

        /// <summary>Crear turno — requiere conexión.</summary>
        public async Task<Turno> CrearTurnoYRondinesAsync(int guardiaID)
        {
            if (!await _connectivity.CheckServerAsync())
                throw new InvalidOperationException(
                    "Se requiere conexión para iniciar un nuevo turno.");

            var turno = await _server.CrearTurnoYRondinesAsync(guardiaID);

            // Cachear localmente
            await _local.UpsertTurnoAsync(MapTurnoLocal(turno));

            // Cachear rondines
            var rondines = await _server.GetRondinesPorTurnoAsync(turno.ID);
            foreach (var r in rondines)
                await _local.UpsertRondinAsync(MapRondinLocal(r));

            // Programar notificaciones para los rondines pendientes
            await ProgramarNotificacionesRondinesAsync(rondines);

            return turno;
        }

        // ─────────────────────────────────────────────────────────────────
        // RONDINES
        // ─────────────────────────────────────────────────────────────────

        public async Task<List<Rondin>> GetRondinesPorTurnoAsync(int turnoID)
        {
            // Local siempre tiene el estado más fresco (guardia escribe aquí primero)
            var locales = await _local.GetRondinesPorTurnoAsync(turnoID);
            if (locales.Count > 0) return locales.Select(MapRondin).ToList();

            // Si no hay local, bajar del servidor y cachear
            if (await _connectivity.CheckServerAsync())
            {
                var lista = await _server.GetRondinesPorTurnoAsync(turnoID);
                foreach (var r in lista)
                    await _local.UpsertRondinAsync(MapRondinLocal(r, sincronizado: true));
                return lista;
            }

            return new List<Rondin>();
        }

        public async Task<(DateTime HoraProgramada, int TurnoID)> GetDatosRondinAsync(int rondinID)
        {
            var local = await _local.GetRondinPorIDAsync(rondinID);
            if (local != null) return (local.HoraProgramada, local.TurnoID);

            if (await _connectivity.CheckServerAsync())
                return await _server.GetDatosRondinAsync(rondinID);

            return (DateTime.Now, 0);
        }

        /// <summary>
        /// Iniciar rondín: escribe en local siempre, sube a servidor si hay red.
        /// La validación de horario se realiza en el servidor usando AppConfig.
        /// </summary>
        public async Task IniciarRondinAsync(int rondinID)
        {
            var local = await _local.GetRondinPorIDAsync(rondinID);
            if (local == null) throw new InvalidOperationException("Rondín no encontrado.");
            if (local.Estado >= 1) return; // Ya iniciado, no hacer nada

            // ── Validar horario ANTES de escribir, usando la misma lógica que el servidor ──
            if (AppConfig.ModoEstrictoRondines)
            {
                var ahora = DateTime.Now;
                var apertura = local.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
                var cierre = local.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);

                if (ahora < apertura)
                    throw new InvalidOperationException(
                        $"El rondín aún no está disponible. " +
                        $"Disponible desde las {apertura:HH:mm} hrs.");

                if (ahora > cierre)
                    throw new InvalidOperationException(
                        $"El rondín de las {local.HoraProgramada:HH:mm} ya no puede iniciarse. " +
                        $"El tiempo límite fue {cierre:HH:mm} hrs.");
            }

            // ── Validar que no haya otro rondín en progreso ──
            var rondinesDelTurno = await _local.GetRondinesPorTurnoAsync(local.TurnoID);
            bool hayOtroEnProgreso = rondinesDelTurno.Any(r => r.Estado == 1 && r.ID != rondinID);
            if (hayOtroEnProgreso)
                throw new InvalidOperationException(
                    "Ya hay un rondín en progreso. Finalízalo antes de iniciar otro.");

            // ── Solo después de pasar las validaciones, escribir en local ──
            local.Estado = 1;
            local.HoraInicio = DateTime.Now;
            local.Sincronizado = false;
            local.FechaModificacion = DateTime.Now;
            await _local.UpsertRondinAsync(local);

            // ── Intentar subir al servidor (errores no críticos) ──
            if (await _connectivity.CheckServerAsync())
            {
                try { await _server.IniciarRondinAsync(rondinID); }
                catch { /* El registro local ya es válido; se sincronizará después */ }
            }
        }

        /// <summary>
        /// Revisa todos los rondines del turno y cierra automáticamente los que hayan
        /// superado su ventana de tiempo. Se llama desde GuardiaHomePage antes de renderizar.
        ///
        /// Casos cubiertos:
        ///   A) Pendiente (Estado=0) cuyo cierre ya pasó   → Estado=3, 0 puntos visitados.
        ///   B) En progreso (Estado=1) cuyo cierre ya pasó → Estado=3, puntos visitados hasta ese momento.
        ///
        /// Devuelve la cantidad de rondines que fueron cerrados, para que la UI pueda
        /// mostrar un aviso si lo desea.
        /// </summary>
        public async Task<int> ExpirarRondinesVencidosAsync(int turnoID)
        {
            if (!AppConfig.ModoEstrictoRondines) return 0;

            var rondines = await _local.GetRondinesPorTurnoAsync(turnoID);
            var ahora = DateTime.Now;
            int cerrados = 0;

            foreach (var r in rondines)
            {
                // Solo aplica a rondines aún "abiertos"
                if (r.Estado >= 2) continue;

                var cierre = r.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);
                if (ahora <= cierre) continue;

                // El rondín venció — calcular estado final
                var puntos = await _local.GetPuntosDeRondinAsync(r.ID);
                r.Estado = 3; // Incompleto
                r.HoraFin = r.HoraFin ?? cierre; // Usar hora de cierre si aún no tiene
                r.PuntosTotal = puntos.Count > 0 ? puntos.Count : r.PuntosTotal;
                r.PuntosVisitados = puntos.Count(p => p.Estado == 1);
                r.Sincronizado = false;
                r.FechaModificacion = ahora;
                await _local.UpsertRondinAsync(r);

                // Marcar puntos pendientes como omitidos en local
                foreach (var p in puntos.Where(p => p.Estado == 0))
                {
                    p.Estado = 2; // Omitido
                    p.Sincronizado = false;
                    p.FechaModificacion = ahora;
                    await _local.UpsertRondinPuntoAsync(p);
                }

                // Intentar persistir en servidor
                if (await _connectivity.CheckServerAsync())
                {
                    try { await _server.FinalizarRondinAsync(r.ID); }
                    catch { /* Se sincronizará en el siguiente ciclo */ }
                }

                cerrados++;
            }

            return cerrados;
        }

        public async Task<int> AsegurarPuntosRondinAsync(int rondinID)
        {
            // Verificar si ya hay puntos en local
            var puntosLocal = await _local.GetPuntosDeRondinAsync(rondinID);
            if (puntosLocal.Count > 0) return puntosLocal.Count;

            // Intentar del servidor
            if (await _connectivity.CheckServerAsync())
            {
                int total = await _server.AsegurarPuntosRondinAsync(rondinID);
                var puntos = await _server.GetPuntosDeRondinAsync(rondinID);

                // Obtener QRCode de cada punto del catálogo local
                var catalogo = await _local.GetPuntosControlAsync();
                var qrMap = catalogo.ToDictionary(p => p.ID, p => p.QRCode);

                foreach (var p in puntos)
                {
                    await _local.UpsertRondinPuntoAsync(new RondinPuntoLocal
                    {
                        ServerID = p.ID,
                        RondinID = p.RondinID,
                        PuntoID = p.PuntoID,
                        NombrePunto = p.NombrePunto,
                        QRCode = qrMap.GetValueOrDefault(p.PuntoID, ""),
                        OrdenPunto = p.OrdenPunto,
                        Estado = p.Estado,
                        Sincronizado = true,
                    });
                }

                // Actualizar contadores en el rondín local
                await ActualizarContadoresRondinAsync(rondinID);
                return total;
            }

            // Sin red y sin local — usar catálogo de puntos cacheado
            var puntosControl = await _local.GetPuntosControlAsync();
            if (puntosControl.Count == 0)
                throw new InvalidOperationException(
                    "Sin puntos de control disponibles. Conecta a la red al menos una vez.");

            var rondinLocal = await _local.GetRondinPorIDAsync(rondinID);
            foreach (var pc in puntosControl)
            {
                await _local.UpsertRondinPuntoAsync(new RondinPuntoLocal
                {
                    ServerID = 0,  // Sin ServerID hasta que se sincronice
                    RondinID = rondinID,
                    PuntoID = pc.ID,
                    NombrePunto = pc.Nombre,
                    QRCode = pc.QRCode,
                    OrdenPunto = pc.Orden,
                    Estado = 0,
                    Sincronizado = false,
                });
            }
            return puntosControl.Count;
        }

        public async Task<List<RondinPunto>> GetPuntosDeRondinAsync(int rondinID)
        {
            var local = await _local.GetPuntosDeRondinAsync(rondinID);
            return local.Select(MapRondinPunto).ToList();
        }

        public async Task<RondinPunto?> GetRondinPuntoPorQRAsync(int rondinID, string qrCode)
        {
            // Buscar en local por QR
            var local = await _local.GetRondinPuntoPorQRAsync(rondinID, qrCode);
            return local != null ? MapRondinPunto(local) : null;
        }

        /// <summary>
        /// Registrar visita: escribe en local primero, intenta servidor.
        /// </summary>
        public async Task<bool> RegistrarVisitaPuntoAsync(
            int rondinPuntoServerID, double? lat, double? lon,
            int rondinID = 0, string qrCode = "")
        {
            // Buscar el registro local: por QR primero (más eficiente), luego por ServerID
            RondinPuntoLocal? local = null;
            if (!string.IsNullOrEmpty(qrCode))
                local = await _local.GetRondinPuntoPorQRAsync(rondinID, qrCode);
            if (local == null && rondinPuntoServerID > 0)
                local = (await _local.GetPuntosDeRondinAsync(rondinID))
                      .FirstOrDefault(p => p.ServerID == rondinPuntoServerID);

            if (local != null)
            {
                local.Estado = 1;
                local.HoraVisita = DateTime.Now;
                local.LatitudG = lat;
                local.LongitudG = lon;
                local.Sincronizado = false;
                local.FechaModificacion = DateTime.Now;
                await _local.UpsertRondinPuntoAsync(local);

                // Actualizar contadores del rondín inmediatamente
                await ActualizarContadoresRondinAsync(local.RondinID);
            }

            // Intentar servidor
            if (await _connectivity.CheckServerAsync() && rondinPuntoServerID > 0)
            {
                try
                {
                    await _server.RegistrarVisitaPuntoAsync(rondinPuntoServerID, lat, lon);
                    if (local != null)
                    {
                        local.Sincronizado = true;
                        await _local.UpsertRondinPuntoAsync(local);
                    }
                    return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Obtiene el punto de rondín local a partir del ServerID.
        /// </summary>
        public async Task<RondinPuntoLocal?> GetRondinPuntoLocalPorServerIDAsync(int serverID)
        {
            return await _local.GetRondinPuntoPorServerIDAsync(serverID);
        }

        public async Task GuardarFotoPuntoAsync(int localID, byte[] fotoBytes)
        {
            var puntoLocal = await _local.GetRondinPuntoPorLocalIDAsync(localID);
            if (puntoLocal == null) throw new InvalidOperationException("Punto no encontrado.");

            puntoLocal.FotoPath = fotoBytes;
            puntoLocal.Sincronizado = false; // Marcar para sincronizar
            puntoLocal.FechaModificacion = DateTime.Now;
            await _local.UpsertRondinPuntoAsync(puntoLocal);

            // Intentar subir al servidor si hay conexión
            if (await _connectivity.CheckServerAsync())
            {
                try
                {
                    await _server.ActualizarFotoPuntoAsync(puntoLocal.ServerID, fotoBytes);
                    puntoLocal.Sincronizado = true;
                    await _local.UpsertRondinPuntoAsync(puntoLocal);
                }
                catch { /* Error no crítico, se sincronizará después */ }
            }
        }

        /// <summary>Finalizar rondín: local + servidor + sync crítico.</summary>
        public async Task FinalizarRondinAsync(int rondinID)
        {
            // Actualizar local
            var local = await _local.GetRondinPorIDAsync(rondinID);
            if (local != null)
            {
                var puntos = await _local.GetPuntosDeRondinAsync(rondinID);
                bool todos = puntos.All(p => p.Estado == 1);
                local.Estado = todos ? 2 : 3;
                local.HoraFin = DateTime.Now;
                local.PuntosTotal = puntos.Count;
                local.PuntosVisitados = puntos.Count(p => p.Estado == 1);
                local.Sincronizado = false;
                local.FechaModificacion = DateTime.Now;
                await _local.UpsertRondinAsync(local);
            }

            // Intentar servidor
            if (await _connectivity.CheckServerAsync())
            {
                try
                {
                    await _server.FinalizarRondinAsync(rondinID);
                    if (local != null)
                    {
                        local.Sincronizado = true;
                        await _local.UpsertRondinAsync(local);
                    }
                }
                catch { }

                // Sync completo de pendientes como acción crítica
                _ = Task.Run(async () =>
                    await _sync.SincronizarAsync(SyncReason.AccionCritica));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // INCIDENCIAS
        // ─────────────────────────────────────────────────────────────────

        public async Task CrearIncidenciaAsync(Incidencia inc)
        {
            // Siempre guardar en local
            var localInc = new IncidenciaLocal
            {
                TurnoID = inc.TurnoID,
                RondinID = inc.RondinID,
                PuntoID = inc.PuntoID,
                GuardiaReportaID = inc.GuardiaReportaID,
                Descripcion = inc.Descripcion,
                FechaReporte = inc.FechaReporte,
                Estado = 0,
                Sincronizado = false,
                FechaModificacion = DateTime.Now,
            };
            await _local.InsertIncidenciaAsync(localInc);

            // Intentar servidor
            if (await _connectivity.CheckServerAsync())
            {
                try
                {
                    await _server.CrearIncidenciaAsync(inc);
                    await _local.MarcarIncidenciaSincronizadaAsync(localInc.LocalID, 0);
                }
                catch { }
            }
        }

        /// <summary>
        /// Obtiene el catálogo de puntos de control.
        /// Online: del servidor (más actualizado).
        /// Offline: del catálogo local cacheado en SQLite.
        /// </summary>
        public async Task<List<PuntoControl>> GetPuntosControlAsync()
        {
            if (await _connectivity.CheckServerAsync())
            {
                var lista = await _sharedDatabase.GetPuntosControlAsync();
                // Actualizar caché local con los más recientes
                foreach (var p in lista)
                    await _local.UpsertPuntoControlAsync(new PuntoControlLocal
                    {
                        ID = p.ID,
                        Nombre = p.Nombre,
                        QRCode = p.QRCode,
                        Orden = p.Orden,
                        Latitud = p.Latitud,
                        Longitud = p.Longitud,
                    });
                return lista;
            }
            // Offline: caché local
            var locales = await _local.GetPuntosControlAsync();
            return locales.Select(p => new PuntoControl
            {
                ID = p.ID,
                Nombre = p.Nombre,
                QRCode = p.QRCode,
                Orden = p.Orden,
                Latitud = p.Latitud,
                Longitud = p.Longitud,
            }).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        // ESTADO DE PENDIENTES (para mostrar badge en UI)
        // ─────────────────────────────────────────────────────────────────

        public async Task<int> GetTotalPendientesSyncAsync()
        {
            var r = (await _local.GetRondinesPendientesSyncAsync()).Count;
            var rp = (await _local.GetPuntosPendientesSyncAsync()).Count;
            var i = (await _local.GetIncidenciasPendientesSyncAsync()).Count;
            return r + rp + i;
        }

        // ─────────────────────────────────────────────────────────────────
        // HISTORIAL GUARDIA — OFFLINE
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Historial del guardia construido desde SQLite local.
        /// Usado cuando no hay conexión al servidor.
        /// Solo muestra datos del turno activo local.
        /// </summary>
        public async Task<List<RondinHistorialItem>> GetHistorialGuardiaLocalAsync(int guardiaID)
        {
            var turno = await _local.GetTurnoActivoAsync(guardiaID);
            if (turno == null) return new List<RondinHistorialItem>();

            var rondines = await _local.GetRondinesPorTurnoAsync(turno.ID);
            var items = new List<RondinHistorialItem>();

            foreach (var r in rondines.Where(r => r.Estado >= 1 || r.HoraInicio.HasValue))
            {
                var puntos = await _local.GetPuntosDeRondinAsync(r.ID);
                var incidencias = await _local.GetIncidenciasPorRondinAsync(r.ID);

                items.Add(new RondinHistorialItem
                {
                    RondinID = r.ID,
                    TurnoID = r.TurnoID,
                    HoraProgramada = r.HoraProgramada,
                    HoraInicio = r.HoraInicio,
                    HoraFin = r.HoraFin,
                    Estado = r.Estado,
                    PuntosVisitados = puntos.Count(p => p.Estado == 1),
                    PuntosTotal = puntos.Count,
                    TotalIncidencias = incidencias.Count,
                    Incidencias = incidencias.Select(i => new Incidencia
                    {
                        ID = i.LocalID,
                        Descripcion = i.Descripcion,
                        FechaReporte = i.FechaReporte,
                    }).ToList(),
                });
            }

            return items.OrderByDescending(i => i.HoraProgramada).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Recalcula PuntosTotal y PuntosVisitados del rondín local
        /// a partir de los puntos en SQLite. Mantiene GuardiaHomePage actualizado.
        /// </summary>
        private async Task ActualizarContadoresRondinAsync(int rondinID)
        {
            var rondin = await _local.GetRondinPorIDAsync(rondinID);
            if (rondin == null) return;
            var puntos = await _local.GetPuntosDeRondinAsync(rondinID);
            rondin.PuntosTotal = puntos.Count;
            rondin.PuntosVisitados = puntos.Count(p => p.Estado == 1);
            await _local.UpsertRondinAsync(rondin);
        }

        // ─────────────────────────────────────────────────────────────────
        // MAPPERS
        // ─────────────────────────────────────────────────────────────────

        private static Usuario MapUsuario(UsuarioLocal u) => new()
        {
            ID = u.ID,
            Nombre = u.Nombre,
            UsuarioLogin = u.UsuarioLogin,
            Contraseña = u.Contrasena,
            QRCode = u.QRCode,
            Rol = u.Rol,
            Activo = u.Activo,
        };

        private static Turno MapTurno(TurnoLocal t) => new()
        {
            ID = t.ID,
            GuardiaID = t.GuardiaID,
            Fecha = DateOnly.FromDateTime(t.Fecha),
            HoraInicio = TimeOnly.FromTimeSpan(t.HoraInicio),
            HoraFin = TimeOnly.FromTimeSpan(t.HoraFin),
        };

        private static TurnoLocal MapTurnoLocal(Turno t) => new()
        {
            ID = t.ID,
            GuardiaID = t.GuardiaID,
            Fecha = t.Fecha.ToDateTime(TimeOnly.MinValue),
            HoraInicio = t.HoraInicio.ToTimeSpan(),
            HoraFin = t.HoraFin.ToTimeSpan(),
            Sincronizado = true,
        };

        private static Rondin MapRondin(RondinLocal r) => new()
        {
            ID = r.ID,
            TurnoID = r.TurnoID,
            GuardiaID = r.GuardiaID,
            HoraProgramada = r.HoraProgramada,
            HoraInicio = r.HoraInicio,
            HoraFin = r.HoraFin,
            Estado = r.Estado,
            PuntosTotal = r.PuntosTotal,
            PuntosVisitados = r.PuntosVisitados,
            Sincronizado = r.Sincronizado,
        };

        private static RondinLocal MapRondinLocal(Rondin r, bool sincronizado = false) => new()
        {
            ID = r.ID,
            TurnoID = r.TurnoID,
            GuardiaID = r.GuardiaID,
            HoraProgramada = r.HoraProgramada,
            HoraInicio = r.HoraInicio,
            HoraFin = r.HoraFin,
            Estado = r.Estado,
            PuntosTotal = r.PuntosTotal,
            PuntosVisitados = r.PuntosVisitados,
            Sincronizado = sincronizado,
            FechaModificacion = DateTime.Now,
        };

        private static RondinPunto MapRondinPunto(RondinPuntoLocal rp) => new()
        {
            ID = rp.ServerID > 0 ? rp.ServerID : rp.LocalID,
            RondinID = rp.RondinID,
            PuntoID = rp.PuntoID,
            NombrePunto = rp.NombrePunto,
            OrdenPunto = rp.OrdenPunto,
            HoraVisita = rp.HoraVisita,
            Estado = rp.Estado,
            LatitudG = rp.LatitudG,
            LongitudG = rp.LongitudG,
            FotoPath = rp.FotoPath,
            Sincronizado = rp.Sincronizado,
        };
    }
}