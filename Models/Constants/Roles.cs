namespace TheBuryProject.Models.Constants;

/// <summary>
/// Constantes de roles del sistema
/// </summary>
public static class Roles
{
    /// <summary>
    /// Super Administrador - Todos los permisos sin restricción
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>
    /// Administrador - Todos los permisos excepto gestión de configuración crítica
    /// </summary>
    public const string Administrador = "Administrador";

    /// <summary>
    /// Gerente - Gestión de ventas, compras, reportes, autorizaciones
    /// </summary>
    public const string Gerente = "Gerente";

    /// <summary>
    /// Vendedor - Ventas, cotizaciones, clientes
    /// </summary>
    public const string Vendedor = "Vendedor";

    /// <summary>
    /// Cajero - Cobros, caja, movimientos de efectivo
    /// </summary>
    public const string Cajero = "Cajero";

    /// <summary>
    /// Repositor - Gestión de stock, movimientos
    /// </summary>
    public const string Repositor = "Repositor";

    /// <summary>
    /// Técnico - Devoluciones, garantías, RMAs
    /// </summary>
    public const string Tecnico = "Tecnico";

    /// <summary>
    /// Contador - Reportes, consultas, visualización sin modificación
    /// </summary>
    public const string Contador = "Contador";

    /// <summary>
    /// Retorna todos los roles del sistema
    /// </summary>
    public static string[] GetAllRoles()
    {
        return new[]
        {
            SuperAdmin,
            Administrador,
            Gerente,
            Vendedor,
            Cajero,
            Repositor,
            Tecnico,
            Contador
        };
    }

    /// <summary>
    /// Verifica si un rol es administrativo (puede gestionar otros usuarios)
    /// </summary>
    public static bool IsAdminRole(string role)
    {
        return role == SuperAdmin || role == Administrador;
    }

    /// <summary>
    /// Verifica si un rol puede autorizar operaciones
    /// </summary>
    public static bool CanAuthorize(string role)
    {
        return role == SuperAdmin || role == Administrador || role == Gerente;
    }
}