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
/// Micro-lote 4: los planes globales activos son la ÚNICA fuente de cantidades disponibles de
/// Crédito Personal. No existe fallback a un rango mín/máx; las columnas legacy
/// <c>Min/MaxCuotasDefaultCreditoPersonal</c> quedaron inertes.
/// </summary>
public class ConfiguracionCreditoPersonalPlanesGlobalesTests : IDisposable
{
    private const decimal TasaGlobalUnica = 10m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _configuracionPagoService;
    private readonly CreditoConfiguracionVentaService _configuracionVentaService;

    public ConfiguracionCreditoPersonalPlanesGlobalesTests()
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
    // Fuente única de cantidades
    // =====================================================================

    [Fact]
    public async Task Planes146_ProducenExactamente146()
    {
        await SeedConfigGlobalPago();
        await SeedGlobal((1, 0m), (4, 4m), (6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.True(planes.EsValido);
        Assert.Equal(new[] { 1, 4, 6 }, Cantidades(planes));
    }

    [Fact]
    public async Task RangoLegacy1a12_NoAlteraElResultado()
    {
        // El rango legacy queda 1..12 pero solo los planes {1,4,6} deben aparecer: nada de 2,3,5…
        await SeedConfigGlobalPago(minLegacy: 1, maxLegacy: 12);
        await SeedGlobal((1, 0m), (4, 4m), (6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.Equal(new[] { 1, 4, 6 }, Cantidades(planes));
        Assert.DoesNotContain(2, Cantidades(planes));
        Assert.DoesNotContain(3, Cantidades(planes));
        Assert.DoesNotContain(12, Cantidades(planes));
    }

    [Fact]
    public async Task CantidadNoConfigurada_NoAparece()
    {
        await SeedConfigGlobalPago();
        await SeedGlobal((1, 0m), (4, 4m), (6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.DoesNotContain(9, Cantidades(planes));
    }

    [Fact]
    public async Task CantidadInactiva_NoAparece()
    {
        await SeedConfigGlobalPago();
        await SeedGlobal((1, 0m), (4, 4m));
        await SeedGlobalInactivo(6, 0m);
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.Equal(new[] { 1, 4 }, Cantidades(planes));
        Assert.DoesNotContain(6, Cantidades(planes));
    }

    // =====================================================================
    // Sin planes activos → no configurable (sin fallback a rango)
    // =====================================================================

    [Fact]
    public async Task SinPlanesGlobales_ProductoDependienteDeGlobal_NoConfigurable()
    {
        await SeedConfigGlobalPago();
        var producto = await SeedProducto("ML4-A"); // sin plan propio: depende de la global

        var planes = await Resolver(producto);

        Assert.False(planes.EsValido);
        Assert.Equal(MotivoSinPlanesCredito.SinPlanesGlobalesActivos, planes.Motivo);
        Assert.False(planes.RigeConfiguracionUnicaGlobal);
    }

    [Fact]
    public async Task SinPlanesGlobales_ConRangoLegacyPresente_SigueSinFallbackARango()
    {
        // Aunque exista el rango legacy 1..12, sin planes activos no se ofrece ninguna cuota.
        await SeedConfigGlobalPago(minLegacy: 1, maxLegacy: 12);
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.False(planes.EsValido);
        Assert.Empty(planes.Planes);
        Assert.Equal(MotivoSinPlanesCredito.SinPlanesGlobalesActivos, planes.Motivo);
    }

    [Fact]
    public async Task SinPlanesGlobales_ConfigurarVenta_RechazaYNoArmaComando()
    {
        await SeedConfigGlobalPago(minLegacy: 1, maxLegacy: 12);
        var producto = await SeedProducto("ML4-A");

        // Forzar 6 cuotas sin planes activos: el backend rechaza, no cae al rango 1..12.
        var resultado = await _configuracionVentaService.ResolverAsync(Modelo(6), Venta(producto));

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Comando);
        Assert.Contains("no hay planes", resultado.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // Producto personalizado
    // =====================================================================

    [Fact]
    public async Task ProductoPersonalizado_FuncionaAunqueGlobalNoTengaPlanes()
    {
        await SeedConfigGlobalPago(); // sin planes globales
        var c = await SeedProducto("ML4-C", (1, 2.5m), (6, 8m));

        var planes = await Resolver(c);

        Assert.True(planes.EsValido);
        Assert.Equal(new[] { 1, 6 }, Cantidades(planes));
    }

    [Fact]
    public async Task ProductoDependienteDeGlobal_JuntoAPersonalizado_SinGlobal_Rechaza()
    {
        // C tiene plan propio; B depende de la global. Sin planes globales, B no es financiable y
        // la venta completa se rechaza (no se "salva" con los planes de C).
        await SeedConfigGlobalPago();
        var b = await SeedProducto("ML4-B");
        var c = await SeedProducto("ML4-C", (1, 2.5m), (6, 8m));

        var planes = await Resolver(b, c);

        Assert.False(planes.EsValido);
        Assert.Equal(MotivoSinPlanesCredito.SinPlanesGlobalesActivos, planes.Motivo);
    }

    // =====================================================================
    // Tasas (herencia vs 0 % explícito)
    // =====================================================================

    [Fact]
    public async Task PlanConTasaCeroExplicita_UsaCeroNoHeredaGlobal()
    {
        await SeedConfigGlobalPago(); // tasa única global 10 %
        await SeedGlobal((6, 0m));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.Equal(0m, planes.BuscarPlan(6)!.TasaMensual);
    }

    // =====================================================================
    // ML2.1 — contrato congelado (cerrado): el porcentaje del plan es la ÚNICA autoridad. Un
    // plan activo sin porcentaje explícito es una configuración inválida (null) y NUNCA hereda
    // la tasa única global — reemplaza al contrato legacy "PlanConTasaNull_HeredaLaTasaGlobal",
    // que validaba exactamente lo contrario. No pueden coexistir ambos contratos como válidos.
    // =====================================================================

    [Fact]
    public async Task PlanConTasaNull_EsConfiguracionInvalida_NoDebeHeredarLaTasaGlobal()
    {
        await SeedConfigGlobalPago(); // tasa única global 10 %
        await SeedGlobal((6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        // Contrato congelado: un plan activo sin porcentaje propio no es "10 %" (el de la
        // global) — no tiene autoridad para resolver nada; es null (configuración inválida).
        Assert.Null(planes.BuscarPlan(6)!.TasaMensual);
    }

    [Fact]
    public async Task TasaUnicaGlobalCero_PlanExplicitoEnCeroSigueDistinguibleDeNoConfigurado()
    {
        // Reformulado (ML1): la versión anterior de este test sembraba el plan con TasaMensual
        // null y afirmaba que heredar el 0 % único global era el comportamiento correcto — eso es
        // exactamente el contrato de herencia que ML1 da de baja. Se conserva la invariante I8 que
        // sí sigue vigente (0 % explícito ≠ no configurado), pero ahora con el plan declarando su
        // propio 0 %, sin pasar por la global.
        var config = new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "Crédito personal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = 0m // recargo único global explícitamente en 0 %
        };
        _context.ConfiguracionesPago.Add(config);
        await _context.SaveChangesAsync();

        await SeedGlobal((5, 0m)); // 0 % PROPIO del plan, no null: no depende de la herencia
        var producto = await SeedProducto("ML5-A");

        var planes = await Resolver(producto);

        Assert.True(planes.EsValido);
        Assert.Equal(0m, planes.BuscarPlan(5)!.TasaMensual);
    }

    [Fact]
    public async Task PlanInexistente_NoSeConfundeConPlanDeCeroPorciento()
    {
        await SeedConfigGlobalPago(); // tasa única global 10 %, no se usa: la cuota 3 fija 0 explícito
        await SeedGlobal((3, 0m));
        var producto = await SeedProducto("ML5-B");

        var planes = await Resolver(producto);

        // Cantidad configurada con 0 % explícito: existe y resuelve a 0, no a la global.
        Assert.NotNull(planes.BuscarPlan(3));
        Assert.Equal(0m, planes.BuscarPlan(3)!.TasaMensual);

        // Cantidad nunca configurada: no aparece en absoluto (no es "0 %", es inexistente).
        Assert.Null(planes.BuscarPlan(9));
        Assert.DoesNotContain(9, Cantidades(planes));
    }

    // ML2.1 — corrige la expectativa pre-ML2.1 de este test (asumía que la tasa propia del
    // producto, 12 % legacy, era la autoridad cuando no hay plan global para esa cantidad). Bajo
    // el contrato congelado (Fase 2) el plan GLOBAL de cuotas es la única autoridad del
    // porcentaje: sin un plan global para 6 cuotas no hay porcentaje válido, ni siquiera cuando
    // el producto declara uno propio. Tampoco es el wrapper legacy (10 % único global): ese dejó
    // de ser fallback del porcentaje.
    [Fact]
    public async Task SinPlanGlobalParaLaCantidad_ProductoConTasaLegacyNoEsAutoridad_EsInvalido()
    {
        await SeedConfigGlobalPago(); // wrapper legacy = 10 % único global, no se usa
        var producto = await SeedProducto("ML5-C", (6, 12m)); // tasa legacy propia del producto

        var tasaDirecta = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
        var planes = await Resolver(producto);

        Assert.NotEqual(tasaDirecta, planes.BuscarPlan(6)!.TasaMensual);
        Assert.NotEqual(12m, planes.BuscarPlan(6)!.TasaMensual);
        Assert.Null(planes.BuscarPlan(6)!.TasaMensual);
    }

    // =====================================================================
    // ML2.1 — T4 (cerrado): Producto no es autoridad del porcentaje. El plan (identificado por
    // su cantidad de cuotas) tiene un único porcentaje canónico; que un producto declare una tasa
    // "legacy" propia y distinta para la misma cantidad de cuotas no cambia el porcentaje
    // resuelto para ese plan: ResolverPlanesCreditoPersonalAsync usa siempre la cuota global
    // cuando existe, nunca la tasa propia de ProductoCreditoPersonalCuota.
    // =====================================================================

    [Fact]
    public async Task DosProductosConTasasLegacyDistintasParaElMismoPlan_DebenResolverElMismoPorcentaje()
    {
        await SeedConfigGlobalPago();
        await SeedGlobal((6, 8m)); // porcentaje canónico del plan "6 cuotas": 8 %
        var productoA = await SeedProducto("ML1-T4-A", (6, 12m)); // tasa legacy propia distinta
        var productoB = await SeedProducto("ML1-T4-B", (6, 3m));  // tasa legacy propia distinta

        var planesA = await Resolver(productoA);
        var planesB = await Resolver(productoB);

        // Contrato nuevo: mismo plan (6 cuotas) ⇒ mismo porcentaje (8 %), sin importar el producto.
        Assert.Equal(8m, planesA.BuscarPlan(6)!.TasaMensual);
        Assert.Equal(8m, planesB.BuscarPlan(6)!.TasaMensual);
        Assert.Equal(planesA.BuscarPlan(6)!.TasaMensual, planesB.BuscarPlan(6)!.TasaMensual);
    }

    // =====================================================================
    // Neutralización de las columnas legacy
    // =====================================================================

    [Fact]
    public async Task AlterarRangoLegacy_NoCambiaLasCuotasEfectivas()
    {
        // Con planes {1,4,6}, poner un rango legacy 2..3 NO recorta ni cambia el resultado.
        await SeedConfigGlobalPago(minLegacy: 2, maxLegacy: 3);
        await SeedGlobal((1, 0m), (4, 4m), (6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.Equal(new[] { 1, 4, 6 }, Cantidades(planes));
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

    private async Task<Producto> SeedProducto(string codigo, params (int Cuotas, decimal? Tasa)[] planes)
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
        {
            _context.ProductoCreditoPersonalCuotas.Add(new ProductoCreditoPersonalCuota
            {
                ProductoId = producto.Id,
                CantidadCuotas = cuotas,
                TasaMensual = tasa,
                Activo = true
            });
        }
        await _context.SaveChangesAsync();

        return producto;
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

    private async Task SeedGlobalInactivo(int cuotas, decimal? tasa)
    {
        _context.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
        {
            CantidadCuotas = cuotas,
            TasaMensual = tasa,
            Activo = false
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedConfigGlobalPago(int minLegacy = 1, int maxLegacy = 12)
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "Crédito personal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = TasaGlobalUnica,
            // Columnas legacy inertes: se seedean con valores explícitos para probar que NO afectan.
            MinCuotasDefaultCreditoPersonal = minLegacy,
            MaxCuotasDefaultCreditoPersonal = maxLegacy
        });
        await _context.SaveChangesAsync();
    }
}
