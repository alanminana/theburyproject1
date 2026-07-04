namespace TheBuryProject.Services
{
    /// <summary>
    /// Regla canónica del "puntaje/nivel crediticio efectivo" del cliente (0–5).
    /// El puntaje efectivo es el override manual (ClienteCreditoConfiguracion.NivelCreditoManual)
    /// cuando existe; si no, el puntaje automático de comportamiento (Cliente.PuntajeCliente).
    /// Autoridad única usada por cupo (CreditoDisponibleService), snapshot de venta (VentaService)
    /// y elegibilidad de garante (GaranteService), para no duplicar la regla ni divergir.
    /// </summary>
    public static class PuntajeCreditoEfectivo
    {
        public const string FuenteManual = "Manual";
        public const string FuenteAutomatico = "Automatico";

        /// <summary>Puntaje efectivo: manual (si hay override) o, en su defecto, el automático.</summary>
        public static int Resolver(int automatico, int? manual) => manual ?? automatico;

        /// <summary>Origen del puntaje efectivo aplicado: "Manual" o "Automatico".</summary>
        public static string Fuente(int? manual) => manual.HasValue ? FuenteManual : FuenteAutomatico;
    }
}
