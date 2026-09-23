using Microsoft.Data.SqlClient;

namespace TheBuryProject.Helpers;

internal static class ProductionSecrets
{
    internal static void Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Configuración obligatoria ausente, vacía o provisional: {key}.");
    }

    internal static string ConnectionString(IConfiguration configuration, bool production)
    {
        var basis = configuration.GetConnectionString("DefaultConnection");
        var user = configuration["ErpDb:User"];
        var password = configuration["ErpDb:Password"];
        if (production)
        {
            Require(basis, "ConnectionStrings:DefaultConnection");
            Require(user, "ErpDb:User");
            Require(password, "ErpDb:Password");
            if (string.Equals(user, "sa", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ErpDb:User no puede ser sa.");
        }

        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(password))
            return basis ?? string.Empty; // LocalDB/Windows Auth en Development y Testing.
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("ErpDb:User y ErpDb:Password deben definirse juntos.");

        try
        {
            var result = new SqlConnectionStringBuilder(basis);
            if (production && (!string.IsNullOrEmpty(result.Password) || result.IntegratedSecurity))
                throw new InvalidOperationException("La cadena base de Production debe usar credenciales SQL separadas.");
            result.UserID = user;
            result.Password = password;
            result.PersistSecurityInfo = false;
            return result.ConnectionString;
        }
        catch (ArgumentException)
        {
            // SqlClient puede incluir el fragmento inválido en su excepción. No conservarla como InnerException.
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection tiene un formato inválido.");
        }
    }
}
