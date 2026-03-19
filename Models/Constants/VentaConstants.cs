namespace TheBuryProject.Models.Constants
{
    /// <summary>
    /// Constantes para el m�dulo de ventas
    /// </summary>
    public static class VentaConstants
    {
        /// <summary>
        /// Tasa de IVA (21%)
        /// </summary>
        public const decimal IVA_RATE = 0.21m;
        
        /// <summary>
        /// Divisor para calcular base imponible desde precio con IVA (1 + 0.21 = 1.21)
        /// Uso: precioConIVA / IVA_DIVISOR = precioSinIVA
        /// </summary>
        public const decimal IVA_DIVISOR = 1.21m;
        
        public const int DIAS_VENCIMIENTO_ALERTA = 30;
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
            public const string VENTA_YA_CANCELADA = "La venta ya est� cancelada";
            public const string REQUIERE_AUTORIZACION = "La venta requiere autorizaci�n antes de continuar";
            public const string CREDITO_NO_ENCONTRADO = "Cr�dito no encontrado";
            public const string CREDITO_INSUFICIENTE = "El monto a financiar (${0:N2}) supera el cr�dito disponible (${1:N2})";
        }
    }
}