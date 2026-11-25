using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Filtros del reporte de IVA de compras y ventas.
    /// </summary>
    public class ReporteIvaFiltroViewModel
    {
        [Display(Name = "Fecha desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }

        [Display(Name = "Fecha hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }

        /// <summary>
        /// Tipo de operación: "compras", "ventas" o "ambas".
        /// </summary>
        [Display(Name = "Tipo de operación")]
        public string Tipo { get; set; } = "ambas";

        /// <summary>
        /// Alícuota opcional (porcentaje). Filtra por la alícuota histórica de cada línea
        /// (ventas y compras). Las compras legacy sin desglose por línea cuentan como 21%.
        /// </summary>
        [Display(Name = "Alícuota (%)")]
        public decimal? Alicuota { get; set; }

        public bool IncluyeCompras => !string.Equals(Tipo, "ventas", StringComparison.OrdinalIgnoreCase);
        public bool IncluyeVentas => !string.Equals(Tipo, "compras", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resultado del reporte de IVA. Los importes de ventas provienen del snapshot
    /// histórico de VentaDetalle y los de compras del snapshot por línea de
    /// OrdenCompraDetalle (neto/IVA/alícuota al momento de la operación).
    /// </summary>
    public class ReporteIvaResultadoViewModel
    {
        public ReporteIvaFiltroViewModel Filtro { get; set; } = new();

        // Compras (OrdenCompra en estado Confirmada / EnTransito / Recibida)
        public int ComprasCantidad { get; set; }
        public decimal ComprasNeto { get; set; }
        public decimal ComprasIva { get; set; }
        public decimal ComprasTotal { get; set; }

        public List<ReporteIvaAlicuotaItemViewModel> ComprasPorAlicuota { get; set; } = new();

        /// <summary>
        /// Órdenes legacy sin snapshot de IVA por línea: se agrupan a la alícuota
        /// general de cabecera (21%, la única aplicada al momento de la operación).
        /// </summary>
        public int ComprasSinDesglosePorLinea { get; set; }

        // Ventas (Venta en estado Confirmada / Facturada / Entregada)
        public int VentasCantidad { get; set; }
        public decimal VentasNeto { get; set; }
        public decimal VentasIva { get; set; }
        public decimal VentasTotal { get; set; }

        public List<ReporteIvaAlicuotaItemViewModel> VentasPorAlicuota { get; set; } = new();

        /// <summary>
        /// Ventas legacy con importe facturado pero sin snapshot de desglose de IVA.
        /// Se excluyen de los totales y se informan aparte; nunca se reconstruye el
        /// IVA desde la alícuota actual del producto.
        /// </summary>
        public int VentasSinDesglose { get; set; }

        public decimal VentasSinDesgloseTotal { get; set; }

        // Notas de crédito FISCALES externas vinculadas (neteo INFORMATIVO del IVA, criterio interno del propietario): se imputan al
        // período de la FECHA DE LA NC FISCAL, nunca al de la venta original, y solo existen si hay una NC fiscal válida vinculada.
        public int NotasCreditoCantidad { get; set; }
        public decimal NotasCreditoNeto { get; set; }
        public decimal NotasCreditoIva { get; set; }
        public decimal NotasCreditoTotal { get; set; }
        public List<ReporteIvaAlicuotaItemViewModel> NotasCreditoPorAlicuota { get; set; } = new();

        /// <summary>NC fiscales vinculadas cuyo IVA no se pudo repartir (venta con líneas sin desglose histórico o con criterio fiscal no reconocido, excluida de los totales de ventas): no se netean y se informan.</summary>
        public int NotasCreditoSinDesglose { get; set; }
        public decimal NotasCreditoSinDesgloseTotal { get; set; }

        /// <summary>
        /// P-02: ventas cuya versión de criterio fiscal no está registrada. No se recalculan con otra regla: quedan fuera
        /// de los totales y se informan aparte (el detalle técnico va solo al log).
        /// </summary>
        public int VentasVersionFiscalInvalida { get; set; }

        public decimal VentasVersionFiscalInvalidaTotal { get; set; }

        /// <summary>
        /// IVA de ventas menos IVA de compras. Se denomina "Diferencia de IVA" porque
        /// no contempla percepciones, retenciones ni saldos anteriores; resta el IVA de las notas de crédito fiscales externas vinculadas.
        /// </summary>
        public decimal DiferenciaIva => VentasIva - NotasCreditoIva - ComprasIva;
    }

    /// <summary>
    /// Desglose de ventas por alícuota histórica.
    /// </summary>
    public class ReporteIvaAlicuotaItemViewModel
    {
        public decimal Porcentaje { get; set; }
        public int Operaciones { get; set; }
        public decimal Neto { get; set; }
        public decimal Iva { get; set; }
        public decimal Total { get; set; }
    }
}
