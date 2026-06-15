namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Precio de canal ML calculado para un producto interno.
    /// </summary>
    public sealed record MercadoLibrePrecioCanal(
        int ProductoId,
        decimal PrecioErp,
        decimal PrecioCanal,
        decimal Costo,
        bool DebajoDelMargenMinimo,
        decimal? MargenResultantePorcentaje);

    /// <summary>
    /// Desglose completo de la calculadora de precio ML (Fase G).
    /// Valores estimados: la autoridad sobre comisiones reales es Mercado Libre.
    /// </summary>
    public sealed record MercadoLibreDesglosePrecio(
        decimal Costo,
        decimal PrecioErp,
        decimal AjusteCanalPorcentaje,
        decimal PrecioCanal,
        decimal ComisionPorcentaje,
        decimal ComisionMonto,
        decimal EnvioEstimado,
        decimal NetoEstimado,
        decimal GananciaEstimada,
        decimal? RentabilidadPorcentaje,
        bool DebajoDelMargenMinimo);

    /// <summary>
    /// Calcula el precio de canal Mercado Libre a partir del precio ERP:
    /// lista de precios configurada → ajuste de canal → redondeo → control de margen.
    /// El backend es la autoridad; la UI solo previsualiza.
    /// </summary>
    public interface IMercadoLibrePricingService
    {
        /// <summary>
        /// Calcula el precio de canal para un conjunto de productos según la
        /// configuración vigente (lista, ajuste, redondeo, margen mínimo).
        /// </summary>
        Task<IReadOnlyDictionary<int, MercadoLibrePrecioCanal>> CalcularPrecioCanalAsync(
            IReadOnlyCollection<int> productoIds, CancellationToken ct = default);

        /// <summary>
        /// Desglose de calculadora para un producto: costo, precio ERP, ajuste,
        /// comisión, envío, neto estimado y rentabilidad.
        /// </summary>
        Task<MercadoLibreDesglosePrecio?> CalcularDesgloseAsync(int productoId, CancellationToken ct = default);

        /// <summary>Aplica la regla de redondeo configurable (ninguno|decena|centena|mil).</summary>
        decimal Redondear(decimal precio, string regla);
    }
}
