namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Reporte de una corrida del backfill de <c>PagoCuota</c> (PUN-ML2) desde
    /// <c>MovimientosCaja</c>. Cada movimiento con <c>Concepto == CobroCuota</c> cae en
    /// exactamente una categoría.
    /// </summary>
    public sealed record PagoCuotaBackfillResultado(
        int ReconstruidosCompletos,
        int ReconstruidosParcialmente,
        int Omitidos,
        int Ambiguos,
        IReadOnlyList<PagoCuotaBackfillDetalle> Detalles)
    {
        public int TotalProcesados => ReconstruidosCompletos + ReconstruidosParcialmente + Omitidos + Ambiguos;
    }

    /// <summary>Motivo de la categoría asignada a un movimiento puntual durante el backfill.</summary>
    public sealed record PagoCuotaBackfillDetalle(
        int MovimientoCajaId,
        int? CuotaId,
        string Categoria,
        string Motivo);
}
