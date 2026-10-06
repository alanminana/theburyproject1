namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Fórmula única (backend) de los montos de una venta con envío a domicilio. Hay dos modelos:
    ///
    /// <list type="bullet">
    /// <item><b>Ventas nuevas</b> (<c>VentaEnvio.IncluidoEnTotal = true</c>): <c>Venta.Total</c> ya incluye
    /// productos, armados y envío, todo con el recargo/ajuste del medio de pago. No hay importe de envío
    /// "aparte": <see cref="CalcularTotalACobrar"/> devuelve el Total y el comprobante lo cubre completo.</item>
    /// <item><b>Ventas legacy</b> (<c>IncluidoEnTotal = false</c>): <c>Venta.Total</c> son solo los productos y
    /// el envío (<c>VentaEnvio.CostoFueraDelTotal</c>, nunca negativo) se cobra aparte, sin IVA ni recargos del
    /// medio de pago. <b>TotalACobrar</b> = Total + ese envío; <b>TotalFacturable</b> = Total (el envío legacy
    /// nunca estuvo en un cálculo fiscal).</item>
    /// </list>
    ///
    /// El parámetro <c>costoEnvio</c> de <see cref="CalcularTotalACobrar"/> es, por lo tanto, solo la parte
    /// del envío que NO está dentro del Total. Si el criterio fiscal cambia, es el único punto a modificar.
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
