using AutoMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Mapping;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase 2: importación idempotente de publicaciones (con variaciones)
/// y vinculación explícita con Producto sin modificarlo.
/// </summary>
public class MercadoLibreListingServiceTests
{
    private static (MercadoLibreListingService Servicio, FakeMercadoLibreApiClient Api, TestDbContextFactory Factory)
        BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var dataProtection = new EphemeralDataProtectionProvider();
        var protector = new MercadoLibreTokenProtector(dataProtection);
        var api = new FakeMercadoLibreApiClient();

        var auth = new MercadoLibreAuthService(
            factory,
            api,
            protector,
            dataProtection,
            Microsoft.Extensions.Options.Options.Create(new MercadoLibreOptions
            {
                ClientId = "cid",
                ClientSecret = "cs",
                RedirectUri = "https://test/cb"
            }),
            NullLogger<MercadoLibreAuthService>.Instance);

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MercadoLibreMappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        var servicio = new MercadoLibreListingService(
            factory, auth, api,
            MercadoLibreTestProductoService.Crear(factory.CreateDbContext()),
            mapper, NullLogger<MercadoLibreListingService>.Instance);

        // Cuenta conectada con token vigente
        using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreAccounts.Add(new MercadoLibreAccount
        {
            MeliUserId = 777,
            Nickname = "VENDEDOR",
            AccessTokenEncrypted = protector.Protect("access-ok"),
            RefreshTokenEncrypted = protector.Protect("refresh-ok"),
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(5),
            Activa = true
        });
        ctx.SaveChanges();

        return (servicio, api, factory);
    }

    private static int CuentaId(TestDbContextFactory factory)
    {
        using var ctx = factory.CreateDbContext();
        return ctx.MercadoLibreAccounts.Single().Id;
    }

    private static MeliItemDto Item(string id, string titulo, decimal precio, int stock, string? sku = null) => new()
    {
        Id = id,
        Title = titulo,
        Price = precio,
        CurrencyId = "ARS",
        AvailableQuantity = stock,
        SoldQuantity = 2,
        Status = "active",
        Permalink = $"https://articulo.mercadolibre.com.ar/{id}",
        CategoryId = "MLA1055",
        ListingTypeId = "gold_special",
        Condition = "new",
        SellerCustomField = sku
    };

    private static async Task<int> CrearProductoAsync(TestDbContextFactory factory, string codigo)
    {
        await using var ctx = factory.CreateDbContext();

        var marca = new Marca { Codigo = $"M-{codigo}", Nombre = $"Marca {codigo}", Activo = true };
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = $"Producto {codigo}",
            CategoriaId = 1, // seed de AppDbContext
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 200m,
            StockActual = 10m,
            Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        return producto.Id;
    }

    // -----------------------------------------------------------------------
    // Importación
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Importar_CreaListingsConDatosYSyncLog()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100", "MLA200" } };
        api.Items = new List<MeliItemDto>
        {
            Item("MLA100", "Heladera Test", 999999.99m, 5, sku: "HEL-001"),
            Item("MLA200", "Notebook Test", 555555m, 3)
        };

        var resultado = await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        Assert.Equal(2, resultado.TotalEncontradas);
        Assert.Equal(2, resultado.Creadas);
        Assert.Equal(0, resultado.Actualizadas);
        Assert.Equal(0, resultado.Errores);

        await using var ctx = factory.CreateDbContext();
        var listing = await ctx.MercadoLibreListings.SingleAsync(l => l.ItemId == "MLA100");

        Assert.Equal("Heladera Test", listing.Titulo);
        Assert.Equal(999999.99m, listing.Precio);
        Assert.Equal(5, listing.AvailableQuantity);
        Assert.Equal("active", listing.Status);
        Assert.Equal("HEL-001", listing.SellerSku);
        Assert.Equal("MLA1055", listing.CategoryId);
        Assert.Equal("gold_special", listing.ListingTypeId);
        Assert.False(listing.TieneVariaciones);
        Assert.NotNull(listing.RawJson);
        Assert.Null(listing.ProductoId); // nunca se vincula automáticamente

        var log = await ctx.MercadoLibreSyncLogs.SingleAsync(l => l.Operacion == "ImportListings");
        Assert.True(log.Exito);

        var cuenta = await ctx.MercadoLibreAccounts.SingleAsync();
        Assert.NotNull(cuenta.UltimaImportacionListingsUtc);
    }

    [Fact]
    public async Task Importar_DosVeces_EsIdempotente()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100" } };
        api.Items = new List<MeliItemDto> { Item("MLA100", "Original", 100m, 1) };

        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        // Segunda corrida con precio/stock cambiados en ML
        api.Items = new List<MeliItemDto> { Item("MLA100", "Original", 150m, 7) };
        var resultado = await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        Assert.Equal(0, resultado.Creadas);
        Assert.Equal(1, resultado.Actualizadas);

        await using var ctx = factory.CreateDbContext();
        var listing = await ctx.MercadoLibreListings.SingleAsync();
        Assert.Equal(150m, listing.Precio);
        Assert.Equal(7, listing.AvailableQuantity);
    }

    [Fact]
    public async Task Importar_ConVariaciones_PersisteVariacionesYLasActualiza()
    {
        var (servicio, api, factory) = BuildServicio();

        var item = Item("MLA300", "Zapatilla", 50000m, 10);
        item.Variations = new List<MeliVariationDto>
        {
            new() { Id = 11, Price = 50000m, AvailableQuantity = 4, SellerCustomField = "ZAP-NEGRA" },
            new() { Id = 22, Price = 50000m, AvailableQuantity = 6 }
        };

        api.PaginasScan = new List<List<string>> { new() { "MLA300" } };
        api.Items = new List<MeliItemDto> { item };

        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        await using (var ctx = factory.CreateDbContext())
        {
            var listing = await ctx.MercadoLibreListings
                .Include(l => l.Variaciones)
                .SingleAsync();

            Assert.True(listing.TieneVariaciones);
            Assert.Equal(2, listing.Variaciones.Count);
            Assert.Equal("ZAP-NEGRA", listing.Variaciones.Single(v => v.VariationId == 11).SellerSku);
        }

        // Segunda corrida: la variación 22 desaparece en ML → soft delete, nunca borrado físico
        item.Variations.RemoveAt(1);
        item.Variations[0].AvailableQuantity = 9;
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        await using (var ctx = factory.CreateDbContext())
        {
            var variaciones = await ctx.MercadoLibreListingVariations
                .IgnoreQueryFilters()
                .ToListAsync();

            Assert.Equal(2, variaciones.Count);
            Assert.Equal(9, variaciones.Single(v => v.VariationId == 11).AvailableQuantity);
            Assert.False(variaciones.Single(v => v.VariationId == 11).IsDeleted);
            Assert.True(variaciones.Single(v => v.VariationId == 22).IsDeleted);
        }
    }

    [Fact]
    public async Task Importar_FallaDeApi_RegistraSyncLogDeError()
    {
        var (servicio, api, factory) = BuildServicio();

        // El fake lanza si no hay páginas configuradas con Usuario nulo… forzamos error
        // haciendo que GetItems lance: páginas con ids pero sin items configurados es válido,
        // así que invalidamos el token para que el refresh falle.
        api.PaginasScan = new List<List<string>> { new() { "MLA1" } };
        api.Items = new List<MeliItemDto>();

        // Cuenta con token vencido y sin TokenParaRefresh → el fake lanza InvalidOperationException
        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = await ctx.MercadoLibreAccounts.SingleAsync();
            cuenta.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-30);
            await ctx.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.ImportarPublicacionesAsync(CuentaId(factory)));

        await using var ctx2 = factory.CreateDbContext();
        var log = await ctx2.MercadoLibreSyncLogs.SingleAsync(l => l.Operacion == "ImportListings");
        Assert.False(log.Exito);
        Assert.NotNull(log.Detalle);
    }

    // -----------------------------------------------------------------------
    // Vinculación con Producto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Vincular_SeteaFkSinTocarElProducto()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100" } };
        api.Items = new List<MeliItemDto> { Item("MLA100", "Heladera", 100m, 1) };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        var productoId = await CrearProductoAsync(factory, "HEL-001");

        Producto productoAntes;
        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            productoAntes = await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId);
            listingId = (await ctx.MercadoLibreListings.SingleAsync()).Id;
        }

        await servicio.VincularProductoAsync(listingId, productoId);

        await using var ctx2 = factory.CreateDbContext();
        var listing = await ctx2.MercadoLibreListings.SingleAsync();
        Assert.Equal(productoId, listing.ProductoId);

        // El producto interno no cambió en nada
        var productoDespues = await ctx2.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId);
        Assert.Equal(productoAntes.PrecioVenta, productoDespues.PrecioVenta);
        Assert.Equal(productoAntes.StockActual, productoDespues.StockActual);
        Assert.Equal(productoAntes.Nombre, productoDespues.Nombre);
        Assert.Null(productoDespues.UpdatedAt);

        Assert.True(await ctx2.MercadoLibreSyncLogs.AnyAsync(l => l.Operacion == "LinkProducto" && l.Exito));
    }

    [Fact]
    public async Task Vincular_ProductoInexistente_Lanza()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100" } };
        api.Items = new List<MeliItemDto> { Item("MLA100", "Heladera", 100m, 1) };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            listingId = (await ctx.MercadoLibreListings.SingleAsync()).Id;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.VincularProductoAsync(listingId, productoId: 99999));
    }

    [Fact]
    public async Task Desvincular_QuitaLaFk()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100" } };
        api.Items = new List<MeliItemDto> { Item("MLA100", "Heladera", 100m, 1) };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        var productoId = await CrearProductoAsync(factory, "HEL-002");

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            listingId = (await ctx.MercadoLibreListings.SingleAsync()).Id;
        }

        await servicio.VincularProductoAsync(listingId, productoId);
        await servicio.DesvincularProductoAsync(listingId);

        await using var ctx2 = factory.CreateDbContext();
        Assert.Null((await ctx2.MercadoLibreListings.SingleAsync()).ProductoId);
        Assert.True(await ctx2.MercadoLibreSyncLogs.AnyAsync(l => l.Operacion == "UnlinkProducto"));
    }

    // -----------------------------------------------------------------------
    // Listado con sugerencias
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetListings_SugiereProductoPorSkuSinVincular()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA100", "MLA200" } };
        api.Items = new List<MeliItemDto>
        {
            Item("MLA100", "Con SKU que matchea", 100m, 1, sku: "HEL-001"),
            Item("MLA200", "Sin SKU", 200m, 2)
        };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        var productoId = await CrearProductoAsync(factory, "HEL-001");

        var listings = await servicio.GetListingsAsync();

        var conSku = listings.Single(l => l.ItemId == "MLA100");
        Assert.Equal(productoId, conSku.ProductoSugeridoId);
        Assert.False(conSku.Vinculada); // sugerencia, nunca vinculación automática

        var sinSku = listings.Single(l => l.ItemId == "MLA200");
        Assert.Null(sinSku.ProductoSugeridoId);

        // Filtros
        Assert.Empty(await servicio.GetListingsAsync("vinculadas"));
        Assert.Equal(2, (await servicio.GetListingsAsync("sin-vincular")).Count);
    }

    // -----------------------------------------------------------------------
    // Checkpoint 2 — Crear producto interno desde publicación
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrearProductoDesdeListing_CreaVinculaYRegistraStockInicial()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA300" } };
        api.Items = new List<MeliItemDto> { Item("MLA300", "Lavarropas automático", 350000m, 4, sku: "LAV-300") };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            listingId = (await ctx.MercadoLibreListings.SingleAsync()).Id;
        }

        // Prefill desde la publicación.
        var prefill = await servicio.GetCrearProductoViewModelAsync(listingId);
        Assert.NotNull(prefill);
        Assert.Equal("LAV-300", prefill!.Codigo);
        Assert.Equal("Lavarropas automático", prefill.Nombre);
        Assert.Equal(350000m, prefill.PrecioVenta);
        Assert.Equal(4, prefill.StockInicial);

        int marcaId;
        await using (var ctx = factory.CreateDbContext())
        {
            var marca = new Marca { Codigo = $"M-{Guid.NewGuid():N}"[..10], Nombre = "Marca", Activo = true };
            ctx.Marcas.Add(marca);
            await ctx.SaveChangesAsync();
            marcaId = marca.Id;
        }

        prefill.CategoriaId = 1;
        prefill.MarcaId = marcaId;
        prefill.PrecioCompra = 200000m;
        prefill.StockInicial = 4m;

        var productoId = await servicio.CrearProductoDesdeListingAsync(prefill, "tester");

        await using var ctx2 = factory.CreateDbContext();

        var producto = await ctx2.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal("LAV-300", producto.Codigo);
        Assert.Equal(4m, producto.StockActual);

        // Stock inicial trazado en el kardex (alta canónica de ProductoService).
        var movimiento = await ctx2.MovimientosStock.SingleAsync(m => m.ProductoId == productoId);
        Assert.Equal(4m, movimiento.Cantidad);
        Assert.Equal("Stock inicial", movimiento.Referencia);

        // Vinculación automática + log. La publicación de ML no se tocó.
        Assert.Equal(productoId, (await ctx2.MercadoLibreListings.SingleAsync(l => l.Id == listingId)).ProductoId);
        Assert.True(await ctx2.MercadoLibreSyncLogs.AnyAsync(l => l.Operacion == "CrearProductoDesdeListing" && l.Exito));
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task CrearProductoDesdeListing_YaVinculada_Lanza()
    {
        var (servicio, api, factory) = BuildServicio();

        api.PaginasScan = new List<List<string>> { new() { "MLA301" } };
        api.Items = new List<MeliItemDto> { Item("MLA301", "Ya vinculada", 100m, 1) };
        await servicio.ImportarPublicacionesAsync(CuentaId(factory));

        var productoId = await CrearProductoAsync(factory, "PRE-001");

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            listingId = (await ctx.MercadoLibreListings.SingleAsync()).Id;
        }

        await servicio.VincularProductoAsync(listingId, productoId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.GetCrearProductoViewModelAsync(listingId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CrearProductoDesdeListingAsync(new MercadoLibreCrearProductoViewModel
            {
                ListingId = listingId,
                Codigo = "X",
                Nombre = "X",
                CategoriaId = 1,
                MarcaId = 1
            }, "tester"));
    }
}
