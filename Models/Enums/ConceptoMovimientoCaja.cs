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
        VentaTransferencia = 6,
        VentaMercadoPago = 7,
        VentaNotaCredito = 9,
        LiquidacionMercadoLibre = 8,
        CancelacionCredito = 4,
        AnticipoCredito = 5,
        GastoOperativo = 10,
        ExtraccionEfectivo = 11,
        DepositoEfectivo = 12,
        /// <summary>Acreditación de un cheque recibido cuya caja de origen ya cerró (ingreso bancario, no físico).</summary>
        AcreditacionCheque = 13,
        /// <summary>Contrapartida del rechazo de un cheque que ya estaba acreditado (egreso bancario, no físico).</summary>
        RechazoCheque = 14,
        DevolucionCliente = 20,
        ReversionVenta = 21,
        AjusteCaja = 30,
        Otro = 99
    }
}