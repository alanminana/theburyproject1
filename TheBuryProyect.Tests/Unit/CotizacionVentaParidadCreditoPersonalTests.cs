using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// ML7 — Paridad exacta Cotización ↔ Configurar Venta para Crédito Personal (Fases 6 y 8 del
/// contrato congelado). Ejercita el pipeline real de Cotización
/// (<see cref="CotizacionPagoCalculator"/> → <see cref="CreditoSimulacionVentaService"/> →
/// <see cref="FinancialCalculationService"/>) contra el mismo <see cref="CreditoSimulacionVentaService"/>
/// tal como lo usa Configurar Venta con venta asociada (camino <c>VentaId</c>, ver
/// <c>CreditoController.SimularPlanVenta</c>): los dos caminos deben producir resultados idénticos
/// a centavos, incluido el vector completo de cuotas. No reimplementa la fórmula financiera: compara
/// el output real de los dos caminos productivos, con dobles solo en los bordes de I/O (precio de
/// producto, planes configurados, venta asociada).
///
/// Cada caso también ejerce la Fase 8 (request manipulado): el camino "Configurar Venta" se llama
/// con <c>TasaMensual=99</c>, <c>MetodoCalculo=Manual</c> y <c>FuenteConfiguracion=Manual</c> — un
/// payload que, si tuviera autoridad, pisaría el porcentaje real del plan. El resultado esperado es
/// el porcentaje real del plan, sin excepción, y <c>fuentePorcentaje="Plan"</c>.
/// </summary>
public sealed class CotizacionVentaParidadCreditoPersonalTests
{
    private static readonly DateTime FechaPrimeraCuota = new(2026, 9, 8);
    private const int ProductoId = 77;
    private const int ClienteId = 30;
    private const int VentaId = 900;

    // Caso A — spec ML7: Precio 100000, Anticipo 0, Plan 4 cuotas, Recargo 4%
    //   => Recargo 4000, Total 104000, 4 cuotas de 26000 exactas.
    [Fact]
    public async Task CasoA_SinAnticipo_4Cuotas4Porciento_CotizacionYVentaCoinciden()
    {
        var resultado = await SimularAmbosCaminosAsync(
            precioProducto: 100_000m, anticipo: 0m, cuotas: 4, porcentajePlan: 4m);

        VerificarParidad(resultado,
            montoFinanciadoEsperado: 100_000m,
            recargoEsperado: 4_000m,
            totalFinanciadoEsperado: 104_000m);

        Assert.All(resultado.PlanCotizacion.Cuotas, c => Assert.Equal(26_000m, c.Total));

        // CSR-ML6: sin cuotas sin recargo configuradas, la metadata es vacía en ambos caminos —
        // nunca null.
        Assert.Empty(resultado.PlanVenta.cuotasSinRecargo);
        Assert.Empty(resultado.PlanCotizacion.CuotasSinRecargo);
    }

    // Caso B — spec ML7: Precio 100000, Anticipo 30000, Plan 12 cuotas, Recargo 10%
    //   => Saldo 70000, Recargo 7000, Total 77000 (no divide exacto: se valida por vector completo).
    [Fact]
    public async Task CasoB_ConAnticipo_12Cuotas10Porciento_CotizacionYVentaCoinciden()
    {
        var resultado = await SimularAmbosCaminosAsync(
            precioProducto: 100_000m, anticipo: 30_000m, cuotas: 12, porcentajePlan: 10m);

        VerificarParidad(resultado,
            montoFinanciadoEsperado: 70_000m,
            recargoEsperado: 7_000m,
            totalFinanciadoEsperado: 77_000m);
    }

    // Caso C — spec ML7: Precio 133100, Anticipo 0, Plan 6 cuotas, Recargo 0%
    //   => conserva 0% (nunca cae a otra tasa) y ajusta centavos exactamente.
    [Fact]
    public async Task CasoC_RecargoCero_6Cuotas_ConservaCeroYAjustaCentavosExacto()
    {
        var resultado = await SimularAmbosCaminosAsync(
            precioProducto: 133_100m, anticipo: 0m, cuotas: 6, porcentajePlan: 0m);

        VerificarParidad(resultado,
            montoFinanciadoEsperado: 133_100m,
            recargoEsperado: 0m,
            totalFinanciadoEsperado: 133_100m);
    }

    // Caso D — CSR-ML4 (P2/P3): Precio 100000, Anticipo 0, Plan 10 cuotas, Recargo 10%, cuotas sin
    // recargo 1,3,5. El vector completo (paridad Fase 6) debe seguir cerrando a centavos Y las
    // cuotas excluidas deben mostrar Interes=0 en AMBOS caminos — mismo plan resuelto server-side,
    // ninguno de los dos vuelve a consultar la configuración por separado.
    [Fact]
    public async Task CasoD_ConCuotasSinRecargo135_CotizacionYVentaCoincidenYRespetanLaExclusion()
    {
        var cuotasSinRecargo = new[] { 1, 3, 5 };

        var resultado = await SimularAmbosCaminosAsync(
            precioProducto: 100_000m, anticipo: 0m, cuotas: 10, porcentajePlan: 10m,
            cuotasSinRecargo: cuotasSinRecargo);

        VerificarParidad(resultado,
            montoFinanciadoEsperado: 100_000m,
            recargoEsperado: 10_000m,
            totalFinanciadoEsperado: 110_000m);

        foreach (var numero in cuotasSinRecargo)
        {
            Assert.Equal(0m, resultado.PlanCotizacion.Cuotas[numero - 1].Interes);
            Assert.Equal(0m, resultado.PlanVenta.cuotas[numero - 1].interes);
        }

        // Al menos una cuota no excluida debe llevar recargo (10 cuotas, 3 excluidas).
        Assert.Contains(resultado.PlanVenta.cuotas, c => c.interes > 0m);

        // CSR-ML6: la metadata "cuotas sin recargo" (no consecutivas) viaja tal cual en ambos
        // caminos — no es sólo una coincidencia de Interes == 0, es la lista configurada.
        Assert.Equal(cuotasSinRecargo, resultado.PlanVenta.cuotasSinRecargo);
        Assert.Equal(cuotasSinRecargo, resultado.PlanCotizacion.CuotasSinRecargo);
    }

    // Caso E — CSR-ML4 (P9): plan 0% con cuotas sin recargo configuradas. La lista viaja igual,
    // pero financieramente no cambia nada (Recargo ya es 0 en todas): sin diferencias artificiales
    // frente a un plan 0% sin exclusiones.
    [Fact]
    public async Task CasoE_RecargoCeroConCuotasSinRecargoConfiguradas_SinDiferenciasArtificiales()
    {
        var resultado = await SimularAmbosCaminosAsync(
            precioProducto: 90_000m, anticipo: 0m, cuotas: 9, porcentajePlan: 0m,
            cuotasSinRecargo: new[] { 1, 3, 5 });

        VerificarParidad(resultado,
            montoFinanciadoEsperado: 90_000m,
            recargoEsperado: 0m,
            totalFinanciadoEsperado: 90_000m);

        Assert.All(resultado.PlanVenta.cuotas, c => Assert.Equal(0m, c.interes));
        Assert.All(resultado.PlanCotizacion.Cuotas, c => Assert.Equal(0m, c.Interes));

        // CSR-ML6: la metadata configurada (1,3,5) sigue viajando tal cual con un plan 0% — no se
        // vacía ni se "completa" a las 9 cuotas sólo porque todas terminan con Interes 0.
        Assert.Equal(new[] { 1, 3, 5 }, resultado.PlanVenta.cuotasSinRecargo);
        Assert.Equal(new[] { 1, 3, 5 }, resultado.PlanCotizacion.CuotasSinRecargo);
    }

    // P10 — ninguna superficie de request de Venta/Cotización transporta CuotasSinRecargo: el
    // backend la resuelve siempre server-side desde el plan (ver CasoD/CasoE arriba). Guarda de
    // regresión: si algún día se agrega una propiedad con ese nombre a estos DTOs, este test debe
    // fallar y forzar a revisar por qué el cliente podría estar mandando la exclusión.
    [Fact]
    public void P10_RequestDeSimulacion_NoExponeCuotasSinRecargoComoSuperficieDeCliente()
    {
        var propiedades = typeof(CreditoSimulacionVentaRequest).GetProperties();
        Assert.DoesNotContain(propiedades, p => p.Name.Contains("CuotasSinRecargo", StringComparison.Ordinal));
    }

    private sealed record ResultadoParidad(
        CotizacionPlanPagoResultado PlanCotizacion,
        CreditoSimulacionVentaJson PlanVenta,
        int Cuotas);

    private static void VerificarParidad(
        ResultadoParidad resultado,
        decimal montoFinanciadoEsperado,
        decimal recargoEsperado,
        decimal totalFinanciadoEsperado)
    {
        var planCotizacion = resultado.PlanCotizacion;
        var planVenta = resultado.PlanVenta;

        // Fase 8 — la fuente siempre es "Plan", nunca la tasa manipulada (99%) que envió el
        // camino Configurar Venta.
        Assert.Equal("Plan", planCotizacion.FuentePorcentaje);
        Assert.Equal("Plan", planVenta.fuentePorcentaje);
        Assert.Equal(planVenta.tasaAplicada, planCotizacion.TasaMensual);
        Assert.NotEqual(99m, planVenta.tasaAplicada);

        // Fase 6 — paridad exacta a centavos entre los dos caminos productivos.
        Assert.Equal(montoFinanciadoEsperado, planCotizacion.SaldoAFinanciar);
        Assert.Equal(montoFinanciadoEsperado, planVenta.montoFinanciado);

        Assert.Equal(recargoEsperado, planCotizacion.CostoFinancieroTotal);
        Assert.Equal(recargoEsperado, planVenta.interesTotal);

        Assert.Equal(totalFinanciadoEsperado, planCotizacion.Total);
        Assert.Equal(totalFinanciadoEsperado, planCotizacion.TotalFinanciado);
        Assert.Equal(totalFinanciadoEsperado, planVenta.totalAPagar);

        Assert.Equal(resultado.Cuotas, planCotizacion.Cuotas.Count);
        Assert.Equal(resultado.Cuotas, planVenta.cuotas.Count);

        for (var i = 0; i < resultado.Cuotas; i++)
        {
            Assert.Equal(planVenta.cuotas[i].capital, planCotizacion.Cuotas[i].Capital);
            Assert.Equal(planVenta.cuotas[i].interes, planCotizacion.Cuotas[i].Interes);
            Assert.Equal(planVenta.cuotas[i].total, planCotizacion.Cuotas[i].Total);
        }

        // Suma exacta de cuotas == total financiado, en ambos caminos.
        Assert.Equal(totalFinanciadoEsperado, planCotizacion.Cuotas.Sum(c => c.Total));
        Assert.Equal(totalFinanciadoEsperado, planVenta.cuotas.Sum(c => c.total));

        // Misma última cuota (la que absorbe el residuo de redondeo).
        Assert.Equal(planVenta.cuotas[^1].total, planCotizacion.UltimaCuota);

        // CSR-ML6: metadata del plan (cuotas sin recargo) idéntica en ambos caminos — mismo
        // resultado canónico (CreditoSimulacionVentaJson), no reinterpretada por separado.
        Assert.Equal(planVenta.cuotasSinRecargo, planCotizacion.CuotasSinRecargo);
    }

    private static async Task<ResultadoParidad> SimularAmbosCaminosAsync(
        decimal precioProducto, decimal anticipo, int cuotas, decimal porcentajePlan,
        IReadOnlyList<int>? cuotasSinRecargo = null)
    {
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[]
            {
                new PlanCuotaCreditoPersonal(
                    cuotas, porcentajePlan, new[] { ProductoId }, false, cuotasSinRecargo)
            },
            OrigenPlanesCredito.Producto);

        var configuracionPagoService = new FakeConfiguracionPagoServiceParidad
        {
            // Distinta del plan a propósito: si algo cayera al fallback global el test lo detecta.
            TasaGlobal = 5m,
            Planes = planes,
            Parametros = new ParametrosCreditoCliente
            {
                // Legado sin autoridad desde ML2.1: si algo lo usara para el %, el test lo detecta.
                TasaMensual = 999m,
                GastosAdministrativos = 0m,
                CuotasMinimas = 1,
                CuotasMaximas = 24
            }
        };
        var financial = new FinancialCalculationService();

        // ---- Camino Cotización: CotizacionPagoCalculator → CreditoSimulacionVentaService (ProductoIds) ----
        var creditoServiceCotizacion = new CreditoSimulacionVentaService(financial, configuracionPagoService);
        var calculator = new CotizacionPagoCalculator(
            new FakeProductoServiceParidad(ProductoId, precioProducto),
            new FakeConfiguracionPagoGlobalQueryServiceParidad(),
            creditoServiceCotizacion,
            new FakeProductoCreditoRestriccionServiceParidad(),
            configuracionPagoService);

        var requestCotizacion = new CotizacionSimulacionRequest
        {
            ClienteId = ClienteId,
            Anticipo = anticipo,
            FechaCotizacion = FechaPrimeraCuota.AddMonths(-1),
            IncluirEfectivo = false,
            IncluirTransferencia = false,
            IncluirTarjetaCredito = false,
            IncluirTarjetaDebito = false,
            IncluirMercadoPago = false,
            IncluirCreditoPersonal = true,
            CuotasSolicitadas = new[] { cuotas },
            Productos = { new CotizacionProductoRequest { ProductoId = ProductoId, Cantidad = 1 } }
        };

        var resultadoCotizacion = await calculator.SimularAsync(requestCotizacion);

        Assert.True(resultadoCotizacion.Exitoso,
            string.Join(" ", resultadoCotizacion.Errores.Concat(resultadoCotizacion.Advertencias)));
        var opcionCredito = resultadoCotizacion.OpcionesPago
            .Single(o => o.MedioPago == CotizacionMedioPagoTipo.CreditoPersonal);
        Assert.True(opcionCredito.Disponible, opcionCredito.MotivoNoDisponible);
        var planCotizacion = opcionCredito.Planes.Single();

        // ---- Camino Configurar Venta: CreditoSimulacionVentaService (VentaId) — mismo servicio que
        // usa CreditoController.SimularPlanVenta cuando la venta ya tiene productos asociados. Se
        // envía además un payload manipulado (Fase 8): con VentaId ninguno de estos tres campos
        // debería tener autoridad sobre el porcentaje.
        var venta = new VentaViewModel
        {
            Id = VentaId,
            ClienteId = ClienteId,
            Total = precioProducto,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = ProductoId, ProductoNombre = "Producto paridad" }
            }
        };
        var creditoServiceVenta = new CreditoSimulacionVentaService(
            financial, configuracionPagoService, ventaService: new StubVentaServiceParidad(venta));

        var resultadoVenta = await creditoServiceVenta.SimularAsync(new CreditoSimulacionVentaRequest
        {
            VentaId = VentaId,
            Anticipo = anticipo,
            Cuotas = cuotas,
            FechaPrimeraCuota = FechaPrimeraCuota.ToString("yyyy-MM-dd"),
            TasaMensual = 99m,
            MetodoCalculo = MetodoCalculoCredito.Manual,
            FuenteConfiguracion = FuenteConfiguracionCredito.Manual
        });

        Assert.True(resultadoVenta.EsValido, resultadoVenta.Error?.error);

        return new ResultadoParidad(planCotizacion, resultadoVenta.Plan!, cuotas);
    }

    // ---- Dobles mínimos: solo los bordes de I/O reales (precio de producto, planes configurados,
    // venta asociada). El cálculo financiero es siempre el servicio real. ----

    private sealed class FakeProductoServiceParidad : IProductoService
    {
        private readonly int _productoId;
        private readonly decimal _precio;

        public FakeProductoServiceParidad(int productoId, decimal precio)
        {
            _productoId = productoId;
            _precio = precio;
        }

        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId) =>
            Task.FromResult(productoId == _productoId
                ? new ProductoPrecioVentaResultado
                {
                    ProductoId = productoId,
                    Codigo = $"P-{productoId}",
                    Nombre = "Producto paridad",
                    PrecioVenta = _precio,
                    FuentePrecio = FuentePrecioVigente.ProductoPrecioBase,
                    StockActual = 10
                }
                : null);

        public Task<IEnumerable<Producto>> GetAllAsync() => throw new NotSupportedException();
        public Task<Producto?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<Producto?> GetByIdParaHistorialAsync(int id) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => throw new NotSupportedException();
        public Task<Producto> CreateAsync(Producto producto) => throw new NotSupportedException();
        public Task<Producto> UpdateAsync(Producto producto) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int id) => throw new NotSupportedException();
        public Task ResolverIvaVentaAsync(Producto producto) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => throw new NotSupportedException();
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => throw new NotSupportedException();
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => throw new NotSupportedException();
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => throw new NotSupportedException();
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => throw new NotSupportedException();
        public Task<bool> ToggleDestacadoAsync(int id) => throw new NotSupportedException();
        public Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad) => throw new NotSupportedException();
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => throw new NotSupportedException();
    }

    private sealed class FakeConfiguracionPagoGlobalQueryServiceParidad : IConfiguracionPagoGlobalQueryService
    {
        public Task<ConfiguracionPagoGlobalResultado> ObtenerActivaParaVentaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfiguracionPagoGlobalResultado { Medios = new List<MedioPagoGlobalDto>() });
    }

    private sealed class FakeProductoCreditoRestriccionServiceParidad : IProductoCreditoRestriccionService
    {
        public Task<ProductoCreditoRestriccionResultado> ResolverAsync(
            IEnumerable<int> productoIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductoCreditoRestriccionResultado());
    }

    private sealed class StubVentaServiceParidad : IVentaService
    {
        private readonly VentaViewModel _venta;
        public StubVentaServiceParidad(VentaViewModel venta) => _venta = venta;

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult<VentaViewModel?>(_venta);

        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
        public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
        public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId) => throw new NotImplementedException();
        public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel) => throw new NotImplementedException();
        public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
        public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
        public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
        public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
        public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
    }

    private sealed class FakeConfiguracionPagoServiceParidad : IConfiguracionPagoService
    {
        public decimal? TasaGlobal { get; init; }
        public PlanesCreditoPersonalResultado? Planes { get; init; }
        public ParametrosCreditoCliente? Parametros { get; init; }

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(TasaGlobal);
        public Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) =>
            Task.FromResult(Planes ?? PlanesCreditoPersonalResultado.SinTablaDePlanes());
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) =>
            Task.FromResult(Parametros ?? new ParametrosCreditoCliente { TasaMensual = tasaGlobal });

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotSupportedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotSupportedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotSupportedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int id) => throw new NotSupportedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotSupportedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotSupportedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotSupportedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotSupportedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotSupportedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotSupportedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotSupportedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotSupportedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => throw new NotSupportedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => throw new NotSupportedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => throw new NotSupportedException();
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => throw new NotSupportedException();
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => throw new NotSupportedException();
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => throw new NotSupportedException();
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => throw new NotSupportedException();
    }
}
