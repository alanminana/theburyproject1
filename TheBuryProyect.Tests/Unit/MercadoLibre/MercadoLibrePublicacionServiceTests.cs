using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

public class MercadoLibrePublicacionServiceTests
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public int TokenCalls { get; private set; }
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
        {
            TokenCalls++;
            return Task.FromResult("token-test");
        }
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

    private static (MercadoLibrePublicacionService Servicio, FakeMercadoLibreApiClient Api,
        FakeAuthService Auth, FakePrecioVigenteResolver Resolver, TestDbContextFactory Factory)
        BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var api = new FakeMercadoLibreApiClient();
        var auth = new FakeAuthService();
        var resolver = new FakePrecioVigenteResolver();

        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var pricing = new MercadoLibrePricingService(configService, resolver);

        var servicio = new MercadoLibrePublicacionService(
            factory, api, auth, configService, pricing,
            NullLogger<MercadoLibrePublicacionService>.Instance);

        return (servicio, api, auth, resolver, factory);
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

    private static async Task<int> SembrarProductoAsync(
        TestDbContextFactory factory,
        string codigo = "ML-QA-1",
        string nombre = "Producto ML QA",
        decimal precioVenta = 1000m,
        decimal precioCompra = 500m,
        decimal stock = 5m)
    {
        await using var ctx = factory.CreateDbContext();

        var categoria = new Categoria
        {
            Codigo = $"C{Guid.NewGuid():N}"[..12],
            Nombre = "Categoria ML",
            Activo = true
        };

        var marca = new Marca
        {
            Codigo = $"M{Guid.NewGuid():N}"[..12],
            Nombre = "Marca ML",
            Activo = true
        };

        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = nombre,
            Descripcion = "Descripcion QA",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioCompra,
            PrecioVenta = precioVenta,
            StockActual = stock,
            Activo = true
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        return producto.Id;
    }

    private static async Task<int> SembrarCuentaAsync(TestDbContextFactory factory)
    {
        await using var ctx = factory.CreateDbContext();

        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
            Nickname = "QA",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };

        ctx.MercadoLibreAccounts.Add(cuenta);
        await ctx.SaveChangesAsync();

        return cuenta.Id;
    }

    private static async Task<int> CrearBorradorValidadoAsync(
        MercadoLibrePublicacionService servicio,
        TestDbContextFactory factory,
        int productoId,
        int stock = 2,
        string categoriaMl = "MLA1055")
    {
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
            borrador.CategoryIdMl = categoriaMl;
            borrador.Stock = stock;
            await ctx.SaveChangesAsync();
        }

        var (ok, _, _) = await servicio.ValidarAsync(borradorId, "tester");
        Assert.True(ok);

        return borradorId;
    }

    private static async Task<int> CrearBorradorValidadoConImagenesAsync(
        MercadoLibrePublicacionService servicio,
        TestDbContextFactory factory,
        int productoId,
        string[] imagenes,
        int stock = 2,
        string categoriaMl = "MLA1055",
        string listingType = "gold_special")
    {
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
            borrador.CategoryIdMl = categoriaMl;
            borrador.Stock = stock;
            borrador.ListingTypeId = listingType;
            borrador.ImagenesJson = imagenes.Length == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(imagenes);
            await ctx.SaveChangesAsync();
        }

        var (ok, _, _) = await servicio.ValidarAsync(borradorId, "tester");
        Assert.True(ok);

        return borradorId;
    }

    [Fact]
    public async Task CrearBorrador_DesdeProducto_PrellenaDatosYVinculaProducto()
    {
        var (servicio, _, _, resolver, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, precioVenta: 1000m, stock: 4m);
        resolver.Precios[productoId] = (1234m, 500m);

        var resultado = await servicio.CrearBorradorAsync(productoId, "tester");

        Assert.False(resultado.Existia);
        Assert.NotNull(resultado.BorradorId);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();

        Assert.Equal(productoId, borrador.ProductoId);
        Assert.Equal(1234m, borrador.Precio);
        Assert.Equal(4, borrador.Stock);
        Assert.Equal(MercadoLibreBorradorEstado.Borrador, borrador.Estado);
    }

    [Fact]
    public async Task CrearBorrador_ConBorradorActivoExistente_NoDuplica()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory);

        var primero = await servicio.CrearBorradorAsync(productoId, "tester");
        var segundo = await servicio.CrearBorradorAsync(productoId, "tester");

        Assert.True(segundo.Existia);
        Assert.Equal(primero.BorradorId, segundo.BorradorId);

        await using var ctx = factory.CreateDbContext();
        Assert.Equal(1, await ctx.MercadoLibrePublicacionBorradores.CountAsync());
    }

    [Fact]
    public async Task CrearBorrador_ConPublicacionActivaExistente_NoCreaBorrador()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory);

        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = new MercadoLibreAccount
            {
                MeliUserId = 123,
                Nickname = "QA",
                AccessTokenEncrypted = "x",
                RefreshTokenEncrypted = "x",
                Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();

            ctx.MercadoLibreListings.Add(new MercadoLibreListing
            {
                AccountId = cuenta.Id,
                ItemId = "MLAQA1",
                Titulo = "Publicacion existente",
                Precio = 1000m,
                AvailableQuantity = 1,
                Status = "active",
                ProductoId = productoId
            });
            await ctx.SaveChangesAsync();
        }

        var resultado = await servicio.CrearBorradorAsync(productoId, "tester");

        Assert.True(resultado.Existia);
        Assert.Null(resultado.BorradorId);
        Assert.NotNull(resultado.ListingId);

        await using var verificar = factory.CreateDbContext();
        Assert.Equal(0, await verificar.MercadoLibrePublicacionBorradores.CountAsync());
    }

    [Fact]
    public async Task Validar_SinCategoriaMl_Falla()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");

        var (ok, errores, _) = await servicio.ValidarAsync(creado.BorradorId!.Value, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("categor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validar_StockMayorAlDisponible_Falla()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 2m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();
            borrador.CategoryIdMl = "MLA1055";
            borrador.Stock = 3;
            await ctx.SaveChangesAsync();
        }

        var (ok, errores, _) = await servicio.ValidarAsync(creado.BorradorId!.Value, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("supera el disponible", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Publicar_SinConfirmarReal_SimulaPorDefectoYDejaEvidenciaVisible()
    {
        // Checkpoint 11.1/11.2: sin "Publicación REAL" marcada (confirmarReal=false) el
        // borrador SIEMPRE se simula, sin importar permiso ni cuenta.
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = false;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: false, "tester");

        Assert.True(ok);
        Assert.Contains("SIMUL", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
        Assert.Equal(0, auth.TokenCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.True(borrador.PublicadoEnSimulacion);
        Assert.NotNull(borrador.FechaSimulacionUtc);
        Assert.Contains("\"title\"", borrador.PayloadSimuladoJson);
        Assert.Equal(MercadoLibreBorradorEstado.Validado, borrador.Estado);
        Assert.Equal(0, await ctx.MercadoLibreListings.CountAsync());
    }

    [Fact]
    public async Task Publicar_SinConfirmarReal_SimulaAunConPermisoYCuenta()
    {
        // Checkpoint 11.2: la publicación real exige el checkbox; con permiso y cuenta
        // pero sin confirmarReal, igual SIMULA (no llama a la API).
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: false, "tester");

        Assert.True(ok);
        Assert.Contains("SIMUL", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
        Assert.Equal(0, auth.TokenCalls);
    }

    [Fact]
    public async Task Publicar_Real_BloqueaSiPermitirPublicacionDesdeErpEsFalse()
    {
        var (servicio, api, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        await ConfigurarAsync(factory, c =>
        {
            c.ModoSimulacion = false;
            c.PermitirPublicacionDesdeErp = false;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("deshabilitada", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
    }

    [Fact]
    public async Task Publicar_Real_BloqueaSiNoHayCuentaConectada()
    {
        // Con permiso pero sin cuenta, la publicación real queda bloqueada.
        var (servicio, api, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = null;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("cuenta", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
    }

    [Fact]
    public async Task Publicar_Real_CreaListingVinculadoAlProducto()
    {
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, codigo: "SKU-ML-1", stock: 3m);

        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = new MercadoLibreAccount
            {
                MeliUserId = 987,
                Nickname = "QA",
                AccessTokenEncrypted = "x",
                RefreshTokenEncrypted = "x",
                Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();

            ctx.MercadoLibreConfiguraciones.Add(new MercadoLibreConfiguracion
            {
                AccountId = cuenta.Id,
                ModoSimulacion = false,
                PermitirPublicacionDesdeErp = true
            });
            await ctx.SaveChangesAsync();
        }

        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);
        api.CreateItemRespuesta = new MeliItemDto
        {
            Id = "MLA900",
            Title = "Producto publicado",
            Price = 1500m,
            CurrencyId = "ARS",
            AvailableQuantity = 2,
            Status = "active",
            CategoryId = "MLA1055",
            ListingTypeId = "gold_special",
            Condition = "new"
        };

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Contains("MLA900", mensaje);
        Assert.Single(api.CreateItemCalls);
        Assert.Equal(1, auth.TokenCalls);

        await using var verificar = factory.CreateDbContext();
        var listing = await verificar.MercadoLibreListings.SingleAsync();
        Assert.Equal(productoId, listing.ProductoId);
        Assert.Equal("MLA900", listing.ItemId);

        var borrador = await verificar.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Equal(MercadoLibreBorradorEstado.Publicado, borrador.Estado);
        Assert.False(borrador.PublicadoEnSimulacion);
    }

    [Fact]
    public async Task Validar_PrecioMenorOIgualACero_Falla()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();
            borrador.CategoryIdMl = "MLA1055";
            borrador.Precio = 0m;
            await ctx.SaveChangesAsync();
        }

        var (ok, errores, _) = await servicio.ValidarAsync(borradorId, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("precio", StringComparison.OrdinalIgnoreCase));

        await using var verificar = factory.CreateDbContext();
        var final = await verificar.MercadoLibrePublicacionBorradores.SingleAsync();
        Assert.Equal(MercadoLibreBorradorEstado.Borrador, final.Estado);
    }

    [Fact]
    public async Task Validar_StockDentroDelDisponible_Pasa()
    {
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 5m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();
            borrador.CategoryIdMl = "MLA1055";
            borrador.Stock = 3;
            await ctx.SaveChangesAsync();
        }

        var (ok, errores, _) = await servicio.ValidarAsync(borradorId, "tester");

        Assert.True(ok);
        Assert.Empty(errores);

        await using var verificar = factory.CreateDbContext();
        var final = await verificar.MercadoLibrePublicacionBorradores.SingleAsync();
        Assert.Equal(MercadoLibreBorradorEstado.Validado, final.Estado);
    }

    [Fact]
    public async Task Publicar_ConConfirmarReal_NoDependeDeModoSimulacion()
    {
        // Checkpoint 11.4: el flujo de publicación ya NO depende de ModoSimulacion.
        // Aunque el cerrojo global esté en true, con "Publicación REAL" marcada +
        // permiso + cuenta, se publica REAL (llama a la API).
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, codigo: "SKU-DECOUP", stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.ModoSimulacion = true; // cerrojo global activo: ya no manda en publicación
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);
        api.CreateItemRespuesta = new MeliItemDto
        {
            Id = "MLA902",
            Title = "Publicado",
            Price = 1500m,
            CurrencyId = "ARS",
            AvailableQuantity = 2,
            Status = "active",
            CategoryId = "MLA1055",
            ListingTypeId = "gold_special",
            Condition = "new"
        };

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Contains("MLA902", mensaje);
        Assert.Single(api.CreateItemCalls);
        Assert.Equal(1, auth.TokenCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Equal(MercadoLibreBorradorEstado.Publicado, borrador.Estado);
        Assert.False(borrador.PublicadoEnSimulacion);
        Assert.Equal(1, await ctx.MercadoLibreListings.CountAsync());
    }

    [Fact]
    public async Task Publicar_RealRechazadoPorMl_MarcaCamposPorReferences()
    {
        // Checkpoint 11.6: si ML rechaza con cause[]/references, se persisten errores
        // por campo (línea machine-readable CAMPOS_ERROR) sin llamar a ML real.
        var (servicio, api, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, codigo: "SKU-REJ", stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);
        api.CreateItemFalla = true;
        api.CreateItemErrorExcerpt =
            "{\"message\":\"Validation error\",\"error\":\"validation_error\",\"cause\":[" +
            "{\"code\":\"item.available_quantity.invalid\",\"message\":\"max 1\",\"references\":[\"available_quantity\"]}," +
            "{\"code\":\"item.category_id.invalid\",\"message\":\"no es hoja\",\"references\":[\"category_id\"]}]}";

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("rechazó", mensaje, StringComparison.OrdinalIgnoreCase);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Contains("CAMPOS_ERROR:", borrador.ErroresValidacion);
        Assert.Contains("available_quantity", borrador.ErroresValidacion);
        Assert.Contains("category_id", borrador.ErroresValidacion);
        Assert.Contains("Stock a publicar", borrador.ErroresValidacion);
    }

    [Fact]
    public async Task Actualizar_StockEditable_PersisteSinModificarInventario()
    {
        // Checkpoint 11.7: bajar el stock a publicar persiste y no toca el inventario ERP.
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 5m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        var vm = await servicio.GetBorradorAsync(borradorId);
        vm!.Stock = 1; // baja respecto del stock ERP (5)
        vm.CategoryIdMl = "MLA1055";
        await servicio.ActualizarBorradorAsync(vm, "tester");

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Equal(1, borrador.Stock);

        var producto = await ctx.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(5m, producto.StockActual); // inventario intacto
    }

    [Fact]
    public async Task Descartar_NoLlamaApiMlYMarcaDescartado()
    {
        // Checkpoint 11.8: descartar nunca llama a ML.
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await servicio.DescartarAsync(borradorId, "tester");

        Assert.Empty(api.CreateItemCalls);
        Assert.Empty(api.UpdateItemCalls);
        Assert.Equal(0, auth.TokenCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Equal(MercadoLibreBorradorEstado.Descartado, borrador.Estado);
    }

    [Fact]
    public async Task Publicar_Real_NoEscribeTokenEnLogs()
    {
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, codigo: "SKU-ML-TOK", stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.ModoSimulacion = false;
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        var borradorId = await CrearBorradorValidadoAsync(servicio, factory, productoId);
        api.CreateItemRespuesta = new MeliItemDto
        {
            Id = "MLA901",
            Title = "Publicado",
            Price = 1500m,
            CurrencyId = "ARS",
            AvailableQuantity = 2,
            Status = "active",
            CategoryId = "MLA1055",
            ListingTypeId = "gold_special",
            Condition = "new"
        };

        var (ok, _) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.True(ok);
        Assert.Equal(1, auth.TokenCalls); // el token se obtuvo y usó realmente

        await using var ctx = factory.CreateDbContext();
        var logs = await ctx.MercadoLibreSyncLogs.AsNoTracking().ToListAsync();
        Assert.NotEmpty(logs);
        // El token que devuelve el fake ("token-test") nunca debe aparecer en los logs.
        Assert.DoesNotContain(logs, l => (l.Detalle ?? "").Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    // ── Imágenes (requiresPictures) ─────────────────────────────────────

    [Fact]
    public async Task Publicar_Simulacion_IncluyePicturesCuandoHayImagenes()
    {
        // Checkpoint 5.1: el payload simulado incluye pictures[{source}] con las URLs.
        var (servicio, api, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var borradorId = await CrearBorradorValidadoConImagenesAsync(
            servicio, factory, productoId,
            new[] { "https://img.example.com/a.jpg", "https://img.example.com/b.jpg" });

        var (ok, _) = await servicio.PublicarAsync(borradorId, confirmarReal: false, "tester");

        Assert.True(ok);
        Assert.Empty(api.CreateItemCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Contains("pictures", borrador.PayloadSimuladoJson);
        Assert.Contains("source", borrador.PayloadSimuladoJson);
        Assert.Contains("https://img.example.com/a.jpg", borrador.PayloadSimuladoJson);
    }

    [Fact]
    public async Task Publicar_Real_FreeSinImagenes_BloqueaLocalSinLlamarMl()
    {
        // Checkpoint 5.2: free sin imágenes se bloquea ANTES de POST /items y marca campos.
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        var borradorId = await CrearBorradorValidadoConImagenesAsync(
            servicio, factory, productoId, Array.Empty<string>(), listingType: "free");

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("imágenes", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
        Assert.Equal(0, auth.TokenCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Contains("CAMPOS_ERROR:", borrador.ErroresValidacion);
        Assert.Contains("listing_type_id", borrador.ErroresValidacion);
        Assert.Contains("pictures", borrador.ErroresValidacion);
    }

    [Fact]
    public async Task Publicar_RealRechazadoPorRequiresPictures_MarcaTipoYImagenes()
    {
        // Checkpoint 5.3/5.4/5.5: el error item.listing_type_id.requiresPictures se parsea
        // y marca AMBOS campos referenciados (Tipo de publicación + Imágenes).
        var (servicio, api, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var accountId = await SembrarCuentaAsync(factory);
        await ConfigurarAsync(factory, c =>
        {
            c.PermitirPublicacionDesdeErp = true;
            c.AccountId = accountId;
        });
        // free CON una imagen válida: pasa la guarda local y llega a ML (que acá rechaza).
        var borradorId = await CrearBorradorValidadoConImagenesAsync(
            servicio, factory, productoId,
            new[] { "https://img.example.com/a.jpg" }, listingType: "free");
        api.CreateItemFalla = true;
        api.CreateItemErrorExcerpt =
            "{\"message\":\"Item pictures are mandatory for listing type free\",\"error\":\"validation_error\",\"cause\":[" +
            "{\"department\":\"items\",\"cause_id\":173,\"type\":\"error\"," +
            "\"code\":\"item.listing_type_id.requiresPictures\"," +
            "\"references\":[\"item.listing_type_id\",\"item.pictures\"]," +
            "\"message\":\"Item pictures are mandatory for listing type free\"}]}";

        var (ok, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: true, "tester");

        Assert.False(ok);
        Assert.Contains("rechazó", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Single(api.CreateItemCalls);

        await using var ctx = factory.CreateDbContext();
        var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync(b => b.Id == borradorId);
        Assert.Contains("CAMPOS_ERROR:", borrador.ErroresValidacion);
        Assert.Contains("listing_type_id", borrador.ErroresValidacion);
        Assert.Contains("pictures", borrador.ErroresValidacion);
        Assert.Contains("Tipo de publicación", borrador.ErroresValidacion);
        Assert.Contains("Imágenes", borrador.ErroresValidacion);
    }

    [Fact]
    public async Task Validar_ConImagenInvalida_Falla()
    {
        // Checkpoint 5.6: una URL que no es http/https rechaza la validación.
        var (servicio, _, _, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();
            borrador.CategoryIdMl = "MLA1055";
            borrador.ImagenesJson = System.Text.Json.JsonSerializer.Serialize(
                new[] { "ftp://malo.example.com/x.jpg", "no-es-una-url" });
            await ctx.SaveChangesAsync();
        }

        var (ok, errores, _) = await servicio.ValidarAsync(borradorId, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("URL inválida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validar_FreeSinImagenes_AdvierteSinBloquearYSimulaSinLlamarMl()
    {
        // Checkpoint 5.7: free sin imágenes NO bloquea la validación (solo advierte) y la
        // simulación sigue funcionando sin llamar a ML.
        var (servicio, api, auth, _, factory) = BuildServicio();
        var productoId = await SembrarProductoAsync(factory, stock: 3m);
        var creado = await servicio.CrearBorradorAsync(productoId, "tester");
        var borradorId = creado.BorradorId!.Value;

        await using (var ctx = factory.CreateDbContext())
        {
            var borrador = await ctx.MercadoLibrePublicacionBorradores.SingleAsync();
            borrador.CategoryIdMl = "MLA1055";
            borrador.ListingTypeId = "free";
            await ctx.SaveChangesAsync();
        }

        var (ok, errores, advertencias) = await servicio.ValidarAsync(borradorId, "tester");

        Assert.True(ok);
        Assert.Empty(errores);
        Assert.Contains(advertencias, a => a.Contains("gratuita", StringComparison.OrdinalIgnoreCase));

        var (okSim, mensaje) = await servicio.PublicarAsync(borradorId, confirmarReal: false, "tester");

        Assert.True(okSim);
        Assert.Contains("SIMUL", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.CreateItemCalls);
        Assert.Equal(0, auth.TokenCalls);
    }
}
