using TheBuryProject.Services.Exceptions;

namespace TheBuryProject.Services.Models;

/// <summary>
/// Resultado de verificar que crédito, venta y cliente forman una relación consistente
/// antes de configurar un crédito. <see cref="ClienteId"/> proviene siempre de la entidad
/// persistida del crédito, nunca del formulario.
/// </summary>
public sealed class ResultadoRelacionConfiguracionCredito
{
    public bool EsValida { get; private init; }
    public int ClienteId { get; private init; }
    public MotivoRechazoConfiguracionCreditoServicio? Motivo { get; private init; }
    public string? Mensaje { get; private init; }

    public static ResultadoRelacionConfiguracionCredito Valida(int clienteId) =>
        new() { EsValida = true, ClienteId = clienteId };

    public static ResultadoRelacionConfiguracionCredito Invalida(
        MotivoRechazoConfiguracionCreditoServicio motivo, string mensaje) =>
        new() { Motivo = motivo, Mensaje = mensaje };
}
