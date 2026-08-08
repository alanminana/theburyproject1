using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Micro-lote 3: intersección de planes de Crédito Personal entre los productos de una venta.
/// </summary>
/// <remarks>
/// Defecto original: con planes disjuntos la resolución devolvía una lista vacía y el consumidor
/// la interpretaba como "no hay tabla de planes", cayendo al rango y a la tasa global. Eso
/// permitía financiar una cantidad de cuotas que ningún producto de la venta admite.
/// </remarks>
public class CreditoPlanesMultiproductoTests : IDisposable
{
    private const decimal TasaGlobalUnica = 10m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _configuracionPagoService;
    private readonly CreditoConfiguracionVentaService _configuracionVentaService;

    public CreditoPlanesMultiproductoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _configuracionPagoService = new ConfiguracionPagoService(
            _context, mapper, NullLogger<ConfiguracionPagoService>.Instance);

        _configuracionVentaService = new CreditoConfiguracionVentaService(
            _configuracionPagoService,
            NullLogger<CreditoConfiguracionVentaService>.Instance,
            new CreditoRangoProductoService(new ProductoCreditoRestriccionService(_context)));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // Producto individual
    // =====================================================================

    [Fact]
    public async Task Producto_SinConfigPropia_UsaLaTablaGlobal()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");

        var planes = await Resolver(b);

        Assert.True(planes.EsValido);
        Assert.Equal(new[] { 1, 4, 6 }, Cantidades(planes));
    }

    [Fact]
    public async Task Producto_ConConfigPropia_ReemplazaLaGlobalYNoOfreceLasCuotasSoloGlobales()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var planes = await Resolver(c);

        // 4 existe globalmente pero el producto personalizado no la habilita: la config propia
        // REEMPLAZA a la global, no la complementa.
        Assert.Equal(new[] { 1, 6 }, Cantidades(planes));
        Assert.DoesNotContain(4, Cantidades(planes));
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 de este test (asumía que la tasa propia del
    // producto, 0 % explícito, sobrevivía cuando no hay plan global equivalente). Bajo el
    // contrato congelado el plan global de cuotas es la única autoridad del porcentaje: sin plan
    // global para esa cantidad no hay porcentaje válido, ni siquiera cuando el producto declaró
    // 0 % (Fase 2, caso "Sin plan global").
    [Fact]
    public async Task Producto_ConCuotaSinPlanGlobalEquivalente_TasaEsInvalidaAunqueElProductoDeclareCero()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m)); // 3 cuotas: sin plan global equivalente

        var planes = await Resolver(d);

        Assert.Equal(new[] { 3 }, Cantidades(planes));
        Assert.Null(planes.BuscarPlan(3)!.TasaMensual);
    }

    // ML3 — renombrado (era "...HeredaLaTasaGlobalDeEsaCantidad"): no hay herencia. El TasaMensual
    // del producto no es fuente de porcentaje; el resultado es siempre el de la cuota global.
    [Fact]
    public async Task Producto_ConTasaNull_TasaSaleDeLaCuotaGlobalDeEsaCantidad()
    {
        await SeedGlobal((6, 7m));
        var producto = await SeedProducto("FIX-RV-PLANES-HEREDA");
        await SeedPlanProducto(producto.Id, 6, tasa: null);

        var planes = await Resolver(producto);

        Assert.Equal(7m, planes.BuscarPlan(6)!.TasaMensual);
    }

    // ML3 — renombrado (era "...SinCuotaGlobalEquivalente_HeredaLaTasaUnicaGlobal"): el nombre
    // original era incorrecto en las dos afirmaciones — SÍ existe cuota global equivalente (misma
    // cantidad, 1), y el resultado no es "herencia" de ninguna tasa única (ConfiguracionPago.
    // TasaInteresMensualCreditoPersonal, aquí ni siquiera seedeada): es el 0 % propio y explícito
    // de esa cuota global, que el TasaMensual del producto no puede alterar ni siendo null.
    [Fact]
    public async Task Producto_ConTasaNullYCuotaGlobalEnCero_TasaEsCeroNoLaTasaUnicaGlobal()
    {
        await SeedGlobal((1, 0m));
        var producto = await SeedProducto("FIX-RV-PLANES-HEREDA-UNICA");
        await SeedPlanProducto(producto.Id, 1, tasa: null);

        var planes = await Resolver(producto);

        // La cuota global de 1 tiene 0 % explícito: ese es el resultado, no el 10 % de
        // TasaGlobalUnica (que ademas no interviene: ResolverPlanesCreditoPersonalAsync no la lee).
        Assert.Equal(0m, planes.BuscarPlan(1)!.TasaMensual);
    }

    [Fact]
    public async Task Producto_ConPlanInactivo_NoAportaEsaCantidad()
    {
        await SeedGlobal((1, 0m), (6, 0m));
        var producto = await SeedProducto("FIX-RV-PLANES-INACTIVO", (1, 3m));
        await SeedPlanProducto(producto.Id, 6, tasa: 5m, activo: false);

        var planes = await Resolver(producto);

        Assert.Equal(new[] { 1 }, Cantidades(planes));
    }

    [Fact]
    public async Task SinPlanesGlobales_NiPorProducto_RechazaSinFallbackARango()
    {
        // Micro-lote 4: sin planes globales activos y sin plan propio, el producto depende de la
        // global; sin planes no hay cuotas disponibles → rechazo explícito, nunca fallback al rango.
        // (Supera la antigua semántica de Micro-lote 3 "SinTablaDePlanes rige la config única global".)
        var producto = await SeedProducto("FIX-RV-PLANES-SINTABLA");

        var planes = await Resolver(producto);

        Assert.False(planes.EsValido);
        Assert.Equal(MotivoSinPlanesCredito.SinPlanesGlobalesActivos, planes.Motivo);
        Assert.False(planes.RigeConfiguracionUnicaGlobal);
    }

    // =====================================================================
    // Multiproducto compatible
    // =====================================================================

    [Fact]
    public async Task Multiproducto_GlobalMasPersonalizado_DevuelveLaInterseccion()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var planes = await Resolver(b, c);

        Assert.Equal(new[] { 1, 6 }, Cantidades(planes));
        Assert.Equal(OrigenPlanesCredito.Mixto, planes.Origen);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 ("máximo entre productos" era autoridad del
    // porcentaje). El plan global de cuotas es la única autoridad: ambas cantidades tienen plan
    // global explícito (0 %) y ganan sobre la tasa propia del producto (2,5 % / 8 %), aunque sea
    // mayor.
    [Fact]
    public async Task Multiproducto_TasaDeCadaCantidad_SiempreSaleDelPlanGlobalAunqueElProductoDeclareOtra()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var planes = await Resolver(b, c);

        Assert.Equal(0m, planes.BuscarPlan(1)!.TasaMensual);
        Assert.Equal(0m, planes.BuscarPlan(6)!.TasaMensual);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 (0 % coincidente entre productos "sobrevivía" sin
    // plan global). Sin plan global para 3 cuotas no hay porcentaje válido, ni siquiera cuando
    // ambos productos declaran 0 % (Fase 2, caso "Sin plan global").
    [Fact]
    public async Task Multiproducto_SinPlanesGlobales_TasaEsInvalidaAunTodosDeclarenCero()
    {
        var uno = await SeedProducto("FIX-RV-PLANES-CERO-1", (3, 0m));
        var dos = await SeedProducto("FIX-RV-PLANES-CERO-2", (3, 0m));

        var planes = await Resolver(uno, dos);

        Assert.Null(planes.BuscarPlan(3)!.TasaMensual);
    }

    [Fact]
    public async Task Multiproducto_ElOrdenDeLosProductosNoCambiaElResultado()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var directo = await Resolver(b, c);
        var invertido = await Resolver(c, b);

        Assert.Equal(Cantidades(directo), Cantidades(invertido));
        Assert.Equal(
            directo.Planes.Select(p => p.TasaMensual).ToArray(),
            invertido.Planes.Select(p => p.TasaMensual).ToArray());
    }

    [Fact]
    public async Task Multiproducto_ProductoRepetido_NoCambiaLaResolucion()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var unaVez = await Resolver(c);
        var repetido = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(
            new[] { c.Id, c.Id, c.Id });

        Assert.Equal(Cantidades(unaVez), Cantidades(repetido));
    }

    [Fact]
    public async Task Multiproducto_AgregarUnProductoNuncaAmpliaLaInterseccion()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));

        var soloB = Cantidades(await Resolver(b));
        var bMasC = Cantidades(await Resolver(b, c));

        Assert.Subset(soloB.ToHashSet(), bMasC.ToHashSet());
        Assert.True(bMasC.Length <= soloB.Length);
    }

    // =====================================================================
    // Multiproducto incompatible (el defecto reportado)
    // =====================================================================

    [Fact]
    public async Task Multiproducto_InterseccionVacia_NoEsValidoYNoCaeAGlobal()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m));

        var planes = await Resolver(c, d);

        Assert.False(planes.EsValido);
        Assert.Equal(MotivoSinPlanesCredito.SinInterseccionEntreProductos, planes.Motivo);
        Assert.False(planes.RigeConfiguracionUnicaGlobal);
        Assert.Empty(planes.Planes);
        Assert.Contains("no comparten", planes.MensajeRechazo!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]   // válida para C, no para D
    [InlineData(3)]   // válida para D, no para C
    [InlineData(6)]   // válida para C, no para D
    [InlineData(4)]   // solo global
    [InlineData(9)]   // de ningún plan
    public async Task ConfigurarVenta_InterseccionVacia_RechazaCualquierCantidad(int cantidadCuotas)
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m));
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(
            Modelo(cantidadCuotas),
            Venta(c, d));

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
        Assert.Contains("no comparten", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigurarVenta_InterseccionVacia_NoDevuelveNiTasaNiRangoGlobal()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m));
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(3), Venta(c, d));

        // Sin comando no hay tasa aplicada, ni cuotas persistidas, ni snapshot de rango:
        // la configuración no llega a ConfigurarCreditoAsync.
        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
    }

    // =====================================================================
    // Multiproducto compatible: gate de la configuración
    // =====================================================================

    // ML2.1 — corrige la expectativa pre-ML2.1 ("máximo del servidor" entre productos). El plan
    // global (0 %, explícito para 6 cuotas) es la única autoridad, no el máximo entre productos
    // (8 %).
    [Fact]
    public async Task ConfigurarVenta_CantidadEnLaInterseccion_AplicaLaTasaDelPlanGlobal()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(6), Venta(b, c));

        Assert.True(resultado.EsValido);
        Assert.Equal(6, resultado.Comando!.CantidadCuotas);
        Assert.Equal(0m, resultado.Comando.TasaMensual);
    }

    [Fact]
    public async Task ConfigurarVenta_CantidadSoloGlobal_EsRechazadaConProductoPersonalizado()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        // 4 cuotas existe en la tabla global y entra en el rango 1–12, pero C no la habilita.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(4), Venta(b, c));

        Assert.False(resultado.EsValido);
        Assert.Equal(nameof(ConfiguracionCreditoVentaViewModel.CantidadCuotas), resultado.ErrorKey);
        Assert.Contains("no esta habilitada", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1: el plan global explícito (0 % para 6 cuotas) es la autoridad, no el valor que mande
    // el navegador ni la tasa propia del producto (8 %).
    [Fact]
    public async Task ConfigurarVenta_TasaDelNavegadorEnCaminoGlobal_EsIgnorada()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var modelo = Modelo(6);
        modelo.TasaMensual = 99m; // valor arbitrario del navegador: no es Manual, se ignora igual

        var resultado = await _configuracionVentaService.ResolverAsync(modelo, Venta(b, c));

        Assert.True(resultado.EsValido);
        Assert.Equal(0m, resultado.Comando!.TasaMensual);
    }

    // ML2.1: el plan global explícito (0 % para 6 cuotas) es la autoridad, no el valor forzado
    // por el navegador ni la tasa propia del producto (8 %).
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ConfigurarVenta_FuenteManualDeclaradaConMetodoGlobal_NoAceptaLaTasaDelNavegador(decimal tasaForzada)
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        // Un POST puede declarar FuenteConfiguracion=Manual manteniendo MetodoCalculo=Global
        // para que se acepte la tasa enviada. La combinación incoherente no habilita el bypass.
        var modelo = Modelo(6);
        modelo.FuenteConfiguracion = FuenteConfiguracionCredito.Manual;
        modelo.TasaMensual = tasaForzada;

        var resultado = await _configuracionVentaService.ResolverAsync(modelo, Venta(b, c));

        Assert.True(resultado.EsValido);
        Assert.Equal(0m, resultado.Comando!.TasaMensual);
    }

    [Fact]
    public async Task ConfigurarVenta_MetodoManual_TampocoEludeLaInterseccion()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var modelo = Modelo(4);
        modelo.MetodoCalculo = MetodoCalculoCredito.Manual;
        modelo.FuenteConfiguracion = FuenteConfiguracionCredito.Manual;
        modelo.TasaMensual = 1m;

        var resultado = await _configuracionVentaService.ResolverAsync(modelo, Venta(b, c));

        Assert.False(resultado.EsValido);
        Assert.Contains("no esta habilitada", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigurarVenta_ProductoQueNoPerteneceALaVenta_NoAmpliaLaInterseccion()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m));
        await SeedConfigGlobalPago();

        // La venta real es C+D (disjunta). Omitir D del request no la vuelve financiable:
        // la resolución usa los detalles de la venta, no la lista que envía el navegador.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(1), Venta(c, d));

        Assert.False(resultado.EsValido);
    }

    // =====================================================================
    // Producto bloqueado
    // =====================================================================

    [Fact]
    public async Task ConfigurarVenta_ProductoBloqueado_AnulaCreditoPersonalAunConPlanesCompatibles()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var a = await SeedProducto("FIX-RV-PLANES-A", (1, 0m), (6, 0m));
        await SeedRestriccion(a.Id, permitido: false);
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        // La intersección {1,6} no es vacía, pero el producto bloqueado anula la operación.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(6), Venta(a, c));

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
        Assert.Contains("bloquea", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigurarVenta_ProductoBloqueado_TampocoSeSalvaDeclarandoMetodoManual()
    {
        await SeedGlobal((1, 0m), (6, 0m));
        var a = await SeedProducto("FIX-RV-PLANES-A", (1, 0m), (6, 0m));
        await SeedRestriccion(a.Id, permitido: false);
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var modelo = Modelo(6);
        modelo.MetodoCalculo = MetodoCalculoCredito.Manual;
        modelo.FuenteConfiguracion = FuenteConfiguracionCredito.Manual;
        modelo.TasaMensual = 0m;

        var resultado = await _configuracionVentaService.ResolverAsync(modelo, Venta(a, c));

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
    }

    // =====================================================================
    // Regresión de un solo producto
    // =====================================================================

    [Fact]
    public async Task ConfigurarVenta_UnicoProductoGlobal_SigueFuncionando()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(4), Venta(b));

        Assert.True(resultado.EsValido);
        Assert.Equal(4m, resultado.Comando!.TasaMensual);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 ("usa su tasa propia"). 1 cuota tiene plan global
    // explícito (0 %); gana sobre la tasa propia del producto (2,5 %), aunque el producto sea el
    // único de la venta.
    [Fact]
    public async Task ConfigurarVenta_UnicoProductoPersonalizado_ElPlanGlobalSigueSiendoAutoridad()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(1), Venta(c));

        Assert.True(resultado.EsValido);
        Assert.Equal(0m, resultado.Comando!.TasaMensual);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 (tasa propia del producto, 0 % explícito,
    // sobrevivía sin plan global equivalente). Sin plan global para 3 cuotas no hay porcentaje
    // válido, ni siquiera cuando el producto declaró 0 % (Fase 2, caso "Sin plan global").
    [Fact]
    public async Task ConfigurarVenta_ProductoConCuotaSinPlanGlobal_EsInvalidaAunqueElProductoDeclareCero()
    {
        var d = await SeedProducto("FIX-RV-PLANES-D", (3, 0m));
        await SeedConfigGlobalPago();

        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(3), Venta(d));

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
        Assert.Contains("porcentaje", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 ("la tasa resuelta" era la del producto). El plan
    // global explícito (0 % para 6 cuotas) es la autoridad, no la tasa propia del producto (8 %).
    [Fact]
    public async Task ConfigurarVenta_ConAnticipo_ConservaElAnticipoYUsaLaTasaDelPlanGlobal()
    {
        await SeedGlobal((1, 0m), (6, 0m));
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        var modelo = Modelo(6);
        modelo.Anticipo = 2_500m;

        var resultado = await _configuracionVentaService.ResolverAsync(modelo, Venta(c));

        Assert.True(resultado.EsValido);
        Assert.Equal(2_500m, resultado.Comando!.Anticipo);
        Assert.Equal(0m, resultado.Comando.TasaMensual);
    }

    [Fact]
    public async Task ConfigurarVenta_CantidadFueraDeLosPlanes_SeRechazaAntesQueElRangoMinMax()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        var b = await SeedProducto("FIX-RV-PLANES-B");
        var c = await SeedProducto("FIX-RV-PLANES-C", (1, 2.5m), (6, 8m));
        await SeedConfigGlobalPago();

        // 9 queda fuera del rango y fuera de los planes: prima el mensaje de planes, que es el
        // que explica el motivo real (los productos), y no el del rango del metodo.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(9), Venta(b, c));

        Assert.False(resultado.EsValido);
        Assert.Contains("no esta habilitada", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MotivoRechazoConfiguracionCredito.Conflicto, resultado.Motivo);
    }

    [Fact]
    public async Task ConfigurarVenta_SinVenta_NoAplicaGateDePlanes()
    {
        await SeedGlobal((1, 0m), (4, 4m), (6, 0m));
        await SeedConfigGlobalPago();

        // Sin venta no hay productos: solo restringe la tabla global.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(4), venta: null);

        Assert.True(resultado.EsValido);
        Assert.Equal(4m, resultado.Comando!.TasaMensual);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private Task<PlanesCreditoPersonalResultado> Resolver(params Producto[] productos) =>
        _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(productos.Select(p => p.Id));

    private static int[] Cantidades(PlanesCreditoPersonalResultado planes) =>
        planes.Planes.Select(p => p.CantidadCuotas).ToArray();

    private static ConfiguracionCreditoVentaViewModel Modelo(int cantidadCuotas) =>
        new()
        {
            CreditoId = 500,
            VentaId = 99,
            ClienteId = 20,
            Monto = 120_000m,
            Anticipo = 0m,
            CantidadCuotas = cantidadCuotas,
            GastosAdministrativos = 0m,
            FechaPrimeraCuota = new DateTime(2026, 8, 10),
            FuenteConfiguracion = FuenteConfiguracionCredito.Global,
            MetodoCalculo = MetodoCalculoCredito.Global
        };

    private static VentaViewModel Venta(params Producto[] productos) =>
        new()
        {
            Id = 99,
            Total = 120_000m,
            Detalles = productos
                .Select(p => new VentaDetalleViewModel { ProductoId = p.Id, ProductoNombre = p.Nombre })
                .ToList()
        };

    private async Task<Producto> SeedProducto(string codigo, params (int Cuotas, decimal Tasa)[] planes)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..6];
        var producto = new Producto
        {
            Codigo = $"{codigo}-{sufijo}",
            Nombre = codigo,
            Categoria = new Categoria { Nombre = $"Cat {sufijo}", Codigo = $"CAT-{sufijo}" },
            Marca = new Marca { Nombre = $"Marca {sufijo}", Codigo = $"MAR-{sufijo}" },
            PrecioCompra = 0m,
            PrecioVenta = 120_000m,
            PorcentajeIVA = 21m
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        foreach (var (cuotas, tasa) in planes)
            await SeedPlanProducto(producto.Id, cuotas, tasa);

        return producto;
    }

    private async Task SeedPlanProducto(int productoId, int cuotas, decimal? tasa, bool activo = true)
    {
        _context.ProductoCreditoPersonalCuotas.Add(new ProductoCreditoPersonalCuota
        {
            ProductoId = productoId,
            CantidadCuotas = cuotas,
            TasaMensual = tasa,
            Activo = activo
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedGlobal(params (int Cuotas, decimal? Tasa)[] planes)
    {
        foreach (var (cuotas, tasa) in planes)
        {
            _context.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
            {
                CantidadCuotas = cuotas,
                TasaMensual = tasa,
                Activo = true
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedRestriccion(int productoId, bool permitido, int? maxCuotas = null)
    {
        _context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = productoId,
            Permitido = permitido,
            MaxCuotasCredito = maxCuotas,
            Activo = true
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedConfigGlobalPago()
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "Crédito personal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = TasaGlobalUnica,
            MinCuotasDefaultCreditoPersonal = 1,
            MaxCuotasDefaultCreditoPersonal = 12
        });
        await _context.SaveChangesAsync();
    }
}
