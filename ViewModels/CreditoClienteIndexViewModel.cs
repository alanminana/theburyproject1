namespace TheBuryProject.ViewModels
{
    public class CreditoClienteIndexViewModel
    {
        public ClienteResumenViewModel Cliente { get; set; } = new();

        public string Documento { get; set; } = string.Empty;

        public int CantidadCreditos { get; set; }

        public decimal SaldoPendienteTotal { get; set; }

        public int CuotasVencidas { get; set; }

        /// <summary>
        /// PUN-ML10-G: mora de CAPITAL únicamente (predicado canónico
        /// <see cref="Services.EstadoCuotaResolver.EstaEnMoraCapitalDerivado"/>), nunca mezclada con
        /// punitorio. Reemplaza el uso ambiguo de <see cref="CuotaViewModel.SaldoPendiente"/> (que
        /// suma el campo legacy congelado <c>Cuota.MontoPunitorio</c>) para el panel de crédito del
        /// cliente. Calculado en <see cref="Services.CreditoUiQueryService.AgruparCreditosPorCliente"/>
        /// a partir de datos ya cargados, sin consulta adicional.
        /// </summary>
        public decimal MontoMoraCapital { get; set; }

        /// <summary>
        /// PUN-ML10-G: punitorio APLICADO pendiente de cobro (fuente autoritativa
        /// <see cref="Services.Interfaces.IPunitorioService.ObtenerPunitorioAplicadoPendientePorCuotasAsync"/>,
        /// una sola consulta batch). Deuda separada de <see cref="MontoMoraCapital"/> — nunca se suma
        /// a la misma cifra. Cero por defecto (no todos los callers de
        /// <see cref="Services.CreditoUiQueryService.AgruparCreditosPorCliente"/> tienen acceso a DB;
        /// sólo <c>CreditoController.PanelCliente</c> lo puebla con el valor real).
        /// </summary>
        public decimal MontoPunitorioAplicadoPendiente { get; set; }

        /// <summary>PUN-ML10-G: cantidad de cuotas con punitorio aplicado pendiente (ver <see cref="MontoPunitorioAplicadoPendiente"/>).</summary>
        public int CuotasConPunitorioAplicadoPendiente { get; set; }

        /// <summary>PUN-ML10-G: true si hay saldo real de punitorio aplicado pendiente (evita bloques/badges con $0,00 ficticio).</summary>
        public bool TienePunitorioAplicadoPendiente => MontoPunitorioAplicadoPendiente > 0m;

        public DateTime? ProximoVencimiento { get; set; }

        public string EstadoConsolidado { get; set; } = string.Empty;

        public List<CreditoViewModel> Creditos { get; set; } = new();
    }
}
