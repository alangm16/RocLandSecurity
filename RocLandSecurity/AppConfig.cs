using System;

namespace RocLandSecurity
{
    /// <summary>
    /// Configuración centralizada de parámetros ajustables.
    /// Los valores pueden leerse desde appsettings.json, variables de entorno o
    /// modificarse directamente en esta clase según necesidades del despliegue.
    /// </summary>
    public static class AppConfig
    {
        // ─────────────────────────────────────────────────────────────────
        // Conexión a SQL Server
        // Usado en: MauiProgram.cs, ConnectivityService.cs, SyncService.cs
        // -- INTEGRADO y Funcionando
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Cadena de conexión construida a partir de los componentes.</summary>
        public const string ConnectionString =
            


        // ─────────────────────────────────────────────────────────────────
        // Turnos
        // Usado en: DatabaseService.cs (CrearTurnoYRondinesAsync, GetTurnoActivoAsync)
        // -- AppConfig Integrado en CrearTurnoYRondinesAsync Funcionando,
        //    GetTurnoActivoAsync no es necesario.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Hora de inicio del turno (formato HH:mm).</summary>
        /// <remarks>
        /// Turno normal: "19:00" (7pm)
        /// Turno pruebas: "08:00" (8am)
        /// </remarks>
        public static string HoraInicioTurno = "19:00";

        /// <summary>Hora de fin del turno (formato HH:mm).</summary>
        /// <remarks>
        /// Turno normal: "07:00" (7am)
        /// Turno pruebas: "18:00" (6pm)
        /// </remarks>
        public static string HoraFinTurno = "07:00";

        /// <summary>
        /// Indica si el turno cruza la medianoche (hora fin es menor que hora inicio).
        /// Turno normal (19:00-07:00): true
        /// Turno pruebas (08:00-18:00): false
        /// </summary>
        public static bool TurnoCruzaMedianoche =>
            TimeSpan.Parse(HoraFinTurno) < TimeSpan.Parse(HoraInicioTurno);

        // ─────────────────────────────────────────────────────────────────
        // Rondines - Ventanas de tiempo y estado
        // Usado en: DatabaseService.cs (IniciarRondinAsync) y RondinActivoPage.xaml.cs
        // Integrado en IniciarRondinAsync y en RondinActivoPage y Funcionando Correctamente
        // ─────────────────────────────────────────────────────────────────
        /// <summary>Minutos antes de la hora programada que se permite iniciar un rondín.</summary>
        public static int VentanaInicioAntesMinutos = 5;
        /// <summary>Minutos después de la hora programada hasta los que se permite iniciar un rondín.</summary>
        public static int VentanaInicioDespuesMinutos = 90;
        /// <summary>
        /// Modo estricto: si true, aplica la ventana horaria; si false, permite iniciar siempre.
        /// Actualmente se usa en RondinActivoPage con false (para pruebas), pero en producción debería ser true.
        /// </summary>
        public static bool ModoEstrictoRondines = true;

        // ─────────────────────────────────────────────────────────────────
        // Sincronización (Sync)
        // Usado en: App.xaml.cs, SyncService.cs, LocalDatabase.cs
        // Integrado y Funcionando
        // ─────────────────────────────────────────────────────────────────
        /// <summary>Intervalo en minutos del timer de sincronización automática.</summary>
        public const int SyncTimerIntervaloMinutos = 5;
        /// <summary>Días que se conservan los datos locales después de sincronizados.</summary>
        public static int RetencionDatosSync = 7;

        // ─────────────────────────────────────────────────────────────────
        // DE MOMENTO NO SE INTEGRARA
        // Historial y consultas 
        // Usado en: DatabaseService.cs (GetIncidenciasSemanaAsync)
        // ─────────────────────────────────────────────────────────────────
        /// <summary>Días hacia atrás que se muestran en el listado de incidencias del supervisor.</summary>
        public static int IncidenciasDiasAtras = 7;

        // ─────────────────────────────────────────────────────────────────
        // DE MOMENTO NO SE INTEGRARA
        // UI – Duración de animaciones y mensajes (milisegundos) 
        // Usado en: Varios (MainPage, RondinActivoPage, ReportarIncidenciaPage, etc.)
        // ─────────────────────────────────────────────────────────────────
        public static int ToastDuracionMs = 2500;
        public static int ToastDuracionCortaMs = 2000;
        public static int AnimacionProgresoMs = 300;
        public static int DelayEscaneoExitosoMs = 700;
        public static int DelayEscaneoErrorMs = 1200;
        public static int DelayEscaneoOrdenMs = 1500;
        public static int DelayReinicioCamaraMs = 600;
        public static int DelayRefrescarCamaraMs = 200;
        public static int DelayNavegacionMs = 1000;
    }
}