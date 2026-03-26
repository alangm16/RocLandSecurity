using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    // Operaciones compartidas por guardia y supervisor:
    // autenticación y catálogo de puntos de control.

    public class SharedDatabaseService : DatabaseServiceBase
    {
        public SharedDatabaseService(string connectionString) : base(connectionString) { }

        // USUARIOS / AUTENTICACIÓN

        public async Task<Usuario?> GetUsuarioByLoginAsync(string usuario, string hashContrasena)
        {
            using var conn = new SqlConnection(ConnectionString);
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
            using var conn = new SqlConnection(ConnectionString);
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
            using var conn = new SqlConnection(ConnectionString);
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

        // PUNTOS DE CONTROL

        public async Task<List<PuntoControl>> GetPuntosControlAsync()
        {
            using var conn = new SqlConnection(ConnectionString);
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
    }
}