using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Modules.MercadoLibre.ViewModels;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase B: precio de canal (ajuste/redondeo/margen) y push de
/// stock/precio ERP→ML con modo simulación y PUTs no destructivos.
/// </summary>
public class MercadoLibreSyncServiceTests
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult("token-test");
    }

    private sealed class FakePrecioVigenteResolver : IPrecioVigenteResolver
    {
        public Dictionary<int, (decimal Precio, decimal Costo)> Precios { get; } = new();

        public async Task<PrecioVigenteResultado?> ResolverAsync(int productoId, int? listaId = null, DateTime? fecha = null)
            => (await ResolverBatchAsync(new[] { productoId }, listaId, fecha)).GetValueOrDefault(productoId);

        public Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
            IEnumerable<int> productoIds, int? listaId = null, DateTime? fecha = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, PrecioVigenteResultado> resultado = productoIds
                .Where(id => Precios.ContainsKey(id))
                .ToDictionary(id => id, id => new PrecioVigenteResultado
                {
                    ProductoId = id,
                    PrecioFinalConIva = Precios[id].Precio,
                    CostoSnapshot = Precios[id].Costo,
                    PrecioBaseProducto = Precios[id].Precio
                });

            return Task.FromResult(resultado);
        }
    }

    private static (MercadoLibreSyncService Sync, MercadoLibrePricingService Pricing,
        FakeMercadoLibreApiClient Api, FakePrecioVigenteResolver Resolver, TestDbContextFactory Factory)
        BuildServicios()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var api = new FakeMercadoLibreApiClient();
        var resolver = new FakePrecioVigenteResolver();

        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var pricing = new MercadoLibrePricingService(configService, resolver);

        var sync = new MercadoLibreSyncService(
            factory, api, new FakeAuthService(), configService, pricing,
            NullLogger<MercadoLibreSyncService>.Instance);

        return (sync, pricing, api, resolver, factory);
    }

    private static async Task ConfigurarAsync(
        TestDbContextFactory factory, Action<MercadoLibreConfiguracion> ajustar)
    {
        await using var ctx = factory.CreateDbContext();

        var config = await ctx.MercadoLibreConfiguraciones.FirstOrDefaultAsync();
        if (config is null)
        {
            config = new MercadoLibreConfiguracion();
            ctx.MercadoLibreConfiguraciones.Add(config);
        }

        ajustar(config);
        await ctx.SaveChangesAsync();
    }

    private static async Task<(int AccountId, int ListingId, int ProductoId)> SembrarListingVinculadoAsync(
        TestDbContextFactory factory,
        decimal precioMl = 1000m,
        int stockMl = 5,
        decimal stockErp = 8m,
        params long[] variaciones)
    {
        await using var ctx = factory.CreateDbContext();

        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
            Nickname = "TEST",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };
        ctx.MercadoLibreAccounts.Add(cuenta);

        var marca = new Marca { Codigo = $"M-{Guid.NewGuid():N}", Nombre = "Marca", Activo = true };
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"P-{Guid.NewGuid():N}"[..10],
            Nombre = "Producto sync",
            CategoriaId = 1,
            MarcaId = marca.Id,
            PrecioCompra = 500m,
            PrecioVenta = 1100m,
            StockActual = stockErp,
            Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var listing = new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = $"MLA{Random.Shared.Next(100000, 999999)}",
            Titulo = "Listing sync",
            Precio = precioMl,
            AvailableQuantity = stockMl,
            Status = "active",
            TieneVariaciones = variaciones.Length > 0,
            ProductoId = producto.Id
        };

        foreach (var variationId in variaciones)
        {
            listing.Variaciones.Add(new MercadoLibreListingVariation
            {
                VariationId = variationId,
                Precio = precioMl,
                AvailableQuantity = stockMl
            });
        }

        ctx.MercadoLibreListings.Add(listing);
        await ctx.SaveChangesAsync();

        return (cuenta.Id, listing.Id, producto.Id);
    }

    // -----------------------------------------------------------------------
    // Precio de canal (redondeo / ajuste / margen)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ninguno", 1234.56, 1234.56)]
    [InlineData("decena", 1234.56, 1230)]
    [InlineData("centena", 1234.56, 1200)]
    [InlineData("mil", 1234.56, 1000)]
    [InlineData("mil", 800, 800)] // nunca redondear a 0
    public void Redondear_AplicaReglas(string regla, decimal entrada, decimal esperado)
    {
        var (_, pricing, _, _, _) = BuildServicios();

        Assert.Equal(esperado, pricing.Redondear(entrada, regla));
    }

    [Fact]
    public async Task CalcularPrecioCanal_AplicaAjusteYRedondeo()
    {
        var (_, pricing, _, resolver, factory) = BuildServicios();

        await ConfigurarAsync(factory, c =>
        {
            c.AjusteCanalPorcentaje = 10m;
            c.ReglaRedondeo = "centena";
        });

        resolver.Precios[42] = (Precio: 1000m, Costo: 600m);

        var resultado = await pricing.CalcularPrecioCanalAsync(new[] { 42 });

        // 1000 * 1.10 = 1100 → centena = 1100
        Assert.Equal(1100m, resultado[42].PrecioCanal);
        Assert.Equal(1000m, resultado[42].PrecioErp);
        Assert.False(resultado[42].DebajoDelMargenMinimo);
    }

    [Fact]
    public async Task CalcularPrecioCanal_DetectaMargenDebajoDelMinimo()
    {
        var (_, pricing, _, resolver, factory) = BuildServicios();

        await ConfigurarAsync(factory, c =>
        {
            c.AjusteCanalPorcentaje = 0m;
            c.MargenMinimoPorcentaje = 50m;
        });

        // Margen real: (1000-800)/800 = 25% < 50%
        resolver.Precios[42] = (Precio: 1000m, Costo: 800m);

        var resultado = await pricing.CalcularPrecioCanalAsync(new[] { 42 });

        Assert.True(resultado[42].DebajoDelMargenMinimo);
        Assert.Equal(25m, resultado[42].MargenResultantePorcentaje);
    }

    // -----------------------------------------------------------------------
    // Preview
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Preview_ListingSinVincular_QuedaExcluida()
    {
        var (sync, _, _, _, factory) = BuildServicios();

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = new MercadoLibreAccount
            {
                MeliUserId = 1, Nickname = "T", AccessTokenEncrypted = "x", RefreshTokenEncrypted = "x", Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();

            var listing = new MercadoLibreListing
            {
                AccountId = cuenta.Id, ItemId = "MLA1", Titulo = "Sin vincular", Status = "active"
            };
            ctx.MercadoLibreListings.Add(listing);
            await ctx.SaveChangesAsync();
            listingId = listing.Id;
        }

        var preview = await sync.PrepararPreviewAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio);

        var item = Assert.Single(preview.Items);
        Assert.True(item.Excluida);
        Assert.Contains("Sin producto vinculado", item.MotivoExclusion);
        Assert.Equal(0, preview.TotalConCambios);
    }

    [Fact]
    public async Task Preview_MultiVariacion_MuestraFilaPorVariationIdYBloqueaStockSinVinculoPropio()
    {
        var (sync, _, _, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(
            factory, precioMl: 1000m, stockMl: 5, stockErp: 8m, variaciones: new long[] { 11, 22 });

        resolver.Precios[productoId] = (Precio: 1500m, Costo: 700m);

        var preview = await sync.PrepararPreviewAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio);

        Assert.Equal(2, preview.Items.Count);
        Assert.All(preview.Items, item =>
        {
            Assert.True(item.EsVariacion);
            Assert.True(item.Excluida);
            Assert.Contains("requiere vinculación/origen", item.MotivoExclusion);
            Assert.Equal(1500m, item.PrecioObjetivo);
            Assert.True(item.CambiaPrecio);
        });
        Assert.Equal(new[] { 11L, 22L }, preview.Items.Select(i => i.VariationId!.Value).OrderBy(id => id));
    }

    [Fact]
    public async Task Preview_UnaVariacion_UsaVariationIdYFallbackSeguroAlProductoDePublicacion()
    {
        var (sync, _, _, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(
            factory, precioMl: 1000m, stockMl: 5, stockErp: 3m, variaciones: new long[] { 196332252298 });

        resolver.Precios[productoId] = (Precio: 1800m, Costo: 900m);

        var preview = await sync.PrepararPreviewAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio);

        var item = Assert.Single(preview.Items);
        Assert.False(item.Excluida);
        Assert.Equal(196332252298, item.VariationId);
        Assert.Equal(5, item.StockMl);
        Assert.Equal(3, item.StockObjetivo);
        Assert.Equal(1000m, item.PrecioMl);
        Assert.Equal(1800m, item.PrecioObjetivo);
    }

    // -----------------------------------------------------------------------
    // Aplicar — simulación
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Aplicar_ConModoSimulacion_NoLlamaApiYRegistraSyncLog()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(factory);
        resolver.Precios[productoId] = (Precio: 2000m, Costo: 900m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = true);

        var resultado = await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio, confirmarReal: true, usuario: "tester");

        Assert.True(resultado.FueSimulado);
        Assert.Equal(1, resultado.Exitosos);
        Assert.Empty(api.UpdateItemCalls); // NUNCA llamó a la API

        await using var ctx = factory.CreateDbContext();

        var log = await ctx.MercadoLibreSyncLogs.SingleAsync(l => l.Operacion == "PushStockPrecio");
        Assert.True(log.Exito);
        Assert.StartsWith("SIMULADO", log.Detalle);

        // El listing local NO se modifica en simulación.
        var listing = await ctx.MercadoLibreListings.SingleAsync(l => l.Id == listingId);
        Assert.Equal(1000m, listing.Precio);
        Assert.Equal(5, listing.AvailableQuantity);
    }

    [Fact]
    public async Task Aplicar_ConModoSimulacion_YVariacion_NoLlamaApiYRegistraPayloadSeguro()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(
            factory, precioMl: 1000m, stockMl: 5, stockErp: 2m, variaciones: new long[] { 777 });
        resolver.Precios[productoId] = (Precio: 1800m, Costo: 900m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = true);

        var resultado = await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio, confirmarReal: true, usuario: "tester");

        Assert.True(resultado.FueSimulado);
        Assert.Equal(1, resultado.Exitosos);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Empty(api.UpdateItemVariationCalls);

        await using var ctx = factory.CreateDbContext();
        var log = await ctx.MercadoLibreSyncLogs.SingleAsync(l => l.Operacion == "PushStockPrecio");
        Assert.Contains("SIMULADO", log.Detalle);
        Assert.Contains("Payload", log.Detalle);
        Assert.Contains("available_quantity", log.Detalle);
        Assert.DoesNotContain("variations", log.Detalle);
    }

    [Fact]
    public async Task Aplicar_SinConfirmarReal_EsSimuladoAunqueSimulacionEsteApagada()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(factory);
        resolver.Precios[productoId] = (Precio: 2000m, Costo: 900m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var resultado = await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.Precio, confirmarReal: false, usuario: "tester");

        Assert.True(resultado.FueSimulado);
        Assert.Empty(api.UpdateItemCalls);
    }

    // -----------------------------------------------------------------------
    // Aplicar — real
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Aplicar_Real_SinVariaciones_MandaPayloadMinimoYActualizaLocal()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(
            factory, precioMl: 1000m, stockMl: 5, stockErp: 8m);

        resolver.Precios[productoId] = (Precio: 2000m, Costo: 900m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var resultado = await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio, confirmarReal: true, usuario: "tester");

        Assert.False(resultado.FueSimulado);
        Assert.Equal(1, resultado.Exitosos);

        var (itemId, payloadObj) = Assert.Single(api.UpdateItemCalls);
        var payload = Assert.IsType<Dictionary<string, object>>(payloadObj);

        Assert.Equal(8, payload["available_quantity"]);
        Assert.Equal(2000m, payload["price"]);
        Assert.False(payload.ContainsKey("variations")); // sin variaciones: payload plano

        await using var ctx = factory.CreateDbContext();
        var listing = await ctx.MercadoLibreListings.SingleAsync(l => l.Id == listingId);

        Assert.Equal(2000m, listing.Precio);
        Assert.Equal(8, listing.AvailableQuantity);
        Assert.NotNull(listing.LastSyncUtc);

        var log = await ctx.MercadoLibreSyncLogs.SingleAsync(l => l.Operacion == "PushStockPrecio");
        Assert.True(log.Exito);
        Assert.DoesNotContain("SIMULADO", log.Detalle);
    }

    [Fact]
    public async Task Aplicar_Real_ConUnaVariacion_MandaSoloIdYCampos()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(
            factory, precioMl: 1000m, stockMl: 5, stockErp: 3m, variaciones: new long[] { 777 });

        resolver.Precios[productoId] = (Precio: 1800m, Costo: 900m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.StockYPrecio, confirmarReal: true, usuario: "tester");

        Assert.Empty(api.UpdateItemCalls);
        var (itemId, variationId, payloadObj) = Assert.Single(api.UpdateItemVariationCalls);
        Assert.Equal(777L, variationId);

        var payload = Assert.IsType<Dictionary<string, object>>(payloadObj);

        Assert.NotEmpty(itemId);
        Assert.Equal(3, payload["available_quantity"]);
        Assert.Equal(1800m, payload["price"]);
        Assert.False(payload.ContainsKey("id"));
        Assert.False(payload.ContainsKey("variations"));
        Assert.False(payload.ContainsKey("attribute_combinations"));
    }

    [Fact]
    public async Task Aplicar_Real_VariacionSinStockResoluble_QuedaOmitidaYNoLlamaApi()
    {
        var (sync, _, api, _, factory) = BuildServicios();

        int listingId;
        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = new MercadoLibreAccount
            {
                MeliUserId = 99,
                Nickname = "TEST",
                AccessTokenEncrypted = "x",
                RefreshTokenEncrypted = "x",
                Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();

            var listing = new MercadoLibreListing
            {
                AccountId = cuenta.Id,
                ItemId = "MLA-SIN-STOCK",
                Titulo = "Sin stock resoluble",
                Precio = 1000m,
                AvailableQuantity = 5,
                Status = "active",
                TieneVariaciones = true
            };
            listing.Variaciones.Add(new MercadoLibreListingVariation
            {
                VariationId = 888,
                Precio = 1000m,
                AvailableQuantity = 5
            });
            ctx.MercadoLibreListings.Add(listing);
            await ctx.SaveChangesAsync();
            listingId = listing.Id;
        }

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var resultado = await sync.AplicarAsync(
            new[] { listingId }, MercadoLibreSyncTipo.Stock, confirmarReal: true, usuario: "tester");

        Assert.False(resultado.FueSimulado);
        Assert.Equal(0, resultado.Exitosos);
        Assert.Equal(1, resultado.Omitidos);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Empty(api.UpdateItemVariationCalls);
        Assert.Contains(resultado.Mensajes, m => m.Contains("requiere vinculación/origen"));
    }

    [Fact]
    public async Task Aplicar_Real_ErrorDeApi_RegistraLogYContinua()
    {
        var (sync, _, api, resolver, factory) = BuildServicios();

        var (_, listingId1, productoId1) = await SembrarListingVinculadoAsync(factory, precioMl: 100m);
        var (_, listingId2, productoId2) = await SembrarListingVinculadoAsync(factory, precioMl: 100m);

        resolver.Precios[productoId1] = (Precio: 500m, Costo: 100m);
        resolver.Precios[productoId2] = (Precio: 500m, Costo: 100m);

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        string itemIdQueFalla;
        await using (var ctx = factory.CreateDbContext())
        {
            itemIdQueFalla = (await ctx.MercadoLibreListings.SingleAsync(l => l.Id == listingId1)).ItemId;
        }
        api.UpdateItemFallan.Add(itemIdQueFalla);

        var resultado = await sync.AplicarAsync(
            new[] { listingId1, listingId2 }, MercadoLibreSyncTipo.Precio, confirmarReal: true, usuario: "tester");

        Assert.Equal(1, resultado.Exitosos);
        Assert.Equal(1, resultado.Fallidos);

        await using var ctx2 = factory.CreateDbContext();
        var logs = await ctx2.MercadoLibreSyncLogs.Where(l => l.Operacion == "PushPrecio").ToListAsync();

        Assert.Equal(2, logs.Count);
        Assert.Single(logs, l => !l.Exito);
        Assert.Single(logs, l => l.Exito);
    }

    // -----------------------------------------------------------------------
    // Origen de stock (Checkpoint 4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Preview_OrigenStockFisico_PublicaSoloUnidadesEnStock()
    {
        var (sync, _, _, _, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(factory, stockMl: 5, stockErp: 8m);

        await ConfigurarAsync(factory, c => c.OrigenStock = MercadoLibreOrigenStock.StockFisicoDisponible);

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.ProductoUnidades.AddRange(
                new ProductoUnidad { ProductoId = productoId, CodigoInternoUnidad = "U-1", Estado = EstadoUnidad.EnStock },
                new ProductoUnidad { ProductoId = productoId, CodigoInternoUnidad = "U-2", Estado = EstadoUnidad.EnStock },
                new ProductoUnidad { ProductoId = productoId, CodigoInternoUnidad = "U-3", Estado = EstadoUnidad.Vendida });
            await ctx.SaveChangesAsync();
        }

        var preview = await sync.PrepararPreviewAsync(new[] { listingId }, MercadoLibreSyncTipo.Stock);

        var item = Assert.Single(preview.Items);
        Assert.Equal(2, item.StockObjetivo); // 2 EnStock, NO el stock lógico (8)
        Assert.Equal(MercadoLibreOrigenStock.StockFisicoDisponible, item.OrigenStock);
    }

    [Fact]
    public async Task Preview_OrigenUnidadEspecifica_PublicaUnoOCero()
    {
        var (sync, _, _, _, factory) = BuildServicios();

        var (_, listingId, productoId) = await SembrarListingVinculadoAsync(factory, stockMl: 5, stockErp: 8m);

        int unidadId;
        await using (var ctx = factory.CreateDbContext())
        {
            var unidad = new ProductoUnidad { ProductoId = productoId, CodigoInternoUnidad = "U-ESP", Estado = EstadoUnidad.EnStock };
            ctx.ProductoUnidades.Add(unidad);
            await ctx.SaveChangesAsync();
            unidadId = unidad.Id;

            var listing = await ctx.MercadoLibreListings.SingleAsync(l => l.Id == listingId);
            listing.OrigenStockOverride = MercadoLibreOrigenStock.UnidadFisicaEspecifica;
            listing.ProductoUnidadId = unidadId;
            await ctx.SaveChangesAsync();
        }

        var preview = await sync.PrepararPreviewAsync(new[] { listingId }, MercadoLibreSyncTipo.Stock);
        Assert.Equal(1, Assert.Single(preview.Items).StockObjetivo);

        // La unidad se vende → el stock publicable pasa a 0 con advertencia.
        await using (var ctx = factory.CreateDbContext())
        {
            (await ctx.ProductoUnidades.SingleAsync(u => u.Id == unidadId)).Estado = EstadoUnidad.Vendida;
            await ctx.SaveChangesAsync();
        }

        var preview2 = await sync.PrepararPreviewAsync(new[] { listingId }, MercadoLibreSyncTipo.Stock);
        var item2 = Assert.Single(preview2.Items);
        Assert.Equal(0, item2.StockObjetivo);
        Assert.Contains(item2.Advertencias, a => a.Contains("no está disponible"));
    }

    [Fact]
    public async Task Configuracion_OrigenesInvalidosComoGlobal_SonRechazados()
    {
        var (_, _, _, _, factory) = BuildServicios();

        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => configService.GuardarAsync(
            new MercadoLibreConfiguracionViewModel { OrigenStock = MercadoLibreOrigenStock.DepositoSucursal }, "tester"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => configService.GuardarAsync(
            new MercadoLibreConfiguracionViewModel { OrigenStock = MercadoLibreOrigenStock.UnidadFisicaEspecifica }, "tester"));
    }
}
