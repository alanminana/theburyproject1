namespace TheBuryProject.Models.Enums
{
    /// <summary>Documentos cuyo número lleva un prefijo configurable (Configuración → Prefijos).</summary>
    public enum TipoPrefijoNumeracion
    {
        Cotizacion = 1,
        Venta = 2,
        FacturaA = 3,
        FacturaB = 4,
        FacturaC = 5,
        FacturaGenerica = 6,
        NotaCreditoFiscal = 7,
        NotaDebitoFiscal = 8,
        Credito = 9,
        Devolucion = 10,
        NotaCreditoDevolucion = 11,
        Garantia = 12,
        OrdenCompra = 13,
        ContratoCredito = 14,
        Pagare = 15
    }

    public static class TipoPrefijoNumeracionExtensions
    {
        public static string PrefijoPorDefecto(this TipoPrefijoNumeracion tipo) => tipo switch
        {
            TipoPrefijoNumeracion.Cotizacion => "COT",
            TipoPrefijoNumeracion.Venta => "VTA",
            TipoPrefijoNumeracion.FacturaA => "FA-A",
            TipoPrefijoNumeracion.FacturaB => "FA-B",
            TipoPrefijoNumeracion.FacturaC => "FA-C",
            TipoPrefijoNumeracion.FacturaGenerica => "FA",
            TipoPrefijoNumeracion.NotaCreditoFiscal => "NC",
            TipoPrefijoNumeracion.NotaDebitoFiscal => "ND",
            TipoPrefijoNumeracion.Credito => "CRE",
            TipoPrefijoNumeracion.Devolucion => "DEV",
            TipoPrefijoNumeracion.NotaCreditoDevolucion => "NC",
            TipoPrefijoNumeracion.Garantia => "GAR",
            TipoPrefijoNumeracion.OrdenCompra => "OC",
            TipoPrefijoNumeracion.ContratoCredito => "CVC",
            TipoPrefijoNumeracion.Pagare => "PAG",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };

        /// <summary>
        /// Largo máximo del prefijo: el número completo ("prefijo-yyyyMM-" más hasta 7 dígitos, 15 caracteres) debe entrar
        /// en la columna que lo guarda (20 en Ventas, Facturas, Devoluciones, Notas de crédito y Garantías; 50 en el resto).
        /// </summary>
        public static int LongitudMaxima(this TipoPrefijoNumeracion tipo) => tipo switch
        {
            TipoPrefijoNumeracion.Credito or TipoPrefijoNumeracion.OrdenCompra
                or TipoPrefijoNumeracion.ContratoCredito or TipoPrefijoNumeracion.Pagare => 10,
            _ => 5
        };

        public static string NombreVisible(this TipoPrefijoNumeracion tipo) => tipo switch
        {
            TipoPrefijoNumeracion.Cotizacion => "Cotización / presupuesto",
            TipoPrefijoNumeracion.Venta => "Venta",
            TipoPrefijoNumeracion.FacturaA => "Factura A",
            TipoPrefijoNumeracion.FacturaB => "Factura B",
            TipoPrefijoNumeracion.FacturaC => "Factura C",
            TipoPrefijoNumeracion.FacturaGenerica => "Factura (otros)",
            TipoPrefijoNumeracion.NotaCreditoFiscal => "Nota de crédito (factura)",
            TipoPrefijoNumeracion.NotaDebitoFiscal => "Nota de débito (factura)",
            TipoPrefijoNumeracion.Credito => "Crédito personal",
            TipoPrefijoNumeracion.Devolucion => "Devolución",
            TipoPrefijoNumeracion.NotaCreditoDevolucion => "Nota de crédito (devolución)",
            TipoPrefijoNumeracion.Garantia => "Garantía",
            TipoPrefijoNumeracion.OrdenCompra => "Orden de compra",
            TipoPrefijoNumeracion.ContratoCredito => "Contrato de venta a crédito",
            TipoPrefijoNumeracion.Pagare => "Pagaré",
            _ => tipo.ToString()
        };

        /// <summary>Forma del número, para mostrarla como ejemplo con el prefijo vigente.</summary>
        public static string Ejemplo(this TipoPrefijoNumeracion tipo, string prefijo) => tipo switch
        {
            TipoPrefijoNumeracion.Cotizacion => $"{prefijo}-20261026-0001",
            TipoPrefijoNumeracion.OrdenCompra => $"{prefijo}-2026-0001",
            _ => $"{prefijo}-202610-000001"
        };
    }
}
