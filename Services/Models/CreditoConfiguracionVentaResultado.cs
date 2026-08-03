using TheBuryProject.Models.DTOs;

namespace TheBuryProject.Services.Models;

/// <summary>
/// Por qué se rechazó la configuración. Determina el código HTTP de la respuesta.
/// </summary>
public enum MotivoRechazoConfiguracionCredito
{
    Ninguno = 0,

    /// <summary>Datos del formulario inválidos o incompletos → 400.</summary>
    SolicitudInvalida = 1,

    /// <summary>
    /// Los productos de la venta impiden financiarla con la selección enviada
    /// (bloqueo por producto, intersección vacía, cantidad no disponible) → 409.
    /// </summary>
    Conflicto = 2
}

public sealed class CreditoConfiguracionVentaResultado
{
    private CreditoConfiguracionVentaResultado(
        bool esValido,
        ConfiguracionCreditoComando? comando,
        CreditoRangoProductoResultado? rangoEfectivo,
        string? errorKey,
        string? errorMessage,
        MotivoRechazoConfiguracionCredito motivo)
    {
        EsValido = esValido;
        Comando = comando;
        RangoEfectivo = rangoEfectivo;
        ErrorKey = errorKey;
        ErrorMessage = errorMessage;
        Motivo = motivo;
    }

    public bool EsValido { get; }
    public ConfiguracionCreditoComando? Comando { get; }
    public CreditoRangoProductoResultado? RangoEfectivo { get; }
    public string? ErrorKey { get; }
    public string? ErrorMessage { get; }
    public MotivoRechazoConfiguracionCredito Motivo { get; }

    public static CreditoConfiguracionVentaResultado Valido(
        ConfiguracionCreditoComando comando,
        CreditoRangoProductoResultado rangoEfectivo) =>
        new(true, comando, rangoEfectivo, null, null, MotivoRechazoConfiguracionCredito.Ninguno);

    public static CreditoConfiguracionVentaResultado Invalido(
        string errorKey,
        string errorMessage,
        CreditoRangoProductoResultado? rangoEfectivo = null,
        MotivoRechazoConfiguracionCredito motivo = MotivoRechazoConfiguracionCredito.SolicitudInvalida) =>
        new(false, null, rangoEfectivo, errorKey, errorMessage, motivo);
}
