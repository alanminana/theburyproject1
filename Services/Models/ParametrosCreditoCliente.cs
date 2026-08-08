using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

/// <summary>
/// Parámetros de crédito resueltos para un cliente. La cadena de prioridad Personalizado >
/// Perfil > Global solo sigue vigente para los campos no financieros (gastos administrativos,
/// límites de cuotas/monto); ver <see cref="TasaMensual"/>.
/// </summary>
public sealed class ParametrosCreditoCliente
{
    /// <summary>Fuente que determinó la configuración resultante (campos no financieros).</summary>
    public FuenteConfiguracionCredito Fuente { get; init; }

    /// <summary>
    /// LEGACY INERTE (ML2.1/ML3): siempre igual a la tasa global pasada a
    /// <see cref="Services.Interfaces.IConfiguracionPagoService.ObtenerParametrosCreditoClienteAsync"/>
    /// — ni Cliente ni Perfil la alteran, sin excepción. Ningún caller productivo la usa para
    /// resolver el porcentaje de una venta (esa resolución es 100% del plan de cuotas, ver
    /// <see cref="Services.CreditoConfiguracionVentaService"/> / <see cref="Services.CreditoSimulacionVentaService"/>).
    /// Único consumidor real: prefill del formulario en el GET de
    /// <see cref="Controllers.CreditoController.ConfigurarVenta(int, int?, string?, bool)"/>, un
    /// valor inicial que el POST descarta y vuelve a resolver desde el plan.
    /// </summary>
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
