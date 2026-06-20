using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase I: aumentos masivos con preview obligatorio, snapshot del
/// precio anterior, aplicación con simulación/confirmación y rollback por lote.
/// </summary>
public class MercadoLibrePriceBatchServiceTests
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

    private static (MercadoLibrePriceBatchService Servicio, FakeMercadoLibreApiClient Api,
        FakePrecioVigenteResolver Resolver, TestDbContextFactory Factory)
        BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var api = new FakeMercadoLibreApiClient();
        var resolver = new FakePrecioVigenteResolver();

        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var servicio = new MercadoLibrePriceBatchService(
            factory, api, new FakeAuthService(), configService,
            new MercadoLibrePricingService(configService, resolver),
            NullLogger<MercadoLibrePriceBatchService>.Instance);

        return (servicio, api, resolver, factory);
    }

    private static async Task ConfigurarAsync(TestDbContextFactory factory, Action<MercadoLibreConfiguracion> ajustar)
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

    private static async Task<int> SembrarListingAsync(
        TestDbContextFactory factory, string itemId, decimal precio, string status = "active",
        int stock = 5, bool vincular = false, params long[] variaciones)
    {
        await using var ctx = factory.CreateDbContext();

        var cuenta = await ctx.MercadoLibreAccounts.FirstOrDefaultAsync();
        if (cuenta is null)
        {
            cuenta = new MercadoLibreAccount
            {
                MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
                Nickname = "T",
                AccessTokenEncrypted = "x",
                RefreshTokenEncrypted = "x",
                Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();
        }

        // Los cambios de precio REALES exigen producto vinculado (Checkpoint 1):
        // los tests de aplicación/rollback siembran la publicación ya vinculada.
        int? productoId = null;
        if (vincular)
        {
            var marca = new Marca { Codigo = $"M-{Guid.NewGuid():N}"[..10], Nombre = "Marca", Activo = true };
            ctx.Marcas.Add(marca);
            await ctx.SaveChangesAsync();

            var producto = new Producto
            {
                Codigo = $"P-{Guid.NewGuid():N}"[..10],
                Nombre = $"Producto {itemId}",
                CategoriaId = 1,
                MarcaId = marca.Id,
                PrecioCompra = 100m,
                PrecioVenta = precio,
                Activo = true
            };
            ctx.Productos.Add(producto);
            await ctx.SaveChangesAsync();
            productoId = producto.Id;
        }

        var listing = new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = itemId,
            Titulo = $"Listing {itemId}",
            Precio = precio,
            AvailableQuantity = stock,
            Status = status,
            TieneVariaciones = variaciones.Length > 0,
            ProductoId = productoId
        };

        foreach (var variationId in variaciones)
        {
            listing.Variaciones.Add(new MercadoLibreListingVariation
            {
                VariationId = variationId,
                Precio = precio,
                AvailableQuantity = stock
            });
        }

        ctx.MercadoLibreListings.Add(listing);
        await ctx.SaveChangesAsync();

        return listing.Id;
    }

    // -----------------------------------------------------------------------
    // Simulación / snapshot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Simular_CreaLoteConSnapshotDePreciosAnteriores()
    {
        var (servicio, _, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA1", 1000m);
        await SembrarListingAsync(factory, "MLA2", 2000m);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Aumento 10%",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var batch = await servicio.GetBatchAsync(batchId);

        Assert.NotNull(batch);
        Assert.Equal(MercadoLibrePriceBatchEstado.Simulado, batch.Estado);
        Assert.Equal(2, batch.Items.Count);

        var item1 = batch.Items.Single(i => i.ItemId == "MLA1");
        Assert.Equal(1000m, item1.PrecioAnterior); // snapshot obligatorio
        Assert.Equal(1100m, item1.PrecioNuevo);
        Assert.Equal(10m, item1.DiferenciaPorcentaje);
        Assert.False(item1.Aplicado);
    }

    [Fact]
    public async Task Simular_ConVariaciones_GeneraUnItemPorVariacion()
    {
        var (servicio, _, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA3", 500m, variaciones: new long[] { 71, 72 });

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Variaciones",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 20m,
            SoloVinculadas = false
        }, "tester");

        var batch = await servicio.GetBatchAsync(batchId);

        Assert.Equal(2, batch!.Items.Count);
        Assert.All(batch.Items, i => Assert.NotNull(i.VariationId));
        Assert.All(batch.Items, i => Assert.Equal(600m, i.PrecioNuevo));
    }

    [Fact]
    public async Task Simular_ExcluyeFinalizadasYFiltraPorPrecio()
    {
        var (servicio, _, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA4", 100m);
        await SembrarListingAsync(factory, "MLA5", 5000m);
        await SembrarListingAsync(factory, "MLA6", 300m, status: "closed");

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Filtrado",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false,
            PrecioHasta = 1000m
        }, "tester");

        var batch = await servicio.GetBatchAsync(batchId);

        var item = Assert.Single(batch!.Items);
        Assert.Equal("MLA4", item.ItemId); // MLA5 fuera por precio, MLA6 por closed
    }

    // -----------------------------------------------------------------------
    // Aplicación
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Aplicar_ConModoSimulacion_NoLlamaApiYMarcaAplicadoEnSimulacion()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA10", 1000m);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = true);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Sim",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, mensaje) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje);
        Assert.Empty(api.UpdateItemCalls);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Aplicado, batch!.Estado);
        Assert.True(batch.AplicadoEnSimulacion);
        Assert.All(batch.Items, item =>
        {
            Assert.NotNull(item.PayloadAplicacionJson);
            Assert.Contains("\"price\"", item.PayloadAplicacionJson);
        });

        // El precio local NO cambió.
        await using var ctx = factory.CreateDbContext();
        Assert.Equal(1000m, (await ctx.MercadoLibreListings.SingleAsync()).Precio);
    }

    [Fact]
    public async Task Revertir_AplicadoEnSimulacion_NoLlamaApiYRegistraLog()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA10SIM", 1000m);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = true);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Sim rollback",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");
        api.UpdateItemCalls.Clear();

        var (ok, mensaje) = await servicio.RevertirAsync(batchId, "qa", confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Contains("No requiere rollback", mensaje);
        Assert.Empty(api.UpdateItemCalls);

        await using var ctx = factory.CreateDbContext();
        var batch = await ctx.MercadoLibrePriceBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Aplicado, batch.Estado);
        Assert.True(batch.AplicadoEnSimulacion);

        Assert.True(await ctx.MercadoLibreSyncLogs.AnyAsync(l =>
            l.Operacion == "PriceBatchRevertirSimulado" && l.Exito));
    }

    [Fact]
    public async Task Aplicar_Real_EmpujaPreciosYActualizaLocal()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA11", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Real",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, _) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        Assert.True(ok);

        var (itemId, payloadObj) = Assert.Single(api.UpdateItemCalls);
        Assert.Equal("MLA11", itemId);

        var payload = Assert.IsType<Dictionary<string, object>>(payloadObj);
        Assert.Equal(1100m, payload["price"]);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Aplicado, batch!.Estado);
        Assert.False(batch.AplicadoEnSimulacion);
        Assert.True(batch.Items.Single().Aplicado);

        await using var ctx = factory.CreateDbContext();
        Assert.Equal(1100m, (await ctx.MercadoLibreListings.SingleAsync()).Precio);
    }

    [Fact]
    public async Task Aplicar_Real_SinConfirmacionExplicita_Bloquea()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA11-NOCONFIRM", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Real sin confirmar",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, mensaje) = await servicio.AplicarAsync(batchId, confirmarReal: false, "tester");

        Assert.False(ok);
        Assert.Contains("confirmacion", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Simulado, batch!.Estado);
        Assert.False(batch.Items.Single().Aplicado);
    }

    [Fact]
    public async Task Aplicar_Real_ConVariaciones_UsaEndpointDeVariacion()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA-VAR", 1000m, vincular: true, variaciones: new long[] { 71, 72 });
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Real variaciones",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, _) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Equal(2, api.UpdateItemVariationCalls.Count);
        Assert.Equal(new[] { 71L, 72L }, api.UpdateItemVariationCalls.Select(c => c.VariationId).OrderBy(id => id));
        Assert.All(api.UpdateItemVariationCalls, call =>
        {
            Assert.Equal("MLA-VAR", call.ItemId);
            var payload = Assert.IsType<Dictionary<string, object>>(call.Payload);
            Assert.Equal(1100m, payload["price"]);
            Assert.False(payload.ContainsKey("id"));
            Assert.False(payload.ContainsKey("variations"));
        });
    }

    [Fact]
    public async Task Aplicar_Real_ConVariacionInexistente_NoLlamaApiYMarcaError()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA-VAR-MISSING", 1000m, vincular: true, variaciones: new long[] { 99 });
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Variacion faltante",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        await using (var ctx = factory.CreateDbContext())
        {
            var variacion = await ctx.MercadoLibreListingVariations.SingleAsync(v => v.VariationId == 99);
            variacion.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var (ok, _) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Empty(api.UpdateItemVariationCalls);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.AplicadoParcial, batch!.Estado);
        Assert.Contains("variacion", batch.Items.Single().Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Aplicar_ErrorParcial_MarcaAplicadoParcialConErroresPorItem()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA12", 1000m, vincular: true);
        await SembrarListingAsync(factory, "MLA13", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        api.UpdateItemFallan.Add("MLA12");

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Parcial",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, _) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        Assert.False(ok);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.AplicadoParcial, batch!.Estado);
        Assert.NotNull(batch.Items.Single(i => i.ItemId == "MLA12").Error);
        Assert.True(batch.Items.Single(i => i.ItemId == "MLA13").Aplicado);

        var listado = await servicio.GetBatchesAsync();
        var fila = listado.Single(b => b.Id == batchId);
        Assert.Equal(2, fila.CantidadItems);
        Assert.Equal(1, fila.CantidadErrores);
    }

    // -----------------------------------------------------------------------
    // Rollback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Revertir_RepublicaPreciosAnterioresDelSnapshot()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA14", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Rollback",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 50m,
            SoloVinculadas = false
        }, "tester");

        await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");
        api.UpdateItemCalls.Clear();

        var (ok, mensaje) = await servicio.RevertirAsync(batchId, "precio equivocado", confirmarReal: true, "tester");

        Assert.True(ok, mensaje);

        var (_, payloadObj) = Assert.Single(api.UpdateItemCalls);
        var payload = Assert.IsType<Dictionary<string, object>>(payloadObj);
        Assert.Equal(1000m, payload["price"]); // vuelve al snapshot anterior

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Revertido, batch!.Estado);
        Assert.True(batch.Items.Single().Revertido);
        Assert.Equal("precio equivocado", batch.MotivoReversion);

        await using var ctx = factory.CreateDbContext();
        Assert.Equal(1000m, (await ctx.MercadoLibreListings.SingleAsync()).Precio);
    }

    [Fact]
    public async Task Revertir_NoSeDuplica()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA14-DUP", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Rollback duplicado",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 50m,
            SoloVinculadas = false
        }, "tester");

        await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");
        api.UpdateItemCalls.Clear();

        var primero = await servicio.RevertirAsync(batchId, "primer rollback", confirmarReal: true, "tester");
        var segundo = await servicio.RevertirAsync(batchId, "duplicado", confirmarReal: true, "tester");

        Assert.True(primero.Ok);
        Assert.False(segundo.Ok);
        Assert.Single(api.UpdateItemCalls);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Revertido, batch!.Estado);
        Assert.True(batch.Items.Single().Revertido);
    }

    [Fact]
    public async Task Revertir_Real_ConModoSimulacion_Bloquea()
    {
        var (servicio, api, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA14-SIMBLOCK", 1000m, vincular: true);
        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Rollback bloqueado por simulacion",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 50m,
            SoloVinculadas = false
        }, "tester");

        await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");
        api.UpdateItemCalls.Clear();

        await ConfigurarAsync(factory, c => c.ModoSimulacion = true);

        var (ok, mensaje) = await servicio.RevertirAsync(batchId, "bloqueado", confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("modo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.Aplicado, batch!.Estado);
        Assert.False(batch.Items.Single().Revertido);
    }

    [Fact]
    public async Task Revertir_LoteSimulado_SeRechaza()
    {
        var (servicio, _, _, factory) = BuildServicio();

        await SembrarListingAsync(factory, "MLA15", 1000m);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "NoAplicado",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        var (ok, mensaje) = await servicio.RevertirAsync(batchId, null, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("solo se revierten lotes aplicados", mensaje);
    }

    // -----------------------------------------------------------------------
    // Checkpoint 1 — Regla de vinculación: sin Producto no hay cambios reales
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Aplicar_Real_OmiteSinVincularYAplicaSoloVinculadas()
    {
        var (servicio, api, _, factory) = BuildServicio();

        var listingSinVincularId = await SembrarListingAsync(factory, "MLA20", 1000m);
        var listingVinculadaId = await SembrarListingAsync(factory, "MLA21", 1000m);

        // Vincular la segunda a un producto real.
        await using (var ctx = factory.CreateDbContext())
        {
            var marca = new Marca { Codigo = $"M-{Guid.NewGuid():N}"[..10], Nombre = "Marca", Activo = true };
            ctx.Marcas.Add(marca);
            await ctx.SaveChangesAsync();

            var producto = new Producto
            {
                Codigo = $"P-{Guid.NewGuid():N}"[..10],
                Nombre = "Vinculado",
                CategoriaId = 1,
                MarcaId = marca.Id,
                PrecioCompra = 100m,
                PrecioVenta = 900m,
                Activo = true
            };
            ctx.Productos.Add(producto);
            await ctx.SaveChangesAsync();

            (await ctx.MercadoLibreListings.SingleAsync(l => l.Id == listingVinculadaId)).ProductoId = producto.Id;
            await ctx.SaveChangesAsync();
        }

        await ConfigurarAsync(factory, c => c.ModoSimulacion = false);

        var batchId = await servicio.SimularAsync(new MercadoLibrePriceBatchRequest
        {
            Nombre = "Mixto",
            Origen = MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
            ValorAjustePorcentaje = 10m,
            SoloVinculadas = false
        }, "tester");

        // La simulación ya advierte sobre la publicación sin vincular.
        var simulado = await servicio.GetBatchAsync(batchId);
        var itemSinVincular = simulado!.Items.Single(i => i.ItemId == "MLA20");
        Assert.True(itemSinVincular.TieneAdvertencia);
        Assert.Contains("Sin producto vinculado", itemSinVincular.MensajeAdvertencia);

        var (ok, _) = await servicio.AplicarAsync(batchId, confirmarReal: true, "tester");

        // Parcial: la vinculada se aplicó, la otra quedó con error y SIN PUT.
        Assert.False(ok);

        var (itemIdPusheado, _) = Assert.Single(api.UpdateItemCalls);
        Assert.Equal("MLA21", itemIdPusheado);

        var batch = await servicio.GetBatchAsync(batchId);
        Assert.Equal(MercadoLibrePriceBatchEstado.AplicadoParcial, batch!.Estado);

        var sinVincular = batch.Items.Single(i => i.ItemId == "MLA20");
        Assert.False(sinVincular.Aplicado);
        Assert.Contains("Vinculá esta publicación", sinVincular.Error);

        Assert.True(batch.Items.Single(i => i.ItemId == "MLA21").Aplicado);

        // El precio local de la sin-vincular no cambió.
        await using var ctx2 = factory.CreateDbContext();
        Assert.Equal(1000m, (await ctx2.MercadoLibreListings.SingleAsync(l => l.Id == listingSinVincularId)).Precio);
    }
}
