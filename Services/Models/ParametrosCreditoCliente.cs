using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

/// <summary>
/// Parámetros de crédito resueltos para un cliente según la cadena de prioridad:
/// Personalizado por cliente > Perfil preferido del cliente > Global.
/// </summary>
public sealed class ParametrosCreditoCliente
{
    /// <summary>Fuente que determinó la configuración resultante.</summary>
    public FuenteConfiguracionCredito Fuente { get; init; }

    /// <summary>Tasa de interés mensual aplicable (%).</summary>
    public decimal TasaMensual { get; init; }

    /// <summary>Gastos administrativos aplicables.</summary>
    public decimal GastosAdministrativos { get; init; }

    /// <summary>Cantidad máxima de cuotas permitidas.</summary>
    public int CuotasMaximas { get; init; }

    /// <summary>Cantidad mínima de cuotas requeridas.</summary>
    public int CuotasMinimas { get; init; }

    /// <summary>Monto mínimo financiable (null = sin restricción).</summary>
    public decimal? MontoMinimo { get; init; }

    /// <summary>Monto máximo financiable (null = sin restricción).</summary>
    public decimal? MontoMaximo { get; init; }

    /// <summary>ID del perfil de crédito preferido del cliente, si existe.</summary>
    public int? PerfilPreferidoId { get; init; }

    /// <summary>Nombre del perfil de crédito preferido, si existe.</summary>
    public string? PerfilPreferidoNombre { get; init; }

    /// <summary>Indica si el cliente tiene al menos un campo de configuración personalizada.</summary>
    public bool TieneConfiguracionPersonalizada { get; init; }

    /// <summary>Indica si el cliente tiene tasa personalizada (para UI).</summary>
    public bool TieneTasaPersonalizada { get; init; }

    /// <summary>Tasa personalizada del cliente si existe (para UI).</summary>
    public decimal? TasaPersonalizada { get; init; }

    /// <summary>Gastos personalizados del cliente si existen (para UI).</summary>
    public decimal? GastosPersonalizados { get; init; }
}
