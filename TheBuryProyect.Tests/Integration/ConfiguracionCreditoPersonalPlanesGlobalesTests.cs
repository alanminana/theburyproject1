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

    [Fact]
    public async Task PlanConTasaNull_HeredaLaTasaGlobal()
    {
        await SeedConfigGlobalPago(); // tasa única global 10 %
        await SeedGlobal((6, null));
        var producto = await SeedProducto("ML4-A");

        var planes = await Resolver(producto);

        Assert.Equal(TasaGlobalUnica, planes.BuscarPlan(6)!.TasaMensual);
    }

    // =====================================================================
    // Micro-lote 5: distinción explícita entre recargo único global en 0 %, plan de cuota
    // en 0 % explícito y cantidad de cuotas inexistente. Ninguno de los tres debe
    // confundirse entre sí (I8: 0 % ya no se interpreta como "no configurado").
    // =====================================================================

    [Fact]
    public async Task TasaUnicaGlobalCero_SeHeredaComoCeroNoComoNoConfigurada()
    {
        var config = new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "Crédito personal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = 0m // recargo único global explícitamente en 0 %
        };
        _context.ConfiguracionesPago.Add(config);
        await _context.SaveChangesAsync();

        await SeedGlobal((5, null)); // hereda la única global
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

    [Fact]
    public async Task WrapperLegacyObtenerTasa_CoincideConElRecargoQueHeredaElPlan()
    {
        // El "wrapper legacy" (ObtenerTasaInteresMensualCreditoPersonalAsync) y la resolución
        // de planes por cantidad de cuotas deben devolver siempre el mismo recargo canónico.
        await SeedConfigGlobalPago();
        await SeedGlobal((6, null));
        var producto = await SeedProducto("ML5-C");

        var tasaDirecta = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
        var planes = await Resolver(producto);

        Assert.Equal(tasaDirecta, planes.BuscarPlan(6)!.TasaMensual);
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
