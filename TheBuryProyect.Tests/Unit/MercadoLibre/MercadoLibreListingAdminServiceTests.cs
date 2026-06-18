using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase E: ABM de publicaciones ML.
/// Cubre simulación (no llama ML), modo real (usa VariationId, no PUT destructivo),
/// logs sin tokens y bloqueos correctos.
/// </summary>
public class MercadoLibreListingAdminServiceTests
{
    // ------------------------------------------------------------------
    // Infraestructura
    // ------------------------------------------------------------------

    private sealed class FakeAdminAuthService : IMercadoLibreAuthService
    {
        public const string FakeToken = "fake-access-token-not-stored-in-logs";

        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult(FakeToken);
    }

    private sealed class NullPricingService : IMercadoLibrePricingService
    {
        public Task<IReadOnlyDictionary<int, MercadoLibrePrecioCanal>> CalcularPrecioCanalAsync(
            IReadOnlyCollection<int> productoIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<int, MercadoLibrePrecioCanal>>(
                new Dictionary<int, MercadoLibrePrecioCanal>());

        public Task<MercadoLibreDesglosePrecio?> CalcularDesgloseAsync(int productoId, CancellationToken ct = default)
            => Task.FromResult<MercadoLibreDesglosePrecio?>(null);

        public decimal Redondear(decimal precio, string regla) => precio;
    }

    private static (MercadoLibreListingAdminService Servicio, FakeMercadoLibreApiClient Api, TestDbContextFactory Factory)
        BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var api = new FakeMercadoLibreApiClient();
        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var servicio = new MercadoLibreListingAdminService(
            factory, api, new FakeAdminAuthService(), configService,
            new NullPricingService(),
            NullLogger<MercadoLibreListingAdminService>.Instance);

        return (servicio, api, factory);
    }

    private static async Task ConfigurarModoAsync(TestDbContextFactory factory, bool modoSimulacion)
    {
        await using var ctx = factory.CreateDbContext();
        var config = await ctx.MercadoLibreConfiguraciones.FirstOrDefaultAsync();
        if (config is null)
        {
            config = new MercadoLibreConfiguracion { ModoSimulacion = modoSimulacion };
            ctx.MercadoLibreConfiguraciones.Add(config);
        }
        else
        {
            config.ModoSimulacion = modoSimulacion;
        }
        await ctx.SaveChangesAsync();
    }

    private static async Task<(int ListingId, string ItemId)> SembrarListingAsync(
        TestDbContextFactory factory,
        string status = "active",
        long[]? variaciones = null,
        string? categoryId = null)
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
        await ctx.SaveChangesAsync();

        var itemId = $"MLA{Random.Shared.Next(100000, 999999)}";
        var listing = new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = itemId,
            Titulo = "Titulo original",
            Precio = 1000m,
            AvailableQuantity = 5,
            Status = status,
            CategoryId = categoryId,
            TieneVariaciones = variaciones?.Length > 0
        };

        if (variaciones != null)
        {
            foreach (var vid in variaciones)
            {
                listing.Variaciones.Add(new MercadoLibreListingVariation
                {
                    VariationId = vid,
                    Precio = 1000m,
                    AvailableQuantity = 5
                });
            }
        }

        ctx.MercadoLibreListings.Add(listing);
        await ctx.SaveChangesAsync();

        return (listing.Id, itemId);
    }

    // ------------------------------------------------------------------
    // Simulación: ninguna llamada sale hacia ML
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_TituloEnSimulacion_NoLlamaMl()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);
        // ModoSimulacion=true es el default de MercadoLibreConfiguracion

        var (ok, mensaje) = await servicio.EditarAsync(
            listingId, titulo: "Nuevo título", precio: null, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Empty(api.UpdateItemVariationCalls);

        await using var ctx = factory.CreateDbContext();
        var log = await ctx.MercadoLibreSyncLogs
            .FirstOrDefaultAsync(l => l.Operacion == "EditarPublicacion");
        Assert.NotNull(log);
        Assert.True(log!.Exito);
        Assert.Contains("SIMULADO", log.Detalle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Editar_DescripcionEnSimulacion_NoLlamaMl()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        var (ok, mensaje) = await servicio.EditarDescripcionAsync(
            listingId, "Descripción nueva", confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateDescripcionCalls);

        await using var ctx = factory.CreateDbContext();
        var log = await ctx.MercadoLibreSyncLogs
            .FirstAsync(l => l.Operacion == "EditarDescripcion");
        Assert.True(log.Exito);
        Assert.Contains("SIMULADO", log.Detalle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CambiarEstado_PausarEnSimulacion_NoLlamaMl()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        var (ok, mensaje) = await servicio.CambiarEstadoAsync(
            listingId, "pausar", confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task EditarCategoria_EnSimulacion_NoLlamaMl()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory, categoryId: "MLA1000");
        // ModoSimulacion=true (default) → no llama ML

        var (ok, mensaje) = await servicio.EditarCategoriaAsync(
            listingId, categoryId: "MLA416632", confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);

        await using var ctx = factory.CreateDbContext();
        var log = await ctx.MercadoLibreSyncLogs
            .FirstOrDefaultAsync(l => l.Operacion == "EditarCategoria");
        Assert.NotNull(log);
        Assert.True(log!.Exito);
        Assert.Contains("SIMULADO", log.Detalle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditarCategoria_ModoReal_EnviaCategoryIdEnPayload()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, itemId) = await SembrarListingAsync(factory, categoryId: "MLA1000");
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        api.UpdateItemRespuestas[itemId] = new MeliItemDto
            { Id = itemId, CategoryId = "MLA416632", Status = "active" };

        var (ok, _) = await servicio.EditarCategoriaAsync(
            listingId, categoryId: "MLA416632", confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Single(api.UpdateItemCalls);
        Assert.Equal(itemId, api.UpdateItemCalls[0].ItemId);

        // El payload debe llevar category_id con la nueva categoría.
        var payload = Assert.IsType<Dictionary<string, object>>(api.UpdateItemCalls[0].Payload);
        Assert.Equal("MLA416632", Assert.Contains("category_id", payload));

        // Persiste la nueva categoría localmente.
        await using var ctx = factory.CreateDbContext();
        var listing = await ctx.MercadoLibreListings.FirstAsync(l => l.Id == listingId);
        Assert.Equal("MLA416632", listing.CategoryId);
    }

    [Fact]
    public async Task EditarCategoria_MismaCategoria_RetornaFalse()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory, categoryId: "MLA416632");

        var (ok, mensaje) = await servicio.EditarCategoriaAsync(
            listingId, categoryId: "MLA416632", confirmarReal: false, usuario: "tester");

        Assert.False(ok);
        Assert.Contains("misma", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task EditarCategoria_Vacia_RetornaFalse()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory, categoryId: "MLA1000");

        var (ok, _) = await servicio.EditarCategoriaAsync(
            listingId, categoryId: "   ", confirmarReal: false, usuario: "tester");

        Assert.False(ok);
        Assert.Empty(api.UpdateItemCalls);
    }

    // ------------------------------------------------------------------
    // Modo simulación forzado: confirmarReal no cambia el resultado
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_ModoSimulacionTrue_SempreSimulaAunqueConfirmaReal()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: "Test forzado", precio: null, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task CambiarEstado_SinConfirmarReal_SempreSimulaEnModoReal()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        // confirmarReal=false → simula aunque ModoSimulacion=false
        var (ok, mensaje) = await servicio.CambiarEstadoAsync(
            listingId, "pausar", confirmarReal: false, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
    }

    // ------------------------------------------------------------------
    // Modo real: precio simple → PUT al item; precio con variación → PUT por variación
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_PrecioSinVariaciones_UsaUpdateItem()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, itemId) = await SembrarListingAsync(factory);
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        api.UpdateItemRespuestas[itemId] = new MeliItemDto
            { Id = itemId, Price = 2000m, Status = "active" };

        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: null, precio: 2000m, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Single(api.UpdateItemCalls);
        Assert.Equal(itemId, api.UpdateItemCalls[0].ItemId);
        Assert.Empty(api.UpdateItemVariationCalls);
    }

    [Fact]
    public async Task Editar_PrecioConUnaVariacion_UsaVariationId()
    {
        const long variationId = 196332252298L;

        var (servicio, api, factory) = BuildServicio();
        var (listingId, itemId) = await SembrarListingAsync(factory, variaciones: new[] { variationId });
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: null, precio: 2000m, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        // Precio con variación: no PUT al item
        Assert.Empty(api.UpdateItemCalls);
        // PUT individual por variationId
        Assert.Single(api.UpdateItemVariationCalls);
        Assert.Equal(itemId, api.UpdateItemVariationCalls[0].ItemId);
        Assert.Equal(variationId, api.UpdateItemVariationCalls[0].VariationId);
    }

    // ------------------------------------------------------------------
    // No PUT destructivo: varias variaciones → un PUT por variación
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_PrecioConDosVariaciones_NoPutDestructivo()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, itemId) = await SembrarListingAsync(
            factory, variaciones: new long[] { 111L, 222L });
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: null, precio: 5000m, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        // Nunca un PUT al item completo cuando hay variaciones de precio
        Assert.Empty(api.UpdateItemCalls);
        // Dos PUTs individuales: uno por variación
        Assert.Equal(2, api.UpdateItemVariationCalls.Count);
        Assert.Contains(api.UpdateItemVariationCalls, c => c.VariationId == 111L);
        Assert.Contains(api.UpdateItemVariationCalls, c => c.VariationId == 222L);
    }

    // ------------------------------------------------------------------
    // Bloqueos correctos
    // ------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstado_AccionInvalida_RetornaFalse()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        var (ok, mensaje) = await servicio.CambiarEstadoAsync(
            listingId, "no-existe", confirmarReal: false, usuario: "tester");

        Assert.False(ok);
        Assert.Contains("inválida", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task Editar_SinCambios_RetornaFalse()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        // Mismo título que el original, sin otros cambios
        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: "Titulo original", precio: null, stock: null,
            sellerSku: null, confirmarReal: false, usuario: "tester");

        Assert.False(ok);
        Assert.Empty(api.UpdateItemCalls);
    }

    [Fact]
    public async Task Editar_StockConVariasVariaciones_RetornaError()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(
            factory, variaciones: new long[] { 111L, 222L });

        var (ok, mensaje) = await servicio.EditarAsync(
            listingId, titulo: null, precio: null, stock: 10,
            sellerSku: null, confirmarReal: false, usuario: "tester");

        Assert.False(ok);
        Assert.Contains("variación", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Empty(api.UpdateItemVariationCalls);
    }

    // ------------------------------------------------------------------
    // Logs no contienen tokens de acceso
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_LogsNoContienenTokenDeAcceso()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, itemId) = await SembrarListingAsync(factory);
        await ConfigurarModoAsync(factory, modoSimulacion: false);

        api.UpdateItemRespuestas[itemId] = new MeliItemDto
            { Id = itemId, Title = "Nuevo", Status = "active" };

        await servicio.EditarAsync(
            listingId, titulo: "Nuevo", precio: null, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        await using var ctx = factory.CreateDbContext();
        var logs = await ctx.MercadoLibreSyncLogs.ToListAsync();

        foreach (var log in logs)
        {
            Assert.DoesNotContain(
                FakeAdminAuthService.FakeToken, log.Detalle ?? string.Empty,
                StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // Edición directa no requiere producto vinculado
    // ------------------------------------------------------------------

    [Fact]
    public async Task Editar_ListingSinProductoVinculado_PrecioEditableEnSimulacion()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory); // ProductoId es null
        // ModoSimulacion=true default → no llama ML

        var (ok, _) = await servicio.EditarAsync(
            listingId, titulo: null, precio: 9999m, stock: null,
            sellerSku: null, confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Empty(api.UpdateItemCalls); // simulado, pero no bloqueado por falta de producto
    }

    // ------------------------------------------------------------------
    // Finalizar: irreversible — la acción se registra y en simulación no persiste en ML
    // ------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstado_Finalizar_EnSimulacion_RegistraLogSinLlamarMl()
    {
        var (servicio, api, factory) = BuildServicio();
        var (listingId, _) = await SembrarListingAsync(factory);

        var (ok, mensaje) = await servicio.CambiarEstadoAsync(
            listingId, "finalizar", confirmarReal: true, usuario: "tester");

        Assert.True(ok);
        Assert.Contains("SIMULACIÓN", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.UpdateItemCalls);

        await using var ctx = factory.CreateDbContext();
        var log = await ctx.MercadoLibreSyncLogs
            .FirstAsync(l => l.Operacion == "CambiarEstado");
        Assert.True(log.Exito);
        Assert.Contains("closed", log.Detalle!, StringComparison.OrdinalIgnoreCase);
    }
}
