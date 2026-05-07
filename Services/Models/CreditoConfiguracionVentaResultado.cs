using TheBuryProject.Models.DTOs;

namespace TheBuryProject.Services.Models;

public sealed class CreditoConfiguracionVentaResultado
{
    private CreditoConfiguracionVentaResultado(
        bool esValido,
        ConfiguracionCreditoComando? comando,
        CreditoRangoProductoResultado? rangoEfectivo,
        string? errorKey,
        string? errorMessage)
    {
        EsValido = esValido;
        Comando = comando;
        RangoEfectivo = rangoEfectivo;
        ErrorKey = errorKey;
        ErrorMessage = errorMessage;
    }

    public bool EsValido { get; }
    public ConfiguracionCreditoComando? Comando { get; }
    public CreditoRangoProductoResultado? RangoEfectivo { get; }
    public string? ErrorKey { get; }
    public string? ErrorMessage { get; }

    public static CreditoConfiguracionVentaResultado Valido(
        ConfiguracionCreditoComando comando,
        CreditoRangoProductoResultado rangoEfectivo) =>
        new(true, comando, rangoEfectivo, null, null);

    public static CreditoConfiguracionVentaResultado Invalido(
        string errorKey,
        string errorMessage,
        CreditoRangoProductoResultado? rangoEfectivo = null) =>
        new(false, null, rangoEfectivo, errorKey, errorMessage);
}
