using SQLite;

namespace RocLandSecurity.Services
{

    /// Base de datos SQLite local en el dispositivo.
    /// Actúa como espejo offline de SQL Server.
    /// Persiste entre cierres de app, reinicios y modo avión.
    ///
    /// Tablas locales:
    ///   - UsuarioLocal       : credenciales cacheadas para login offline
    ///   - TurnoLocal         : turno activo del guardia
    ///   - RondinLocal        : rondines del turno con estado de sync
    ///   - RondinPuntoLocal   : visitas a puntos con estado de sync
    ///   - IncidenciaLocal    : incidencias reportadas con estado de sync
    ///   - PuntoControlLocal  : catálogo de puntos QR (se sincroniza al inicio)

    public class LocalDatabase
    {
        private SQLiteAsyncConnection? _db;
        private static readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized = false;

        private static string DbPath =>
            Path.Combine(FileSystem.AppDataDirectory, "rocland_local.db3");

        private SQLiteAsyncConnection Db => _db
            ?? throw new InvalidOperationException("LocalDatabase no inicializada. Llama InitAsync primero.");

        public Task<RondinPuntoLocal?> GetRondinPuntoPorLocalIDAsync(int localID) =>
            Db.Table<RondinPuntoLocal>().Where(rp => rp.LocalID == localID).FirstOrDefaultAsync();

        public Task<RondinPuntoLocal?> GetRondinPuntoPorServerIDAsync(int serverID) =>
            Db.Table<RondinPuntoLocal>().Where(rp => rp.ServerID == serverID).FirstOrDefaultAsync();

        public Task<List<TurnoLocal>> GetTurnosPendientesSyncAsync() =>
            Db.Table<TurnoLocal>().Where(t => !t.Sincronizado).ToListAsync();


        // INICIALIZACIÓN

        public async Task InitAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;
                _db = new SQLiteAsyncConnection(DbPath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

                await _db.CreateTableAsync<UsuarioLocal>();
                await _db.CreateTableAsync<TurnoLocal>();
                await _db.CreateTableAsync<RondinLocal>();
                await _db.CreateTableAsync<RondinPuntoLocal>();
                await _db.CreateTableAsync<IncidenciaLocal>();
                await _db.CreateTableAsync<PuntoControlLocal>();

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        // USUARIOS — Login offline

        public async Task UpsertUsuarioAsync(UsuarioLocal u)
        {
            await EnsureInitializedAsync();
            await Db.InsertOrReplaceAsync(u);
        }


        public async Task<UsuarioLocal?> GetUsuarioByLoginAsync(string usuario, string hashContrasena)
        {
            string hashUp = hashContrasena.ToUpperInvariant();
            string hashLo = hashContrasena.ToLowerInvariant();
            var corte = DateTime.Now.AddDays(-AppConfig.RetencionDatosSync); // 1 día

            var u = await Db.Table<UsuarioLocal>()
              .Where(u => u.UsuarioLogin == usuario && u.Activo &&
                         (u.Contrasena == hashUp || u.Contrasena == hashLo))
              .FirstOrDefaultAsync();

            // Si el caché es demasiado viejo, lo tratamos como no encontrado
            return (u != null && u.FechaCacheada >= corte) ? u : null;
        }

        public async Task<UsuarioLocal?> GetUsuarioByQRAsync(string qrCode)
        {
            var corte = DateTime.Now.AddDays(-AppConfig.RetencionDatosSync);
            var u = await Db.Table<UsuarioLocal>()
                      .Where(u => u.QRCode == qrCode && u.Activo)
                      .FirstOrDefaultAsync();
            return (u != null && u.FechaCacheada >= corte) ? u : null;
        }

        // PUNTOS DE CONTROL — Catálogo offline

        public Task UpsertPuntoControlAsync(PuntoControlLocal p) =>
            Db.InsertOrReplaceAsync(p);

        public Task<List<PuntoControlLocal>> GetPuntosControlAsync() =>
            Db.Table<PuntoControlLocal>().OrderBy(p => p.Orden).ToListAsync();

        // TURNOS


        public Task UpsertTurnoAsync(TurnoLocal t) =>
            Db.InsertOrReplaceAsync(t);

        public async Task<TurnoLocal?> GetTurnoActivoAsync(int guardiaID)
        {
            // Traemos el turno Pendiente (0) o En Progreso (1) 
            // sin importar su fecha de creación.
            var candidatos = await Db.Table<TurnoLocal>()
                .Where(t => t.GuardiaID == guardiaID
                         && (t.Estado == 0 || t.Estado == 1))
                .ToListAsync();

            // Preferir el más reciente
            return candidatos.OrderByDescending(t => t.Fecha).FirstOrDefault();
        }

        public async Task FinalizarTurnoLocalAsync(int turnoID)
        {
            var t = await GetTurnoPorIDAsync(turnoID);
            if (t != null)
            {
                t.Estado = 2;
                t.Sincronizado = false; // Pendiente de sync al recuperar conexión
                await Db.UpdateAsync(t);
            }
        }

        public Task<TurnoLocal?> GetTurnoPorIDAsync(int turnoID) =>
            Db.Table<TurnoLocal>().Where(t => t.ID == turnoID).FirstOrDefaultAsync();

        // RONDINES

        public Task UpsertRondinAsync(RondinLocal r) =>
            Db.InsertOrReplaceAsync(r);

        public Task<List<RondinLocal>> GetRondinesPorTurnoAsync(int turnoID) =>
            Db.Table<RondinLocal>()
              .Where(r => r.TurnoID == turnoID)
              .OrderBy(r => r.HoraProgramada)
              .ToListAsync();

        public Task<RondinLocal?> GetRondinPorIDAsync(int rondinID) =>
            Db.Table<RondinLocal>().Where(r => r.ID == rondinID).FirstOrDefaultAsync();

        public Task<List<RondinLocal>> GetRondinesPendientesSyncAsync() =>
            Db.Table<RondinLocal>().Where(r => !r.Sincronizado).ToListAsync();

        // PUNTOS DE RONDÍN

        public Task UpsertRondinPuntoAsync(RondinPuntoLocal rp)
        {
            // Si LocalID == 0 es un registro nuevo: Insert (SQLite asigna el ID automático)
            // Si LocalID > 0 ya existe: Replace
            return rp.LocalID == 0
                ? Db.InsertAsync(rp)
                : Db.InsertOrReplaceAsync(rp);
        }

        public Task<List<RondinPuntoLocal>> GetPuntosDeRondinAsync(int rondinID) =>
            Db.Table<RondinPuntoLocal>()
              .Where(rp => rp.RondinID == rondinID)
              .OrderBy(rp => rp.OrdenPunto)
              .ToListAsync();

        public Task<RondinPuntoLocal?> GetRondinPuntoPorQRAsync(int rondinID, string qrCode) =>
            Db.Table<RondinPuntoLocal>()
              .Where(rp => rp.RondinID == rondinID && rp.QRCode == qrCode)
              .FirstOrDefaultAsync();

        public Task<List<RondinPuntoLocal>> GetPuntosPendientesSyncAsync() =>
            // Solo sincronizar puntos que el guardia modificó (escaneados o marcados omitido)
            // Los puntos en Estado=0 (Pendiente sin escanear) ya existen en el servidor tal cual
            Db.Table<RondinPuntoLocal>().Where(rp => !rp.Sincronizado && rp.Estado > 0).ToListAsync();

        public Task<int> GetTotalPuntosModificadosPendientesAsync() =>
            Db.Table<RondinPuntoLocal>()
              .Where(rp => !rp.Sincronizado && rp.Estado > 0)
              .CountAsync();

        // INCIDENCIAS

        public async Task<int> InsertIncidenciaAsync(IncidenciaLocal inc)
        {
            await Db.InsertAsync(inc);
            return inc.LocalID;
        }

        public Task UpsertIncidenciaAsync(IncidenciaLocal inc) =>
            Db.InsertOrReplaceAsync(inc);

        public Task<List<IncidenciaLocal>> GetIncidenciasPendientesSyncAsync() =>
            Db.Table<IncidenciaLocal>().Where(i => !i.Sincronizado).ToListAsync();

        public Task<List<IncidenciaLocal>> GetIncidenciasPorRondinAsync(int rondinID) =>
            Db.Table<IncidenciaLocal>()
              .Where(i => i.RondinID == rondinID)
              .ToListAsync();

        public Task<List<IncidenciaLocal>> GetIncidenciasPorTurnoAsync(int turnoID) =>
            Db.Table<IncidenciaLocal>()
              .Where(i => i.TurnoID == turnoID && i.RondinID == null)
              .ToListAsync();

        // MARCADO DE SYNC

        public async Task MarcarRondinSincronizadoAsync(int rondinID)
        {
            var r = await GetRondinPorIDAsync(rondinID);
            if (r != null) { r.Sincronizado = true; await Db.UpdateAsync(r); }
        }

        public async Task MarcarPuntoSincronizadoAsync(int localID)
        {
            var p = await Db.Table<RondinPuntoLocal>()
                .Where(rp => rp.LocalID == localID).FirstOrDefaultAsync();
            if (p != null) { p.Sincronizado = true; await Db.UpdateAsync(p); }
        }

        public async Task MarcarIncidenciaSincronizadaAsync(int localID, int serverID)
        {
            var inc = await Db.Table<IncidenciaLocal>()
                .Where(i => i.LocalID == localID).FirstOrDefaultAsync();
            if (inc != null)
            {
                inc.Sincronizado = true;
                inc.ServerID = serverID;
                await Db.UpdateAsync(inc);
            }
        }

        // LIMPIEZA — Elimina registros sync de turnos > 7 días

        public async Task LimpiarDatosViejosAsync()
        {
            var corte = DateTime.Today.AddDays(-AppConfig.RetencionDatosSync);

            // Obtener IDs de turnos viejos sincronizados
            var turnosViejos = await Db.Table<TurnoLocal>()
                .Where(t => t.Fecha < corte)
                .ToListAsync();

            foreach (var turno in turnosViejos)
            {
                var rondines = await GetRondinesPorTurnoAsync(turno.ID);
                bool todosSinc = rondines.All(r => r.Sincronizado);
                if (!todosSinc) continue; // No limpiar si hay pendientes

                foreach (var rondin in rondines)
                {
                    await Db.Table<RondinPuntoLocal>()
                        .Where(rp => rp.RondinID == rondin.ID).DeleteAsync();
                    await Db.Table<IncidenciaLocal>()
                        .Where(i => i.RondinID == rondin.ID && i.Sincronizado).DeleteAsync();
                }
                await Db.Table<RondinLocal>()
                    .Where(r => r.TurnoID == turno.ID).DeleteAsync();
                await Db.DeleteAsync(turno);
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_initialized)
                await InitAsync();
        }
    }


    // MODELOS SQLITE (tablas locales)

    [Table("Usuarios")]
    public class UsuarioLocal
    {
        [PrimaryKey] public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UsuarioLogin { get; set; } = string.Empty;
        public string Contrasena { get; set; } = string.Empty;  // hash SHA256
        public string? QRCode { get; set; }
        public int Rol { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCacheada { get; set; } = DateTime.Now;
    }

    [Table("PuntosControl")]
    public class PuntoControlLocal
    {
        [PrimaryKey] public int ID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string QRCode { get; set; } = string.Empty;
        public int Orden { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }
    }

    [Table("Turnos")]
    public class TurnoLocal
    {
        [PrimaryKey] public int ID { get; set; }
        public int GuardiaID { get; set; }
        public DateTime Fecha { get; set; }
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFin { get; set; }
        public byte Estado { get; set; }   // 0-3
        public bool Sincronizado { get; set; } = true; // Los turnos siempre se crean online
    }

    [Table("Rondines")]
    public class RondinLocal
    {
        [PrimaryKey] public int ID { get; set; }
        public int TurnoID { get; set; }
        public int GuardiaID { get; set; }
        public DateTime HoraProgramada { get; set; }
        public DateTime? HoraInicio { get; set; }
        public DateTime? HoraFin { get; set; }
        public int Estado { get; set; }   // 0-4
        public int PuntosTotal { get; set; }   // se rellena al cargar puntos
        public int PuntosVisitados { get; set; }   // se actualiza al escanear
        public bool Sincronizado { get; set; } = false;
        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }

    [Table("RondinesPuntos")]
    public class RondinPuntoLocal
    {
        [PrimaryKey, AutoIncrement] public int LocalID { get; set; }
        public int ServerID { get; set; }   // ID en SQL Server (0 si no sincronizado)
        public int RondinID { get; set; }
        public int PuntoID { get; set; }
        public string NombrePunto { get; set; } = string.Empty;
        public string QRCode { get; set; } = string.Empty;
        public int OrdenPunto { get; set; }
        public DateTime? HoraVisita { get; set; }
        public int Estado { get; set; }   // 0=Pendiente 1=Visitado 2=Omitido
        public double? LatitudG { get; set; }
        public double? LongitudG { get; set; }
        public byte[]? FotoPath { get; set; }
        public bool Sincronizado { get; set; } = false;
        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }

    [Table("Incidencias")]
    public class IncidenciaLocal
    {
        [PrimaryKey, AutoIncrement] public int LocalID { get; set; }
        public int ServerID { get; set; }   // ID en SQL Server (0 si no sync)
        public int TurnoID { get; set; }
        public int? RondinID { get; set; }
        public int? PuntoID { get; set; }
        public int GuardiaReportaID { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public byte[]? FotoPath { get; set; }
        public DateTime FechaReporte { get; set; }
        public int Estado { get; set; } = 0;
        public bool Sincronizado { get; set; } = false;
        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }
}