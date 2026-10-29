namespace TheBuryProject.Services.Interfaces
{
    public interface IBorradorCreditoVencimientoService
    {
        /// <summary>
        /// H2-C2: cancela los borradores de crédito abandonados (venta en PendienteFinanciacion con su crédito en
        /// PendienteConfiguracion, sin ninguna operación real) cuya última actividad es anterior o igual a
        /// <c>ahora - DiasVencimientoBorradorCredito</c>. Libera el cupo reutilizando la cancelación de venta.
        /// Idempotente y seguro con varias instancias.
        /// </summary>
        Task<BorradorCreditoVencimientoResultado> VencerBorradoresAsync(DateTime ahoraUtc, CancellationToken cancellationToken = default);
    }

    public sealed record BorradorCreditoVencimientoResultado(
        bool Habilitado,
        int PlazoDias,
        int Evaluados,
        IReadOnlyList<int> VentasVencidasIds,
        int Omitidos,
        int Fallidos);
}
