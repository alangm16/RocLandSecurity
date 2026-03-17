using Microsoft.Data.SqlClient;
using RocLandSecurity.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace RocLandSecurity.Services
{
    internal class DatabaseService
    {
        private readonly string connectionString;

        public DatabaseService(string connString)
        {
            connectionString = connString;
        }

        public async Task<Usuario?> GetUsuarioByLoginAsync(string usuario, string hashContrasena)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            string query = "Select * FROM Usuarios WHERE Usuario = @usuario AND Contrasena = @contrasena AND Activo = 1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@usuario", usuario);
            cmd.Parameters.AddWithValue("@contrasena", hashContrasena);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Usuario
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
            return null;

        }
    }
}
