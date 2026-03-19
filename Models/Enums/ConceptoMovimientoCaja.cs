namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Concepto del movimiento de caja
    /// </summary>
    public enum ConceptoMovimientoCaja
    {
        VentaEfectivo = 0,
        VentaTarjeta = 1,
        VentaCheque = 2,
        CobroCuota = 3,
        CancelacionCredito = 4,
        AnticipoCredito = 5,
        GastoOperativo = 10,
        ExtraccionEfectivo = 11,
        DepositoEfectivo = 12,
        DevolucionCliente = 20,
        AjusteCaja = 30,
        Otro = 99
    }
}