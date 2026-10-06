namespace TheBuryProject.Models.Constants
{
    /// <summary>
    /// Constantes para el módulo de ventas
    /// </summary>
    public static class VentaConstants
    {
        public const string PREFIJO_COTIZACION = "COT";
        public const string PREFIJO_VENTA = "VTA";
        public const string FORMATO_NUMERO_VENTA = "{0}-{1}-{2:D6}";
        public const string FORMATO_PERIODO = "yyyyMM";

        public static class FacturaPrefijos
        {
            public const string TIPO_A = "FA-A";
            public const string TIPO_B = "FA-B";
            public const string TIPO_C = "FA-C";
            public const string NOTA_CREDITO = "NC";
            public const string NOTA_DEBITO = "ND";
            public const string GENERICO = "FA";
        }

        public static class ErrorMessages
        {
            public const string VENTA_NO_ENCONTRADA = "Venta no encontrada";
            public const string VENTA_YA_CANCELADA = "La venta ya está cancelada";
        }
    }
}
