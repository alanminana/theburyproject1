using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    public class MercadoLibrePricingService : IMercadoLibrePricingService
    {
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IPrecioVigenteResolver _precioVigenteResolver;

        public MercadoLibrePricingService(
            IMercadoLibreConfiguracionService configuracionService,
            IPrecioVigenteResolver precioVigenteResolver)
        {
            _configuracionService = configuracionService;
            _precioVigenteResolver = precioVigenteResolver;
        }

        public async Task<IReadOnlyDictionary<int, MercadoLibrePrecioCanal>> CalcularPrecioCanalAsync(
            IReadOnlyCollection<int> productoIds, CancellationToken ct = default)
        {
            var resultados = new Dictionary<int, MercadoLibrePrecioCanal>();

            if (productoIds.Count == 0)
                return resultados;

            var config = await _configuracionService.GetAsync(ct);

            var preciosVigentes = await _precioVigenteResolver.ResolverBatchAsync(
                productoIds, config.ListaPrecioId, cancellationToken: ct);

            foreach (var (productoId, vigente) in preciosVigentes)
            {
                var precioErp = vigente.PrecioFinalConIva;
                var costo = vigente.CostoSnapshot;

                var precioCanal = precioErp * (1 + config.AjusteCanalPorcentaje / 100m);
                precioCanal = Redondear(precioCanal, config.ReglaRedondeo);

                decimal? margenResultante = null;
                var debajoDelMinimo = false;

                if (costo > 0)
                {
                    margenResultante = Math.Round((precioCanal - costo) / costo * 100m, 2);

                    if (config.MargenMinimoPorcentaje.HasValue)
                        debajoDelMinimo = margenResultante < config.MargenMinimoPorcentaje.Value;
                }

                resultados[productoId] = new MercadoLibrePrecioCanal(
                    productoId, precioErp, precioCanal, costo, debajoDelMinimo, margenResultante);
            }

            return resultados;
        }

        public async Task<MercadoLibreDesglosePrecio?> CalcularDesgloseAsync(
            int productoId, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            var precios = await CalcularPrecioCanalAsync(new[] { productoId }, ct);
            if (!precios.TryGetValue(productoId, out var canal))
                return null;

            var comisionMonto = Math.Round(canal.PrecioCanal * config.ComisionEstimadaPorcentaje / 100m, 2);
            var envio = config.CostoEnvioEstimado;
            var neto = canal.PrecioCanal - comisionMonto - envio;
            var ganancia = neto - canal.Costo;

            decimal? rentabilidad = canal.Costo > 0
                ? Math.Round(ganancia / canal.Costo * 100m, 2)
                : null;

            return new MercadoLibreDesglosePrecio(
                Costo: canal.Costo,
                PrecioErp: canal.PrecioErp,
                AjusteCanalPorcentaje: config.AjusteCanalPorcentaje,
                PrecioCanal: canal.PrecioCanal,
                ComisionPorcentaje: config.ComisionEstimadaPorcentaje,
                ComisionMonto: comisionMonto,
                EnvioEstimado: envio,
                NetoEstimado: neto,
                GananciaEstimada: ganancia,
                RentabilidadPorcentaje: rentabilidad,
                DebajoDelMargenMinimo: canal.DebajoDelMargenMinimo);
        }

        public decimal Redondear(decimal precio, string regla)
        {
            var factor = regla switch
            {
                "decena" => 10m,
                "centena" => 100m,
                "mil" => 1000m,
                _ => 0m
            };

            if (factor == 0m)
                return Math.Round(precio, 2);

            // Redondeo hacia abajo al múltiplo (precio psicológico estable, nunca sube solo).
            var redondeado = Math.Floor(precio / factor) * factor;

            // Nunca devolver 0 por redondear un precio chico.
            return redondeado > 0 ? redondeado : Math.Round(precio, 2);
        }
    }
}
