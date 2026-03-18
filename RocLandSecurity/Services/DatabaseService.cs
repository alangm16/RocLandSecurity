using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;

namespace RocLandSecurity.Services
{
    public class DatabaseService
    {
        private readonly string connectionString;

        public DatabaseService(string connString)
        {
            connectionString = connString;
        }

        // Login por usuario + contraseña (hash SHA256)
        public async Task<Usuario?> GetUsuarioByLoginAsync(string usuario, string hashContrasena)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM Usuarios
                WHERE Usuario = @usuario
                  AND Contrasena = @contrasena
                  AND Activo = 1";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@usuario", usuario);
            cmd.Parameters.AddWithValue("@contrasena", hashContrasena);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapUsuario(reader);

            return null;
        }

        // Login por código QR
        public async Task<Usuario?> GetUsuarioByQRAsync(string qrCode)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            const string query = @"
                SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion, Activo
                FROM Usuarios
                WHERE QRCode = @qrCode";
            // No filtramos Activo=1 aquí para poder dar mensaje diferenciado

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@qrCode", qrCode);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapUsuario(reader);

            return null;
        }

        private static Usuario MapUsuario(SqlDataReader reader) => new()
        {
            ID = reader.GetInt32(0),
            Nombre = reader.GetString(1),
            UsuarioLogin = reader.GetString(2),
            Contraseña = reader.GetString(3),
            QRCode = reader.IsDBNull(4) ? null : reader.GetString(4),
            Rol = reader.GetInt32(5),
            FechaCreacion = reader.GetDateTime(6),
            Activo = reader.GetBoolean(7)
        };
    }
}