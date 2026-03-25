using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;
using System.Data;
using static RocLandSecurity.AppConfig;

namespace RocLandSecurity.Services
{
    public class SupervisorService
    {
        private readonly string connectionString;

        public SupervisorService(string connString)
        {
            connectionString = connString;
        }

        // Conexion string para servicios de Sync
        public string GetConnectionString() => connectionString;

        // USUARIOS

        public async Task<List<Usuario>> GetUsuarios(string? filtro = null)
        {
            var lista = new List<Usuario>();
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            string query = @"
            SELECT ID, Nombre, Usuario, Contrasena, QRCode, Rol, FechaCreacion,
                Activo FROM TBL_ROCLAND_SECURITY_USUARIOS
                Where Activo = 1";

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                query += " AND (Nombre LIKE @filtro OR Usuario LIKE @filtro)";
            }

            using var cmd = new SqlCommand(query, conn);

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                cmd.Parameters.AddWithValue("@filtro", $"%{filtro}%");
            }

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(MapUsuario(reader));
            }
            return lista;
        }

        // Mappers

        private static Usuario MapUsuario(SqlDataReader r) => new()
        {
            ID = r.GetInt32(0),
            Nombre = r.GetString(1),
            UsuarioLogin = r.GetString(2),
            Contraseña = r.GetString(3),
            QRCode = r.IsDBNull(4) ? null : r.GetString(4),
            Rol = r.GetInt32(5),
            FechaCreacion = r.GetDateTime(6),
            Activo = r.GetBoolean(7)
        };

    }
}
