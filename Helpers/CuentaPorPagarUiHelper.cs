using System.Globalization;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// D-03: textos y estilos de las pantallas de cuentas por pagar. Formato argentino explícito (es-AR)
    /// para no depender de la cultura del proceso. Solo presentación: ninguna regla de negocio.
    /// </summary>
    public static class CuentaPorPagarUiHelper
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        /// <summary>Clases de los controles de formulario (mismas que los filtros de Órdenes de Compra).</summary>
        public const string InputClass =
            "w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-white placeholder:text-slate-500 transition-colors focus:border-primary focus:ring-2 focus:ring-primary/50";

        public const string LabelClass = "mb-2 block text-xs font-bold uppercase tracking-wider text-slate-400";

        public static string Moneda(decimal valor) => valor.ToString("C2", Ar);

        public static string Fecha(DateTime? fecha) => fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", Ar) : "—";

        public static string NombreTipo(TipoComprobanteProveedor tipo) => tipo switch
        {
            TipoComprobanteProveedor.Factura => "Factura",
            TipoComprobanteProveedor.NotaDebito => "Nota de débito",
            TipoComprobanteProveedor.NotaCredito => "Nota de crédito",
            _ => tipo.ToString()
        };

        public static string NombreMedio(MedioPagoProveedor medio) => medio switch
        {
            MedioPagoProveedor.Efectivo => "Efectivo",
            MedioPagoProveedor.Transferencia => "Transferencia",
            MedioPagoProveedor.Cheque => "Cheque",
            _ => "Otro"
        };

        /// <summary>Una nota de crédito no se «paga»: se aplica. El estado usa el verbo que corresponde.</summary>
        public static string NombreEstado(EstadoComprobanteProveedor estado, TipoComprobanteProveedor tipo)
        {
            if (tipo == TipoComprobanteProveedor.NotaCredito)
            {
                return estado switch
                {
                    EstadoComprobanteProveedor.Pendiente => "Sin aplicar",
                    EstadoComprobanteProveedor.ParcialmentePagado => "Aplicada en parte",
                    EstadoComprobanteProveedor.Pagado => "Aplicada",
                    _ => "Anulado"
                };
            }

            return estado switch
            {
                EstadoComprobanteProveedor.Pendiente => "Pendiente",
                EstadoComprobanteProveedor.ParcialmentePagado => "Pago parcial",
                EstadoComprobanteProveedor.Pagado => "Pagado",
                _ => "Anulado"
            };
        }

        public static string EstadoBadgeClass(EstadoComprobanteProveedor estado) => estado switch
        {
            EstadoComprobanteProveedor.Pendiente => "badge-erp badge-erp-warning",
            EstadoComprobanteProveedor.ParcialmentePagado => "badge-erp badge-erp-info",
            EstadoComprobanteProveedor.Pagado => "badge-erp badge-erp-success",
            _ => "badge-erp badge-erp-neutral"
        };
    }
}
