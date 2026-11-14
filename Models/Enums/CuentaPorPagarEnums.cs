namespace TheBuryProject.Models.Enums
{
    /// <summary>Tipo de comprobante que el proveedor emite (D-03: libro administrativo, sin fiscalidad propia).</summary>
    public enum TipoComprobanteProveedor
    {
        Factura = 1,
        NotaDebito = 2,
        NotaCredito = 3
    }

    /// <summary>
    /// Estado del comprobante, derivado de sus imputaciones y persistido de forma consistente.
    /// Para una nota de crédito significa: Pendiente = sin aplicar, ParcialmentePagado = aplicada en parte,
    /// Pagado = aplicada por completo.
    /// </summary>
    public enum EstadoComprobanteProveedor
    {
        Pendiente = 1,
        ParcialmentePagado = 2,
        Pagado = 3,
        Anulado = 4
    }

    public enum MedioPagoProveedor
    {
        Efectivo = 1,
        Transferencia = 2,
        Cheque = 3,
        Otro = 4
    }
}
