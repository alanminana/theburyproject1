namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Fórmula única (backend) que separa los montos de una venta con envío a domicilio:
    ///
    /// <list type="bullet">
    /// <item><b>TotalProductos</b> = <c>Venta.Total</c>: ítems + descuentos + recargos/ajustes del medio de
    /// pago. No incluye el envío.</item>
    /// <item><b>ImporteEnvio</b> = <c>VentaEnvio.CostoEnvio</c> (único campo que guarda el importe; nunca
    /// negativo). Es un concepto separado: no forma parte del precio unitario, no lleva IVA en esta capa
    /// y no recibe recargos del medio de pago.</item>
    /// <item><b>TotalACobrar</b> = TotalProductos + ImporteEnvio: lo que el cliente debe entregar.</item>
    /// <item><b>TotalFacturable</b>: lo que hoy cubre el comprobante (<c>Factura.Total = Venta.Total</c>).
    /// El envío no está en ningún cálculo fiscal del sistema (sin línea, alícuota ni configuración), así
    /// que no se factura; la diferencia con TotalACobrar queda explícita en vez de mezclarse en silencio.
    /// Si el criterio fiscal cambia, es el único punto a modificar.</item>
    /// </list>
    /// </summary>
    public static class VentaMontos
    {
        /// <summary>Importe de envío efectivo: null o negativo cuentan como 0 (un envío nunca resta).</summary>
        public static decimal NormalizarImporteEnvio(decimal? costoEnvio) =>
            costoEnvio is > 0m
                ? Math.Round(costoEnvio.Value, 2, MidpointRounding.AwayFromZero)
                : 0m;

        public static decimal CalcularTotalACobrar(decimal totalProductos, decimal? costoEnvio) =>
            totalProductos + NormalizarImporteEnvio(costoEnvio);

        public static decimal CalcularTotalFacturable(decimal totalProductos) => totalProductos;
    }
}
