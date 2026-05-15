namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estado de acreditación de un movimiento de caja.
    /// Solo aplica a medios que requieren confirmación bancaria (Transferencia, MercadoPago, Tarjeta).
    /// </summary>
    public enum EstadoAcreditacionMovimientoCaja
    {
        NoAplica = 0,
        Pendiente = 1,
        Acreditado = 2,
        Rechazado = 3,
        Anulado = 4,
        Revertido = 5
    }
}
