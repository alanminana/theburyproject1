using TheBuryProject.Models.Entities;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Costo (para el negocio) de los servicios de una venta: entrega y armados. Es distinto del precio que se cobra al
    /// cliente (VentaEnvio.CostoEnvio, VentaDetalle.ArmadoSubtotal). Regla única: el costo REAL cargado por venta
    /// reemplaza al ESTÁNDAR congelado; nunca se descuentan ambos. Ventas anteriores (sin snapshot ni costo real) = 0.
    /// </summary>
    public static class CostoServiciosVenta
    {
        public static decimal Envio(VentaEnvio? envio) =>
            envio is null || envio.IsDeleted
                ? 0m
                : Redondear(envio.CostoRealServicio ?? envio.CostoEstandarServicio ?? 0m);

        public static decimal Armado(VentaDetalle detalle)
        {
            if (detalle.IsDeleted || detalle.TipoArmado is null || detalle.EntregaCajaCerrada)
                return 0m;

            return Redondear(detalle.ArmadoCostoReal ?? detalle.ArmadoCostoUnitarioEstandar * detalle.Cantidad);
        }

        public static decimal Armados(IEnumerable<VentaDetalle> detalles) => detalles.Sum(Armado);

        public static decimal Total(Venta venta) => Envio(venta.Envio) + Armados(venta.Detalles);

        private static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }
}
