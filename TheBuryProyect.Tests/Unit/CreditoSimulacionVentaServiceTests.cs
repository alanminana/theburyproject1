using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

public sealed class CreditoSimulacionVentaServiceTests
{
    [Fact]
    public async Task Simular_FechaInvalida_UsaFallbackAlMesSiguiente()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial);
        var antes = DateTime.Today.AddMonths(1).Date;

        // ML8: sin ventaId, se necesita contexto de productos (ProductoIds) para que el service no
        // devuelva "contexto insuficiente" antes de llegar al fallback de fecha que este test prueba.
        var result = await service.SimularAsync(Request(fechaPrimeraCuota: "fecha-invalida", productoIds: new[] { 7 }));
        var despues = DateTime.Today.AddMonths(1).Date;

        Assert.True(result.EsValido);
        Assert.NotNull(financial.ReceivedFechaPrimeraCuota);
        Assert.InRange(financial.ReceivedFechaPrimeraCuota!.Value.Date, antes, despues);
    }

    [Theory]
    [InlineData(-1, 0, 5, "anticipo no puede ser negativo")]
    [InlineData(0, -1, 5, "gastos administrativos no pueden ser negativos")]
    [InlineData(0, 0, -1, "tasa mensual no puede ser negativa")]
    public async Task Simular_RechazaValoresNegativos(
        decimal anticipo, decimal gastos, decimal tasaGlobalConfigurada, string mensaje)
    {
        // ML6.1: request.TasaMensual ya no tiene autoridad (se eliminó la rama Manual), así que el
        // caso "tasa negativa" ya no puede inyectarse vía el request: se configura una tasa única
        // global negativa (dato mal cargado en Administración) para ejercitar el mismo guard
        // (tasaVal < 0) por el único camino legítimo que puede producirlo hoy.
        // ML8: se agrega ProductoIds — los dos primeros casos (anticipo/gastos negativos) rechazan
        // antes de resolver contexto, así que no les afecta; el tercero (tasa negativa) sí necesita
        // contexto para no rechazar antes por "contexto insuficiente".
        var service = CrearService(
            new RecordingFinancialCalculationService(),
            new TasaCreditoPersonalConfigService(tasaGlobalConfigurada));

        var result = await service.SimularAsync(Request(
            anticipo: anticipo,
            gastosAdministrativos: gastos,
            productoIds: new[] { 7 }));

        Assert.False(result.EsValido);
        Assert.Contains(mensaje, result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_TasaRequestSinFuenteManualValida_EsIgnoradaYUsaTasaGlobal()
    {
        // I6/ML4: una tasa mandada por el navegador ya no tiene prioridad. Sin FuenteConfiguracion
        // + MetodoCalculo ambos en Manual, el servidor la ignora y resuelve la global.
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(9m));

        var result = await service.SimularAsync(Request(
            tasaMensual: 3.5m,
            metodoCalculo: null,
            fuenteConfiguracion: null,
            productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.Equal(9m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_TasaManualConFuenteYMetodoManual_YaNoSeHonra_UsaTasaGlobalYFuentePlan()
    {
        // ML6.1 cierra el "Test obligatorio #9" anterior: FuenteConfiguracion + MetodoCalculo
        // ambos Manual ya NO conservan el porcentaje que mandó el operador (esa rama se eliminó
        // del service — contrato previo a ML6.1, inalcanzable desde la UI real de Configurar Venta
        // desde ML6). El servidor ignora tasaMensual igual que con cualquier otra combinación.
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(9m));

        var result = await service.SimularAsync(Request(
            tasaMensual: 3.5m,
            metodoCalculo: MetodoCalculoCredito.Manual,
            fuenteConfiguracion: FuenteConfiguracionCredito.Manual,
            productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.Equal(9m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_SinTasaRequest_UsaTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(8m));

        var result = await service.SimularAsync(Request(tasaMensual: null, productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task Simular_SinTasaRequestYSinConfiguracionGlobal_RetornaInvalido()
    {
        var service = CrearService(
            new RecordingFinancialCalculationService(),
            new TasaCreditoPersonalConfigService(null));

        var result = await service.SimularAsync(Request(tasaMensual: null, productoIds: new[] { 7 }));

        Assert.False(result.EsValido);
        Assert.Contains("tasa de inter", result.Error!.error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cr", result.Error.error);
    }

    [Fact]
    public async Task Simular_DevuelveCalculoFinancieroEsperado()
    {
        // ML6.1: tasaMensual del request ya no tiene autoridad; el 4.25% determinístico se fija
        // vía la tasa única global configurada (ML8: ahora vía ProductoIds + tabla de planes
        // legado/sin-tabla, la misma resolución que antes usaba el fallback sin contexto).
        var service = CrearService(
            new RecordingFinancialCalculationService(), new TasaCreditoPersonalConfigService(4.25m));

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m,
            anticipo: 1_000m,
            cuotas: 6,
            gastosAdministrativos: 250m,
            fechaPrimeraCuota: "2026-07-15",
            productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.NotNull(result.Plan);
        Assert.Equal(9_000m, result.Plan!.montoFinanciado);
        Assert.Equal(1_000m, result.Plan.cuotaEstimada);
        Assert.Equal(4.25m, result.Plan.tasaAplicada);
        Assert.Equal(250m, result.Plan.gastosAdministrativos);
        Assert.Equal(10_250m, result.Plan.totalPlan);
        Assert.Equal("2026-07-15", result.Plan.fechaPrimerPago);
    }

    [Fact]
    public async Task Simular_DevuelveSemaforoConMismaEstructura()
    {
        var financial = new RecordingFinancialCalculationService();
        var aptitud = new SemaforoOnlyClienteAptitudService(new SemaforoFinancieroViewModel
        {
            RatioVerdeMax = 0.12m,
            RatioAmarilloMax = 0.20m
        });
        var service = CrearService(financial, aptitudService: aptitud);

        // ML8: sin ventaId, se necesita ProductoIds como contexto para no rechazar por "contexto
        // insuficiente" antes de llegar al cálculo del semáforo que este test prueba.
        var result = await service.SimularAsync(Request(productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.Equal(0.12m, financial.ReceivedVerdeMax);
        Assert.Equal(0.20m, financial.ReceivedAmarilloMax);
        Assert.NotNull(result.Plan);
        Assert.Equal("verde", result.Plan!.semaforoEstado);
        Assert.Equal("Condiciones preliminares saludables.", result.Plan.semaforoMensaje);
        Assert.False(result.Plan.mostrarMsgIngreso);
        Assert.False(result.Plan.mostrarMsgAntiguedad);
    }

    // ── ML4: autoridad del monto y del porcentaje cuando hay VentaId ───────────────────────

    [Fact]
    public async Task Simular_ConVentaId_UsaTotalRealDeLaVentaIgnorandoTotalVentaDelRequest()
    {
        // Tests obligatorios #1/#2/#4: un total financiado falso en el request se ignora; el
        // saldo se calcula sobre el total real de la venta.
        var financial = new RecordingFinancialCalculationService();
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 12_345m,
            Detalles = new List<VentaDetalleViewModel>()
        };
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes()
        };
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            totalVenta: 999_999m,
            anticipo: 345m,
            ventaId: 55,
            tasaMensual: null,
            metodoCalculo: null,
            fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(12_345m, result.Plan!.totalVenta);
        Assert.Equal(12_000m, result.Plan.montoFinanciado);
        Assert.Equal(5m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_VentaInexistente_Rechaza()
    {
        var service = CrearService(new RecordingFinancialCalculationService(), ventaService: new StubVentaService(venta: null));

        var result = await service.SimularAsync(Request(ventaId: 55, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("no se encontr", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ConVentaId_AnticipoSuperaElTotalReal_Rechaza()
    {
        var venta = new VentaViewModel { Id = 55, ClienteId = 20, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var service = CrearService(new RecordingFinancialCalculationService(), ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            anticipo: 10_001m,
            ventaId: 55,
            metodoCalculo: null,
            fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("anticipo no puede superar", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ConVentaId_UsaTasaDelPlanDelProductoNoLaGlobal()
    {
        // La tasa del producto (test obligatorio #3, rama "por producto") pisa la tasa única
        // global cuando la venta tiene un plan propio para la cantidad de cuotas pedida.
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    // ── CSR-ML6: cuotasSinRecargo viaja en el JSON como metadata del plan, no inferida ────────

    [Fact]
    public async Task Simular_PlanConCuotasSinRecargoNoConsecutivas_ExponeLaListaTalCualEnElJson()
    {
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 100_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(10, 10m, new[] { 7 }, false, new[] { 1, 3, 5 }) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var service = CrearService(
            new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(ventaId: 55, cuotas: 10));

        Assert.True(result.EsValido);
        Assert.Equal(new[] { 1, 3, 5 }, result.Plan!.cuotasSinRecargo);
    }

    [Fact]
    public async Task Simular_PlanSinCuotasSinRecargoConfiguradas_ExponeListaVacia()
    {
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var service = CrearService(
            new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(ventaId: 55, cuotas: 6));

        Assert.True(result.EsValido);
        Assert.Empty(result.Plan!.cuotasSinRecargo);
    }

    // ── ML10/ML6.1: clasificación explícita de fuentePorcentaje contra los Origen reales que emite
    // ConfiguracionPagoService.ResolverPlanesCreditoPersonalAsync en producción (Global/Producto/
    // Mixto — SinTablaDePlanes es legado de dobles de test, el resolutor real no lo emite). Estos
    // tests fijan que, sin importar el Origen (disponibilidad de cantidades: quién aportó el plan),
    // el % siempre lo resuelve el mismo camino (ResolverTasaDelPlanOTasaGlobalAsync contra el plan
    // de cuotas) y fuentePorcentaje siempre es "Plan" — ML6.1 cerró el bug donde Origen.Producto se
    // reportaba como si el producto aportara el % (nunca lo hizo: ML2.1 ya lo sacaba del plan). ──

    [Fact]
    public async Task Simular_ConVentaId_PlanPropioDelProducto_FuenteEsPlan()
    {
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_PlanGlobalPorCantidadSinPlanPropio_UsaTasaPorCuotasDelPlanNoElEscalarGlobal()
    {
        // Escenario obligatorio: producto sin configuración propia, plan global con tasa distinta
        // por cantidad de cuotas. ResolverPlanesCreditoPersonalAsync real (ConfiguracionPagoService,
        // porProducto.Count == 0) devuelve Origen.Global, NUNCA SinTablaDePlanes. fuentePorcentaje
        // es "Plan" en ambos casos (ML6.1); lo que este test protege es que la tasa efectiva salga
        // de BuscarPlan(cuotas) — el plan por cantidad — y no del escalar plano TasaGlobal.
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        // TasaGlobal (99) deliberadamente distinta de la tasa del plan global para 6 cuotas (8): si
        // la rama "Global" volviera a usar el escalar plano en vez de BuscarPlan(cuotas), este test
        // lo detectaría además de detectar la etiqueta incorrecta.
        var planesGlobalesPorCantidad = PlanesCreditoPersonalResultado.Resuelto(
            new[]
            {
                new PlanCuotaCreditoPersonal(3, 5m, Array.Empty<int>(), true),
                new PlanCuotaCreditoPersonal(6, 8m, Array.Empty<int>(), true)
            },
            OrigenPlanesCredito.Global);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 99m, Planes = planesGlobalesPorCantidad };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_OrigenMixto_ProductoContribuyeTasa_FuenteEsPlan()
    {
        // Venta con más de un producto: uno tiene plan propio para esta cantidad, otro hereda la
        // global (Origen.Mixto). Sin importar quién aportó el plan efectivo para la cantidad
        // pedida (ProductosConPlanPropio no vacío o vacío), fuentePorcentaje sigue siendo "Plan".
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = 7, ProductoNombre = "Notebook" },
                new() { ProductoId = 8, ProductoNombre = "Mouse" }
            }
        };
        var planesMixtos = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, true) },
            OrigenPlanesCredito.Mixto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planesMixtos };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_PlanConCeroPorciento_FuenteEsPlanSinFallback()
    {
        // ML6.1 — Tests obligatorios: 0% es un recargo válido del plan, nunca reinterpretado como
        // "sin porcentaje configurado" ni como motivo para inventar otra fuente.
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 0m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(0m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_PlanSinPorcentajeExplicito_RechazaSinInventarFuente()
    {
        // ML6.1 — Tests obligatorios: plan inexistente/sin porcentaje configurado es inválido; el
        // servicio no debe caer a Global/Producto/Manual como fuente de rescate (ML2.1: TasaMensual
        // null en el plan = configuración inválida, nunca "heredar" de otra fuente).
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, null, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var service = CrearService(new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Null(result.Plan);
        Assert.Contains("porcentaje financiero", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 de este test (asumía que la configuración propia
    // del cliente pisaba el plan del producto). Bajo el contrato congelado (Fase 3) el cliente ya
    // no es autoridad del porcentaje ni siquiera pidiéndolo explícitamente por cliente: el plan
    // resuelto (15 %) sigue ganando sobre parametros.TasaMensual (12 %, dato legado del cliente).
    [Fact]
    public async Task Simular_ConVentaId_FuenteCliente_NoPisaElPlanDelProducto()
    {
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = planes,
            Parametros = new ParametrosCreditoCliente { TasaMensual = 12m } // ML2.1: legado, sin efecto
        };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: MetodoCalculoCredito.UsarCliente,
            fuenteConfiguracion: FuenteConfiguracionCredito.PorCliente));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_ManualManipulado_YaNoSeHonra_UsaElPlanDelProducto()
    {
        // ML6.1 — Fase 6 (request manipulado): FuenteConfiguracion + MetodoCalculo ambos Manual +
        // tasaMensual manipulada ya NO conservan la tasa que mandó el operador (ML6.1 eliminó esa
        // rama), ni siquiera con venta real y plan de producto disponible para la cantidad pedida.
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 3.5m,
            metodoCalculo: MetodoCalculoCredito.Manual, fuenteConfiguracion: FuenteConfiguracionCredito.Manual));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.NotEqual(3.5m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_CantidadNoHabilitadaEnPlanDelProducto_Rechaza()
    {
        // Test obligatorio #7 (cantidad no permitida).
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(3, 5m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var service = CrearService(new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("no está habilitada", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ConVentaId_SinPlanesGlobalesActivos_Rechaza()
    {
        // Test obligatorio #7 (plan inactivo: sin planes globales activos no hay cuotas).
        var venta = new VentaViewModel { Id = 55, ClienteId = 20, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinPlanesGlobales("No hay planes de Crédito Personal globales activos.")
        };
        var service = CrearService(new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(ventaId: 55, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("planes", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ConVentaId_ProductoBloqueante_Rechaza()
    {
        // Test obligatorio #8.
        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes()
        };
        var rangoBloqueado = new CreditoRangoProductoResultado(
            1, 120, 120, null, null, null, null,
            "No se puede configurar crédito personal: Notebook bloquea el medio de pago.");
        var service = CrearService(
            new RecordingFinancialCalculationService(),
            configService,
            ventaService: new StubVentaService(venta),
            creditoRangoProductoService: new StubCreditoRangoProductoService(rangoBloqueado));

        var result = await service.SimularAsync(Request(ventaId: 55, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("bloquea el medio de pago", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_NoEjecutaNingunaEscrituraDePersistencia()
    {
        // Test obligatorio #11: StubVentaService solo implementa GetByIdAsync (lectura); todos
        // sus métodos de escritura lanzan NotImplementedException. Que esto complete sin lanzar
        // demuestra que SimularAsync no creó crédito, venta, cuota, caja ni movimiento alguno.
        var venta = new VentaViewModel { Id = 55, ClienteId = 20, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes()
        };
        var service = CrearService(new RecordingFinancialCalculationService(), configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(ventaId: 55, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
    }

    [Fact]
    public async Task Simular_DevuelveVectorDeCuotasDeFinancialCalculationService()
    {
        // Test obligatorio #5/#6: el vector expuesto es exactamente el que produce
        // FinancialCalculationService.SimularPlanCredito (el mismo que persiste ML3), no un
        // recálculo propio. Se usa el servicio real (sin dobles) para esta comparación.
        // ML6.1: tasaMensual del request ya no tiene autoridad; el 10% determinístico se fija vía
        // la tasa única global configurada. ML8: ProductoIds da el contexto que antes daba el
        // fallback sin venta ni productos (retirado — ver CreditoSimulacionVentaService.SimularAsync).
        var real = new FinancialCalculationService();
        var service = new CreditoSimulacionVentaService(real, new TasaCreditoPersonalConfigService(10m));

        var result = await service.SimularAsync(Request(
            totalVenta: 100_000m, anticipo: 0m, cuotas: 12, productoIds: new[] { 7 }));

        Assert.True(result.EsValido);
        Assert.Equal(12, result.Plan!.cuotas.Count);
        Assert.Equal(result.Plan.totalAPagar, result.Plan.cuotas.Sum(c => c.total));
        Assert.All(result.Plan.cuotas, c => Assert.True(c.total >= 0 && c.interes >= 0 && c.capital >= 0));
    }

    [Fact]
    public async Task Simular_ConVentaId_AnticipoIgualAlTotalReal_GeneraSaldoCeroSinRechazar()
    {
        // Test obligatorio #6: la regla ya confirmada y congelada por
        // FinancialCalculationServiceTests.ComputeFinancedAmount_AnticipoIgualTotal_DevuelveCero
        // es que anticipo == total es válido (no un error): da saldo, recargo y cuotas en $0. Esta
        // pantalla no puede reabrir esa decisión; solo debe reflejarla sin romperse ni rechazarla.
        var venta = new VentaViewModel { Id = 55, ClienteId = 20, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes()
        };
        var real = new FinancialCalculationService();
        var service = new CreditoSimulacionVentaService(real, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            anticipo: 10_000m, cuotas: 3, ventaId: 55, tasaMensual: null, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(0m, result.Plan!.montoFinanciado);
        Assert.Equal(0m, result.Plan.interesTotal);
        Assert.Equal(0m, result.Plan.totalAPagar);
        Assert.All(result.Plan.cuotas, c => Assert.Equal(0m, c.total));
    }

    // ── ML8: resolución server-authoritative sin venta persistida (Cotización) ─────────────

    [Fact]
    public async Task Simular_SinVentaConProductoIds_UsaTasaDelPlanDelProductoNoLaGlobal()
    {
        // Mismo escenario que Simular_ConVentaId_UsaTasaDelPlanDelProductoNoLaGlobal, pero sin
        // venta: Cotización pasa ProductoIds/ClienteId directamente. Debe resolver exactamente
        // igual (misma precedencia, mismo método, sin reconstruir nada en un calculator paralelo).
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService);

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m, cuotas: 6, tasaMensual: 1m, clienteId: 20,
            productoIds: new[] { 7 }, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(15m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 de este test (asumía que el cliente era autoridad
    // del porcentaje). Sin tabla de planes en absoluto (SinTablaDePlanes) rige la tasa única
    // global (5 %), nunca parametros.TasaMensual (12 %, dato legado del cliente) — misma regla
    // que CreditoConfiguracionVentaService.ResolverAsync.
    [Fact]
    public async Task Simular_SinVentaConProductoIds_FuenteCliente_SinTablaDePlanes_UsaTasaGlobal()
    {
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes(),
            Parametros = new ParametrosCreditoCliente { TasaMensual = 12m } // ML2.1: legado, sin efecto
        };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService);

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m, cuotas: 3, tasaMensual: 1m, clienteId: 20,
            productoIds: new[] { 7 }, metodoCalculo: null,
            fuenteConfiguracion: FuenteConfiguracionCredito.PorCliente));

        Assert.True(result.EsValido);
        Assert.Equal(5m, financial.ReceivedTasaMensual);
        Assert.Equal("Plan", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_SinVentaConProductoIds_CantidadNoHabilitadaEnPlanDelProducto_Rechaza()
    {
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(3, 5m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var service = CrearService(new RecordingFinancialCalculationService(), configService);

        var result = await service.SimularAsync(Request(
            cuotas: 6, clienteId: 20, productoIds: new[] { 7 },
            metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("no está habilitada", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_SinVentaConProductoIds_SinPlanesGlobalesActivos_Rechaza()
    {
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinPlanesGlobales("No hay planes de Crédito Personal globales activos.")
        };
        var service = CrearService(new RecordingFinancialCalculationService(), configService);

        var result = await service.SimularAsync(Request(
            clienteId: 20, productoIds: new[] { 7 }, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Contains("planes", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_SinVentaSinProductoIds_RetornaInvalidoPorContextoInsuficiente()
    {
        // ML8 — Fase 1: auditado, el fallback legacy sin contexto (VentaId ni ProductoIds) no tiene
        // ningún caller productivo hoy: GET /Credito/Simular, el único caller histórico, fue
        // retirado antes de ML8 y ya no llama a este service (solo redirige a Index);
        // CreditoController.SimularPlanVenta siempre recibe ventaId desde toda navegación real; y
        // CotizacionPagoCalculator siempre pasa ProductoIds. Sin caller productivo, este service ya
        // no resuelve el escalar global legacy (ConfiguracionPago.TasaInteresMensualCreditoPersonal)
        // como si fuera el porcentaje vigente: devuelve inválido por contexto insuficiente.
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(8m));

        var result = await service.SimularAsync(Request(tasaMensual: null, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.False(result.EsValido);
        Assert.Null(result.Plan);
        Assert.Null(financial.ReceivedTasaMensual);
        Assert.Contains("contexto", result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ConVentaIdYSinVentaConProductoIdsEquivalentes_DevuelvenElMismoResultado()
    {
        // Paridad obligatoria Cotización ↔ Venta (test #18): mismos cliente/productos/cuotas/
        // anticipo/config vigente → mismo saldo, mismo % efectivo, mismo recargo, mismo total
        // financiado, mismo vector exacto de cuotas y misma fuente. Se usa el cálculo financiero
        // real (sin dobles) para comparar el resultado completo, no solo la cuota estimada.
        var planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 15m, new[] { 7 }, false) },
            OrigenPlanesCredito.Producto);
        var configService = new VentaConfiguracionPagoService { TasaGlobal = 5m, Planes = planes };
        var real = new FinancialCalculationService();

        var venta = new VentaViewModel
        {
            Id = 55,
            ClienteId = 20,
            Total = 100_000m,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = 7, ProductoNombre = "Notebook" } }
        };
        var servicioConVenta = new CreditoSimulacionVentaService(real, configService, ventaService: new StubVentaService(venta));
        var servicioSinVenta = new CreditoSimulacionVentaService(real, configService);

        var resultadoConVenta = await servicioConVenta.SimularAsync(Request(
            anticipo: 20_000m, cuotas: 6, ventaId: 55, tasaMensual: null,
            metodoCalculo: null, fuenteConfiguracion: null));
        var resultadoSinVenta = await servicioSinVenta.SimularAsync(Request(
            totalVenta: 100_000m, anticipo: 20_000m, cuotas: 6, clienteId: 20,
            productoIds: new[] { 7 }, tasaMensual: null, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(resultadoConVenta.EsValido);
        Assert.True(resultadoSinVenta.EsValido);
        var planConVenta = resultadoConVenta.Plan!;
        var planSinVenta = resultadoSinVenta.Plan!;

        Assert.Equal(planConVenta.fuentePorcentaje, planSinVenta.fuentePorcentaje);
        Assert.Equal(planConVenta.tasaAplicada, planSinVenta.tasaAplicada);
        Assert.Equal(planConVenta.montoFinanciado, planSinVenta.montoFinanciado);
        Assert.Equal(planConVenta.interesTotal, planSinVenta.interesTotal);
        Assert.Equal(planConVenta.totalAPagar, planSinVenta.totalAPagar);
        Assert.Equal(planConVenta.cuotas.Count, planSinVenta.cuotas.Count);
        for (var i = 0; i < planConVenta.cuotas.Count; i++)
        {
            Assert.Equal(planConVenta.cuotas[i].capital, planSinVenta.cuotas[i].capital);
            Assert.Equal(planConVenta.cuotas[i].interes, planSinVenta.cuotas[i].interes);
            Assert.Equal(planConVenta.cuotas[i].total, planSinVenta.cuotas[i].total);
        }
    }

    // ML6.1: sin la rama Manual, la mayoría de los tests que no ejercitan la resolución del %
    // en sí necesitan igual un IConfiguracionPagoService funcional (aunque sea el fallback "sin
    // contexto de productos") para no romper por TasaGlobalNoConfigurada. Default 5% cuando el
    // caller no wirea uno explícito — los tests que sí prueban la resolución del % siguen pasando
    // su propio doble.
    private static CreditoSimulacionVentaService CrearService(
        RecordingFinancialCalculationService financial,
        IConfiguracionPagoService? configuracionPagoService = null,
        IClienteAptitudService? aptitudService = null,
        IVentaService? ventaService = null,
        ICreditoRangoProductoService? creditoRangoProductoService = null) =>
        new(financial, configuracionPagoService ?? new TasaCreditoPersonalConfigService(5m),
            aptitudService, ventaService, creditoRangoProductoService);

    // ML6.1: sin default Manual/Manual — el service eliminó esa rama (contrato congelado: el plan
    // de cuotas es la única fuente del %, request.TasaMensual/MetodoCalculo/FuenteConfiguracion
    // nunca la pisan). tasaMensual/metodoCalculo/fuenteConfiguracion quedan en null por default: el
    // % lo determina siempre CrearService (vía IConfiguracionPagoService), nunca el request.
    private static CreditoSimulacionVentaRequest Request(
        decimal totalVenta = 10_000m,
        decimal? anticipo = 0m,
        int cuotas = 6,
        decimal? gastosAdministrativos = 0m,
        string? fechaPrimeraCuota = "2026-07-15",
        decimal? tasaMensual = null,
        int? ventaId = null,
        MetodoCalculoCredito? metodoCalculo = null,
        FuenteConfiguracionCredito? fuenteConfiguracion = null,
        IEnumerable<int>? productoIds = null,
        int? clienteId = null) =>
        new()
        {
            TotalVenta = totalVenta,
            Anticipo = anticipo,
            Cuotas = cuotas,
            GastosAdministrativos = gastosAdministrativos,
            FechaPrimeraCuota = fechaPrimeraCuota,
            TasaMensual = tasaMensual,
            VentaId = ventaId,
            MetodoCalculo = metodoCalculo,
            FuenteConfiguracion = fuenteConfiguracion,
            ProductoIds = productoIds,
            ClienteId = clienteId
        };

    private sealed class RecordingFinancialCalculationService : IFinancialCalculationService
    {
        public decimal? ReceivedVerdeMax { get; private set; }
        public decimal? ReceivedAmarilloMax { get; private set; }
        public decimal? ReceivedTasaMensual { get; private set; }
        public DateTime? ReceivedFechaPrimeraCuota { get; private set; }

        public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => throw new NotImplementedException();
        public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto) => throw new NotImplementedException();
        public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => throw new NotImplementedException();
        public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => throw new NotImplementedException();

        public SimulacionPlanCreditoDto SimularPlanCredito(
            decimal totalVenta,
            decimal anticipo,
            int cuotas,
            decimal tasaMensual,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m,
            IReadOnlyCollection<int>? cuotasSinRecargo = null)
        {
            ReceivedVerdeMax = semaforoRatioVerdeMax;
            ReceivedAmarilloMax = semaforoRatioAmarilloMax;
            ReceivedTasaMensual = tasaMensual;
            ReceivedFechaPrimeraCuota = fechaPrimeraCuota;

            return new SimulacionPlanCreditoDto
            {
                MontoFinanciado = totalVenta - anticipo,
                CuotaEstimada = 1_000m,
                TasaAplicada = tasaMensual,
                InteresTotal = 0m,
                TotalAPagar = 10_000m,
                GastosAdministrativos = gastosAdministrativos,
                TotalPlan = 10_000m + gastosAdministrativos,
                FechaPrimerPago = fechaPrimeraCuota,
                SemaforoEstado = "verde",
                SemaforoMensaje = "Condiciones preliminares saludables.",
                MostrarMsgIngreso = false,
                MostrarMsgAntiguedad = false
            };
        }
    }

    private sealed class SemaforoOnlyClienteAptitudService : IClienteAptitudService
    {
        private readonly SemaforoFinancieroViewModel _semaforo;

        public SemaforoOnlyClienteAptitudService(SemaforoFinancieroViewModel semaforo)
        {
            _semaforo = semaforo;
        }

        public Task<SemaforoFinancieroViewModel> GetSemaforoFinancieroAsync() => Task.FromResult(_semaforo);
        public Task UpdateSemaforoFinancieroAsync(SemaforoFinancieroViewModel model) => Task.CompletedTask;

        public Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto) => throw new NotImplementedException();
        public Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId) => throw new NotImplementedException();
        public Task<ConfiguracionCredito> GetConfiguracionAsync() => throw new NotImplementedException();
        public Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync() => throw new NotImplementedException();
        public Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null) => throw new NotImplementedException();
        public Task<decimal> GetCupoDisponibleAsync(int clienteId) => throw new NotImplementedException();
        public Task<decimal> GetCreditoUtilizadoAsync(int clienteId) => throw new NotImplementedException();
        public Task<ScoringThresholdsViewModel> GetScoringThresholdsAsync() => throw new NotImplementedException();
        public Task UpdateScoringThresholdsAsync(ScoringThresholdsViewModel model) => throw new NotImplementedException();
    }

    private sealed class TasaCreditoPersonalConfigService : IConfiguracionPagoService
    {
        private readonly decimal? _tasa;

        public TasaCreditoPersonalConfigService(decimal? tasa)
        {
            _tasa = tasa;
        }

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(_tasa);
        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo,
            int? perfilId,
            int? clienteId) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId,
            IEnumerable<int> productoIds) => throw new NotImplementedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        // ML8: este doble solo ejercita la resolución vía escalar único global (tasa única, sin
        // tabla de planes por cantidad) — igual que ConfiguracionPagoService cuando no hay ninguna
        // cuota global configurada. Los tests que sí prueban la tabla de planes por cantidad usan
        // VentaConfiguracionPagoService con un PlanesCreditoPersonalResultado explícito.
        public Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) =>
            Task.FromResult(PlanesCreditoPersonalResultado.SinTablaDePlanes());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
    }

    // ── ML4: dobles para las pruebas de autoridad de monto/porcentaje con VentaId ──────────

    private sealed class StubVentaService : IVentaService
    {
        private readonly VentaViewModel? _venta;

        public StubVentaService(VentaViewModel? venta)
        {
            _venta = venta;
        }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(_venta);

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

    private sealed class StubCreditoRangoProductoService : ICreditoRangoProductoService
    {
        private readonly CreditoRangoProductoResultado _resultado;

        public StubCreditoRangoProductoService(CreditoRangoProductoResultado resultado)
        {
            _resultado = resultado;
        }

        public Task<CreditoRangoProductoResultado> ResolverAsync(
            VentaViewModel? venta,
            TipoPago tipoPago,
            int minBase,
            int maxBase,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_resultado);
    }

    private sealed class VentaConfiguracionPagoService : IConfiguracionPagoService
    {
        public decimal? TasaGlobal { get; init; }
        public PlanesCreditoPersonalResultado? Planes { get; init; }
        public ParametrosCreditoCliente? Parametros { get; init; }

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(TasaGlobal);
        public Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) =>
            Task.FromResult(Planes ?? PlanesCreditoPersonalResultado.SinTablaDePlanes());
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) =>
            Task.FromResult(Parametros ?? new ParametrosCreditoCliente { TasaMensual = tasaGlobal });

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo,
            int? perfilId,
            int? clienteId) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId,
            IEnumerable<int> productoIds) => throw new NotImplementedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
    }
}
