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

        public OfflineDatabaseService(GuardiaDatabaseService server, SharedDatabaseService sharedDatabase,
            LocalDatabase local, ConnectivityService connectivity, SyncService sync,
            INotificationManagerService? notificationService = null)
        {
            _server = server;
            _sharedDatabase = sharedDatabase;
            _local = local;
            _connectivity = connectivity;
            _sync = sync;
            _notificationService = notificationService;
        }

        private async Task ProgramarNotificacionesRondinesAsync(List<Rondin> rondines)
        {
            if (_notificationService == null) return;
            var ahora = DateTime.Now;
            foreach (var rondin in rondines.Where(r => r.Estado == 0))
            {
                var horaInicioNotif = rondin.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
                if (horaInicioNotif > ahora)
                    _notificationService.SendNotification(
                        "⏰ Rondín próximo a iniciar",
                        $"El rondín de las {rondin.HoraProgramada:HH:mm} hrs comenzará en {AppConfig.VentanaInicioAntesMinutos} minutos.",
                        horaInicioNotif, "inicio", rondin.ID);

                var horaFinNotif = rondin.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos - 5);
                if (horaFinNotif > ahora)
                    _notificationService.SendNotification(
                        "⚠️ Rondín por finalizar",
                        $"El rondín de las {rondin.HoraProgramada:HH:mm} finaliza en 5 minutos.",
                        horaFinNotif, "fin", rondin.ID);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // AUTENTICACIÓN
        // ═══════════════════════════════════════════════════════════════

        public async Task<(Usuario? usuario, bool fueOffline)> LoginAsync(
            string usuario, string hashContrasena)
        {
            bool online = await _connectivity.CheckServerAsync();
            if (online)
            {
                var user = await _sharedDatabase.GetUsuarioByLoginAsync(usuario, hashContrasena);
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
                var local = await _local.GetUsuarioByLoginAsync(usuario, hashContrasena);
                if (local == null) return (null, true);
                return (MapUsuario(local), true);
            }
        }

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

        // ═══════════════════════════════════════════════════════════════
        // TURNO
        // ═══════════════════════════════════════════════════════════════

        public async Task<Turno?> GetTurnoActivoAsync(int guardiaID)
        {
            // Paso 1: revisar y auto-expirar en local
            var local = await _local.GetTurnoActivoAsync(guardiaID);

            // Paso 2: si hay conexión, la fuente de verdad es el servidor
            if (await _connectivity.CheckServerAsync())
            {
                var turnoServidor = await _server.GetTurnoActivoAsync(guardiaID);

                if (turnoServidor != null)
                {
                    // El servidor tiene un turno activo: actualizar local y devolver
                    await _local.UpsertTurnoAsync(MapTurnoLocal(turnoServidor));
                    return turnoServidor;
                }
                else
                {
                    // El servidor no tiene turno activo: si local tenía uno, cerrarlo
                    if (local != null)
                        await _local.FinalizarTurnoLocalAsync(local.ID);
                    return null;
                }
            }

            // Sin conexión: confiar en local (ya fue auto-expirado arriba si era necesario)
            return local != null ? MapTurno(local) : null;
        }

        public async Task<Turno> CrearTurnoYRondinesAsync(int guardiaID)
        {
            // ─────────────────────────────────────────────────────────
            // FIX E: VALIDACIÓN DE HORARIO CORREGIDA (bug lógico &&)
            //
            // PROBLEMA ORIGINAL: La condición usaba AND (&&) entre dos
            // comparaciones de tiempo que nunca podían ser verdaderas al
            // mismo tiempo para el turno nocturno, haciendo que la
            // validación nunca disparara y permitiendo crear turnos en
            // horarios incorrectos.
            //
            //   CÓDIGO BUGGY:
            //     if (horaActual > fin && horaActual < inicio.Add(-2h))
            //
            //   Para el turno 19:00-07:00, 'fin'=07:00 e 'inicio-2h'=17:00.
            //   Para que ambas condiciones sean true simultáneamente, la hora
            //   actual debería ser > 07:00 Y < 17:00 al mismo tiempo — eso
            //   sí es posible, pero la ventana resultante (07:01-16:59) es
            //   el único período BLOQUEADO. Fuera de esa ventana, el guardia
            //   podría crear turno siempre, incluyendo a las 03:00 del lunes
            //   cuando ya existe el del domingo. El fallo se manifestaba en
            //   que la validación de horario pasaba, pero luego el servidor
            //   rechazaba por duplicado.
            //
            //   CÓDIGO CORRECTO:
            //     El período "muerto" (donde NO se puede crear turno) para un
            //     turno nocturno 19:00-07:00 es: de 07:01 a 16:59 (2 horas
            //     antes del inicio). Se bloquea con OR porque basta con que
            //     cumpla UNA de las condiciones extremas.
            // ─────────────────────────────────────────────────────────
            TimeSpan horaActual = DateTime.Now.TimeOfDay;
            TimeSpan inicio = TimeSpan.Parse(AppConfig.HoraInicioTurno);
            TimeSpan fin = TimeSpan.Parse(AppConfig.HoraFinTurno);

            // Configura aquí tu tolerancia. 
            // Para ser estricto a las 19:00 usa TimeSpan.Zero
            // Para permitir ingresar 15 minutos antes usa TimeSpan.FromMinutes(15)
            TimeSpan toleranciaPreIngreso = TimeSpan.Zero;

            if (!AppConfig.TurnoCruzaMedianoche)
            {
                // Turno de día (ej. 08:00-18:00)
                if (horaActual > fin)
                    throw new InvalidOperationException(
                        $"El turno finalizó a las {AppConfig.HoraFinTurno} hrs. Ya no puedes iniciarlo.");

                if (horaActual < inicio.Subtract(toleranciaPreIngreso))
                    throw new InvalidOperationException(
                        $"Aún es muy temprano. El turno de hoy inicia a las {AppConfig.HoraInicioTurno} hrs.");
            }
            else
            {
                // Turno nocturno (ej. 19:00-07:00)
                TimeSpan preIngreso = inicio.Subtract(toleranciaPreIngreso);

                if (horaActual > fin && horaActual < preIngreso)
                    throw new InvalidOperationException(
                        $"Fuera de horario. El turno nocturno inicia a las {AppConfig.HoraInicioTurno} hrs.");
            }

            // ─────────────────────────────────────────────────────────
            // FIX F: SINCRONIZAR PRIMERO, LUEGO CREAR
            //
            // PROBLEMA: Si el guardia finalizó el turno anterior en modo
            // offline, el servidor aún lo tiene como Estado=1. Al intentar
            // crear el nuevo turno, el servidor lanza "Ya existe un turno
            // pendiente". GuardiaDatabaseService.CrearTurnoYRondinesAsync
            // ya hace el auto-cierre del lado del servidor (FIX A), pero
            // también disparamos un sync aquí para asegurarnos de que
            // cualquier dato offline pendiente (estado final del turno
            // anterior, rondines incompletos, puntos omitidos) llegue al
            // servidor ANTES de intentar la creación.
            // ─────────────────────────────────────────────────────────
            try
            {
                await _sync.SincronizarAsync(SyncReason.AccionCritica);
            }
            catch { /* El sync es de mejor esfuerzo; continuamos de todas formas */ }

            var turno = await _server.CrearTurnoYRondinesAsync(guardiaID);

            // Cachear localmente
            await _local.UpsertTurnoAsync(MapTurnoLocal(turno));

            // Cachear rondines
            var rondines = await _server.GetRondinesPorTurnoAsync(turno.ID);
            foreach (var r in rondines)
                await _local.UpsertRondinAsync(MapRondinLocal(r, sincronizado: true));

            // Cachear puntos de cada rondín para garantizar operación offline
            var catalogo = await _local.GetPuntosControlAsync();
            var qrMap = catalogo.ToDictionary(p => p.ID, p => p.QRCode);
            foreach (var r in rondines)
            {
                var puntos = await _server.GetPuntosDeRondinAsync(r.ID);
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
            }

            await ProgramarNotificacionesRondinesAsync(rondines);
            return turno;
        }

        // ═══════════════════════════════════════════════════════════════
        // RONDINES
        // ═══════════════════════════════════════════════════════════════

        public async Task<List<Rondin>> GetRondinesPorTurnoAsync(int turnoID)
        {
            var locales = await _local.GetRondinesPorTurnoAsync(turnoID);
            if (locales.Count > 0) return locales.Select(MapRondin).ToList();

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

        public async Task IniciarRondinAsync(int rondinID)
        {
            var local = await _local.GetRondinPorIDAsync(rondinID);
            if (local == null) throw new InvalidOperationException("Rondín no encontrado.");
            if (local.Estado >= 1) return;

            if (AppConfig.ModoEstrictoRondines)
            {
                var ahora = DateTime.Now;
                var apertura = local.HoraProgramada.AddMinutes(-AppConfig.VentanaInicioAntesMinutos);
                var cierre = local.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);
                if (ahora < apertura)
                    throw new InvalidOperationException(
                        $"El rondín aún no está disponible. Disponible desde las {apertura:HH:mm} hrs.");
                if (ahora > cierre)
                    throw new InvalidOperationException(
                        $"El rondín de las {local.HoraProgramada:HH:mm} ya no puede iniciarse. " +
                        $"El tiempo límite fue {cierre:HH:mm} hrs.");
            }

            var rondinesDelTurno = await _local.GetRondinesPorTurnoAsync(local.TurnoID);
            bool hayOtroEnProgreso = rondinesDelTurno.Any(r => r.Estado == 1 && r.ID != rondinID);
            if (hayOtroEnProgreso)
                throw new InvalidOperationException(
                    "Ya hay un rondín en progreso. Finalízalo antes de iniciar otro.");

            local.Estado = 1;
            local.HoraInicio = DateTime.Now;
            local.Sincronizado = false;
            local.FechaModificacion = DateTime.Now;
            await _local.UpsertRondinAsync(local);

            if (await _connectivity.CheckServerAsync())
            {
                try { await _server.IniciarRondinAsync(rondinID); }
                catch { }
            }
        }

        /// Revisa todos los rondines del turno y cierra automáticamente los que
        /// superaron su ventana de tiempo. Se llama desde GuardiaHomePage antes
        /// de renderizar, y también desde la auto-finalización de turno.
        public async Task<int> ExpirarRondinesVencidosAsync(int turnoID)
        {
            if (!AppConfig.ModoEstrictoRondines) return 0;

            var rondines = await _local.GetRondinesPorTurnoAsync(turnoID);
            var ahora = DateTime.Now;
            int cerrados = 0;
            bool requiereSync = false;

            foreach (var r in rondines)
            {
                if (r.Estado >= 2) continue;

                var cierre = r.HoraProgramada.AddMinutes(AppConfig.VentanaInicioDespuesMinutos);
                if (ahora <= cierre) continue;

                var puntos = await _local.GetPuntosDeRondinAsync(r.ID);
                r.Estado = 3;
                r.HoraFin = r.HoraFin ?? cierre;
                r.PuntosTotal = puntos.Count > 0 ? puntos.Count : r.PuntosTotal;
                r.PuntosVisitados = puntos.Count(p => p.Estado == 1);
                r.Sincronizado = false;
                r.FechaModificacion = ahora;
                await _local.UpsertRondinAsync(r);

                foreach (var p in puntos.Where(p => p.Estado == 0))
                {
                    p.Estado = 2;
                    p.Sincronizado = false;
                    p.FechaModificacion = ahora;
                    await _local.UpsertRondinPuntoAsync(p);
                }

                cerrados++;
                requiereSync = true;
            }

            if (requiereSync && await _connectivity.CheckServerAsync())
                _ = Task.Run(async () => await _sync.SincronizarAsync(SyncReason.AccionCritica));

            return cerrados;
        }

        public async Task<int> AsegurarPuntosRondinAsync(int rondinID)
        {
            var puntosLocal = await _local.GetPuntosDeRondinAsync(rondinID);
            if (puntosLocal.Count > 0) return puntosLocal.Count;

            if (await _connectivity.CheckServerAsync())
            {
                int total = await _server.AsegurarPuntosRondinAsync(rondinID);
                var puntos = await _server.GetPuntosDeRondinAsync(rondinID);

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
                await ActualizarContadoresRondinAsync(rondinID);
                return total;
            }

            var puntosControl = await _local.GetPuntosControlAsync();
            if (puntosControl.Count == 0)
                throw new InvalidOperationException(
                    "Sin puntos de control disponibles. Conecta a la red al menos una vez.");

            foreach (var pc in puntosControl)
            {
                await _local.UpsertRondinPuntoAsync(new RondinPuntoLocal
                {
                    ServerID = 0,
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
            var local = await _local.GetRondinPuntoPorQRAsync(rondinID, qrCode);
            return local != null ? MapRondinPunto(local) : null;
        }

        public async Task<bool> RegistrarVisitaPuntoAsync(
            int rondinPuntoServerID, double? lat, double? lon,
            int rondinID = 0, string qrCode = "")
        {
            RondinPuntoLocal? local = null;
            if (!string.IsNullOrEmpty(qrCode))
                local = await _local.GetRondinPuntoPorQRAsync(rondinID, qrCode);
            if (local == null && rondinPuntoServerID > 0)
                local = (await _local.GetPuntosDeRondinAsync(rondinID))
                      .FirstOrDefault(p => p.ServerID == rondinPuntoServerID);
            if (local == null && rondinPuntoServerID > 0)
                local = await _local.GetRondinPuntoPorLocalIDAsync(rondinPuntoServerID);

            if (local != null)
            {
                local.Estado = 1;
                local.HoraVisita = DateTime.Now;
                local.LatitudG = lat;
                local.LongitudG = lon;
                local.Sincronizado = false;
                local.FechaModificacion = DateTime.Now;
                await _local.UpsertRondinPuntoAsync(local);
                await ActualizarContadoresRondinAsync(local.RondinID);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"No se encontró punto para QR={qrCode}, ServerID={rondinPuntoServerID}, RondinID={rondinID}");
                return false;
            }

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

        public async Task<RondinPuntoLocal?> GetRondinPuntoLocalPorServerIDAsync(int serverID) =>
            await _local.GetRondinPuntoPorServerIDAsync(serverID);

        public async Task GuardarFotoPuntoAsync(int localID, byte[] fotoBytes)
        {
            var puntoLocal = await _local.GetRondinPuntoPorLocalIDAsync(localID);
            if (puntoLocal == null) throw new InvalidOperationException("Punto no encontrado.");

            puntoLocal.FotoPath = fotoBytes;
            puntoLocal.Sincronizado = false;
            puntoLocal.FechaModificacion = DateTime.Now;
            await _local.UpsertRondinPuntoAsync(puntoLocal);

            if (await _connectivity.CheckServerAsync())
            {
                try
                {
                    await _server.ActualizarFotoPuntoAsync(puntoLocal.ServerID, fotoBytes);
                    puntoLocal.Sincronizado = true;
                    await _local.UpsertRondinPuntoAsync(puntoLocal);
                }
                catch { }
            }
        }

        public async Task FinalizarRondinAsync(int rondinID)
        {
            var local = await _local.GetRondinPorIDAsync(rondinID);
            if (local != null)
            {
                var puntos = await _local.GetPuntosDeRondinAsync(rondinID);
                bool todosVisitados = puntos.Count > 0 && puntos.All(p => p.Estado == 1);
                local.Estado = todosVisitados ? 2 : 3;
                local.HoraFin = DateTime.Now;
                local.PuntosTotal = puntos.Count;
                local.PuntosVisitados = puntos.Count(p => p.Estado == 1);
                local.Sincronizado = false;
                local.FechaModificacion = DateTime.Now;
                await _local.UpsertRondinAsync(local);
            }

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
                _ = Task.Run(async () => await _sync.SincronizarAsync(SyncReason.AccionCritica));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // INCIDENCIAS
        // ═══════════════════════════════════════════════════════════════

        public async Task CrearIncidenciaAsync(Incidencia inc)
        {
            var localInc = new IncidenciaLocal
            {
                TurnoID = inc.TurnoID,
                RondinID = inc.RondinID,
                PuntoID = inc.PuntoID,
                GuardiaReportaID = inc.GuardiaReportaID,
                Descripcion = inc.Descripcion,
                FotoPath = inc.FotoPath,
                FechaReporte = inc.FechaReporte,
                Estado = 0,
                Sincronizado = false,
                FechaModificacion = DateTime.Now,
            };
            await _local.InsertIncidenciaAsync(localInc);

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

        // ═══════════════════════════════════════════════════════════════
        // PUNTOS DE CONTROL
        // ═══════════════════════════════════════════════════════════════

        public async Task<List<PuntoControl>> GetPuntosControlAsync()
        {
            if (await _connectivity.CheckServerAsync())
            {
                var lista = await _sharedDatabase.GetPuntosControlAsync();
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

        // ═══════════════════════════════════════════════════════════════
        // ESTADO DE PENDIENTES
        // ═══════════════════════════════════════════════════════════════

        public async Task<int> GetTotalPendientesSyncAsync()
        {
            var r = (await _local.GetRondinesPendientesSyncAsync()).Count;
            var rp = (await _local.GetPuntosPendientesSyncAsync()).Count;
            var i = (await _local.GetIncidenciasPendientesSyncAsync()).Count;
            return r + rp + i;
        }

        // ═══════════════════════════════════════════════════════════════
        // HISTORIAL GUARDIA — OFFLINE
        // ═══════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private async Task ActualizarContadoresRondinAsync(int rondinID)
        {
            var rondin = await _local.GetRondinPorIDAsync(rondinID);
            if (rondin == null) return;
            var puntos = await _local.GetPuntosDeRondinAsync(rondinID);
            rondin.PuntosTotal = puntos.Count;
            rondin.PuntosVisitados = puntos.Count(p => p.Estado == 1);
            await _local.UpsertRondinAsync(rondin);
        }

        // ═══════════════════════════════════════════════════════════════
        // MAPPERS
        // ═══════════════════════════════════════════════════════════════

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
            Estado = t.Estado,
        };

        private static TurnoLocal MapTurnoLocal(Turno t) => new()
        {
            ID = t.ID,
            GuardiaID = t.GuardiaID,
            Fecha = t.Fecha.ToDateTime(TimeOnly.MinValue),
            HoraInicio = t.HoraInicio.ToTimeSpan(),
            HoraFin = t.HoraFin.ToTimeSpan(),
            Estado = t.Estado,
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