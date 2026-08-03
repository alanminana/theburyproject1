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

        var result = await service.SimularAsync(Request(fechaPrimeraCuota: "fecha-invalida"));
        var despues = DateTime.Today.AddMonths(1).Date;

        Assert.True(result.EsValido);
        Assert.NotNull(financial.ReceivedFechaPrimeraCuota);
        Assert.InRange(financial.ReceivedFechaPrimeraCuota!.Value.Date, antes, despues);
    }

    [Theory]
    [InlineData(-1, 0, 5, "anticipo no puede ser negativo")]
    [InlineData(0, -1, 5, "gastos administrativos no pueden ser negativos")]
    [InlineData(0, 0, -1, "tasa mensual no puede ser negativa")]
    public async Task Simular_RechazaValoresNegativos(decimal anticipo, decimal gastos, decimal tasa, string mensaje)
    {
        var service = CrearService(new RecordingFinancialCalculationService());

        var result = await service.SimularAsync(Request(
            anticipo: anticipo,
            gastosAdministrativos: gastos,
            tasaMensual: tasa));

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
            fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(9m, financial.ReceivedTasaMensual);
        Assert.Equal("Global", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_TasaManualConFuenteYMetodoManual_SeHonra()
    {
        // Test obligatorio #9: la fuente manual válida (FuenteConfiguracion + MetodoCalculo ambos
        // Manual) conserva el porcentaje que mandó el operador, incluso con tasa global configurada.
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(9m));

        var result = await service.SimularAsync(Request(
            tasaMensual: 3.5m,
            metodoCalculo: MetodoCalculoCredito.Manual,
            fuenteConfiguracion: FuenteConfiguracionCredito.Manual));

        Assert.True(result.EsValido);
        Assert.Equal(3.5m, financial.ReceivedTasaMensual);
        Assert.Equal("Manual", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_SinTasaRequest_UsaTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(8m));

        var result = await service.SimularAsync(Request(tasaMensual: null));

        Assert.True(result.EsValido);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task Simular_SinTasaRequestYSinConfiguracionGlobal_RetornaInvalido()
    {
        var service = CrearService(
            new RecordingFinancialCalculationService(),
            new TasaCreditoPersonalConfigService(null));

        var result = await service.SimularAsync(Request(tasaMensual: null));

        Assert.False(result.EsValido);
        Assert.Contains("tasa de inter", result.Error!.error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cr", result.Error.error);
    }

    [Fact]
    public async Task Simular_DevuelveCalculoFinancieroEsperado()
    {
        var service = CrearService(new RecordingFinancialCalculationService());

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m,
            anticipo: 1_000m,
            cuotas: 6,
            gastosAdministrativos: 250m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 4.25m));

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

        var result = await service.SimularAsync(Request());

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
        Assert.Equal("Global", result.Plan.fuentePorcentaje);
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
        Assert.Equal("Producto", result.Plan!.fuentePorcentaje);
    }

    // ── ML10: clasificación explícita de fuentePorcentaje contra los Origen reales que emite
    // ConfiguracionPagoService.ResolverPlanesCreditoPersonalAsync en producción (Global/Producto/
    // Mixto — SinTablaDePlanes es legado de dobles de test, el resolutor real no lo emite). Los
    // tests preexistentes de "Global" de este archivo usan el doble PlanesCreditoPersonalResultado
    // .SinTablaDePlanes(), que NO es lo que la implementación real devuelve cuando ningún producto
    // tiene plan propio (ella emite Origen.Global): por eso no detectaban que, con contexto real de
    // producto (hayContextoDeProductos=true), CreditoSimulacionVentaService etiquetaba "Producto"
    // incluso cuando ningún producto de la venta tenía configuración propia. ──────────────────────

    [Fact]
    public async Task Simular_ConVentaId_PlanPropioDelProducto_FuenteEsProducto()
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
        Assert.Equal("Producto", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_PlanGlobalPorCantidadSinPlanPropio_FuenteEsGlobalNoProducto()
    {
        // Escenario obligatorio: producto sin configuración propia, plan global con tasa distinta
        // por cantidad de cuotas. ResolverPlanesCreditoPersonalAsync real (ConfiguracionPagoService,
        // porProducto.Count == 0) devuelve Origen.Global, NUNCA SinTablaDePlanes. La fuente debe
        // quedar "Global", no "Producto".
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
        Assert.Equal("Global", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_OrigenMixto_ProductoContribuyeTasa_FuenteEsProducto()
    {
        // Venta con más de un producto: uno tiene plan propio para esta cantidad, otro hereda la
        // global (Origen.Mixto). Si el plan efectivo para la cantidad pedida fue aportado por un
        // producto (ProductosConPlanPropio no vacío), la fuente sigue siendo "Producto".
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
        Assert.Equal("Producto", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_ConfiguracionPersonalizadaDelCliente_FuenteEsCliente()
    {
        // Configuración propia del cliente pisa el plan del producto (Origen.Producto igual que en
        // el primer caso) apenas se pide explícitamente por cliente.
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
            Parametros = new ParametrosCreditoCliente { TasaMensual = 12m }
        };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService, ventaService: new StubVentaService(venta));

        var result = await service.SimularAsync(Request(
            ventaId: 55, cuotas: 6, tasaMensual: 1m, metodoCalculo: MetodoCalculoCredito.UsarCliente,
            fuenteConfiguracion: FuenteConfiguracionCredito.PorCliente));

        Assert.True(result.EsValido);
        Assert.Equal(12m, financial.ReceivedTasaMensual);
        Assert.Equal("Cliente", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_ConVentaId_ModoManualAutorizado_FuenteEsManual()
    {
        // Manual con venta real de por medio (no solo el escenario sin venta ya cubierto arriba):
        // FuenteConfiguracion + MetodoCalculo ambos Manual conservan la tasa que mandó el operador
        // incluso con un plan de producto disponible para esa cantidad.
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
        Assert.Equal(3.5m, financial.ReceivedTasaMensual);
        Assert.Equal("Manual", result.Plan!.fuentePorcentaje);
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
        var real = new FinancialCalculationService();
        var service = new CreditoSimulacionVentaService(real, configuracionPagoService: null);

        var result = await service.SimularAsync(Request(totalVenta: 100_000m, anticipo: 0m, cuotas: 12, tasaMensual: 10m));

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
        Assert.Equal("Producto", result.Plan!.fuentePorcentaje);
    }

    [Fact]
    public async Task Simular_SinVentaConProductoIds_UsaTasaCliente_CuandoFuenteConfiguracionPorCliente()
    {
        var configService = new VentaConfiguracionPagoService
        {
            TasaGlobal = 5m,
            Planes = PlanesCreditoPersonalResultado.SinTablaDePlanes(),
            Parametros = new ParametrosCreditoCliente { TasaMensual = 12m }
        };
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, configService);

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m, cuotas: 3, tasaMensual: 1m, clienteId: 20,
            productoIds: new[] { 7 }, metodoCalculo: null,
            fuenteConfiguracion: FuenteConfiguracionCredito.PorCliente));

        Assert.True(result.EsValido);
        Assert.Equal(12m, financial.ReceivedTasaMensual);
        Assert.Equal("Cliente", result.Plan!.fuentePorcentaje);
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
    public async Task Simular_SinVentaSinProductoIds_MantieneFallbackGlobalLegacy()
    {
        // Compatibilidad con el único caller legítimo previo a ML8 (p. ej. /Credito/Simular):
        // sin VentaId y sin ProductoIds, sigue resolviendo la tasa única global tal cual antes.
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(8m));

        var result = await service.SimularAsync(Request(tasaMensual: null, metodoCalculo: null, fuenteConfiguracion: null));

        Assert.True(result.EsValido);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
        Assert.Equal("Global", result.Plan!.fuentePorcentaje);
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

    private static CreditoSimulacionVentaService CrearService(
        RecordingFinancialCalculationService financial,
        IConfiguracionPagoService? configuracionPagoService = null,
        IClienteAptitudService? aptitudService = null,
        IVentaService? ventaService = null,
        ICreditoRangoProductoService? creditoRangoProductoService = null) =>
        new(financial, configuracionPagoService, aptitudService, ventaService, creditoRangoProductoService);

    // Default Manual/Manual: la mayoría de estos tests preexistentes usan tasaMensual para fijar
    // un valor determinístico sin necesitar wirear IConfiguracionPagoService, no para probar la
    // autoridad del porcentaje en sí (eso lo cubren los tests de "TasaRequestSinFuenteManualValida"
    // y "TasaManualConFuenteYMetodoManual" de forma explícita, pisando estos defaults).
    private static CreditoSimulacionVentaRequest Request(
        decimal totalVenta = 10_000m,
        decimal? anticipo = 0m,
        int cuotas = 6,
        decimal? gastosAdministrativos = 0m,
        string? fechaPrimeraCuota = "2026-07-15",
        decimal? tasaMensual = 5m,
        int? ventaId = null,
        MetodoCalculoCredito? metodoCalculo = MetodoCalculoCredito.Manual,
        FuenteConfiguracionCredito? fuenteConfiguracion = FuenteConfiguracionCredito.Manual,
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
            decimal semaforoRatioAmarilloMax = 0.15m)
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
        public async Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) => PlanesCreditoPersonalStub.DesdeGlobales(await GetCuotasCreditoPersonalActivasAsync());
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
