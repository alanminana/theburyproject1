using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Parámetros ya resueltos para configurar un crédito.
    /// El controller valida y resuelve la fuente (Manual/PorCliente/Global/Perfil);
    /// este comando contiene los valores finales a persistir.
    /// </summary>
    public class ConfiguracionCreditoComando
    {
        public int CreditoId { get; init; }
        public int? VentaId { get; init; }
        public decimal Monto { get; init; }
        public decimal Anticipo { get; init; }
        public int CantidadCuotas { get; init; }
        public decimal TasaMensual { get; init; }
        public decimal GastosAdministrativos { get; init; }
        public DateTime? FechaPrimeraCuota { get; init; }
        public MetodoCalculoCredito MetodoCalculo { get; init; }
        public FuenteConfiguracionCredito FuenteConfiguracion { get; init; }
        public int? PerfilCreditoAplicadoId { get; init; }
        public string? PerfilCreditoAplicadoNombre { get; init; }
        public int CuotasMinPermitidas { get; init; }
        public int CuotasMaxPermitidas { get; init; }

        /// <summary>Fuente que originó la restricción efectiva: "Producto" o "Global".</summary>
        public string? FuenteRestriccionCuotasSnap { get; init; }

        /// <summary>ID del producto que impuso el límite más restrictivo (snapshot, sin FK).</summary>
        public int? ProductoIdRestrictivoSnap { get; init; }

        /// <summary>Máximo de cuotas global antes de aplicar restricción por producto.</summary>
        public int? MaxCuotasBaseSnap { get; init; }
    }
}
