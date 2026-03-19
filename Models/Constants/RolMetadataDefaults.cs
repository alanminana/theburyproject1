namespace TheBuryProject.Models.Constants;

/// <summary>
/// Valores por defecto para metadata de roles base del sistema.
/// </summary>
public static class RolMetadataDefaults
{
    public static string GetDescripcion(string? roleName)
    {
        return roleName switch
        {
            Roles.SuperAdmin => "Acceso total al sistema y administración integral de seguridad.",
            Roles.Administrador => "Administración operativa del ERP con amplio alcance funcional.",
            Roles.Gerente => "Supervisión comercial y operativa con foco en ventas, compras y reportes.",
            Roles.Vendedor => "Gestión comercial de ventas, cotizaciones y atención de clientes.",
            Roles.Cajero => "Operación de caja, cobranzas y movimientos asociados.",
            Roles.Repositor => "Gestión de stock, movimientos internos y alertas de inventario.",
            Roles.Tecnico => "Atención de devoluciones, garantías y circuitos técnicos.",
            Roles.Contador => "Consulta de reportes y seguimiento financiero con alcance de solo lectura.",
            _ => "Rol configurable del módulo Seguridad."
        };
    }
}
