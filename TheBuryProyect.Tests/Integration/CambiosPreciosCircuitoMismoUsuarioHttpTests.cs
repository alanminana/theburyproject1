using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-01 / R-14 (decisión del propietario): quien tiene los permisos correspondientes puede simular, aprobar y aplicar un
/// cambio de precios por lotes SIN que se exija otro usuario. La auditoría se mantiene (quién simuló, aprobó y aplicó, y cuándo).
/// Cada paso exige su permiso propio, también por POST directo.
/// </summary>
[Collection("HttpIntegration.3")]
public class CambiosPreciosCircuitoMismoUsuarioHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public CambiosPreciosCircuitoMismoUsuarioHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await _factory.SeedUserWithPreciosCambioRapidoCompletoAsync();
        await _factory.SeedUserWithPreciosSimularAprobarAsync();
        await _factory.SeedUserWithPreciosSimularAplicarAsync();
        await _factory.SeedUserWithPreciosAprobarAplicarAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, params (string, string)[] campos)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2)))
        };
        req.Headers.Add("RequestVerificationToken", await TokenAsync(client));
        return await client.SendAsync(req);
    }

    private static void AssertDenegado(HttpResponseMessage resp) =>
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect,
            $"Se esperaba 403 o redirección a acceso denegado, llegó {(int)resp.StatusCode}.");

    private static void AssertDenegadoPorPermiso(HttpResponseMessage resp)
    {
        AssertDenegado(resp);
        if (resp.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("AccessDenied", resp.Headers.Location?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(int ListaId, int ProductoId)> SembrarProductoConPrecioAsync(decimal precio = 100m)
    {
        await using var ctx = await NuevoContextoAsync();
        var code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        var lista = new ListaPrecio { Nombre = "Lista " + code, Codigo = "R" + code, Tipo = TipoListaPrecio.Contado, Activa = true, Orden = 1 };
        ctx.AddRange(cat, marca, lista);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = "P" + code, Nombre = "Producto " + code, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 50m, PrecioVenta = precio, PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        ctx.Productos.Add(prod);
        await ctx.SaveChangesAsync();
        ctx.ProductosPrecios.Add(new ProductoPrecioLista
        {
            ProductoId = prod.Id, ListaId = lista.Id, VigenciaDesde = DateTime.UtcNow.AddDays(-1), Costo = 50m, Precio = precio,
            MargenValor = precio - 50m, MargenPorcentaje = 100m, EsVigente = true, EsManual = true, CreadoPor = "seed"
        });
        await ctx.SaveChangesAsync();
        return (lista.Id, prod.Id);
    }

    private static (string, string)[] FormSimular(string nombre, int listaId, int productoId) => new[]
    {
        ("Nombre", nombre), ("TipoCambio", nameof(TipoCambio.PorcentajeSobrePrecioActual)),
        ("TipoAplicacion", nameof(TipoAplicacion.Aumento)), ("ValorCambio", "10"),
        ("ListasIds", listaId.ToString()), ("ProductoIdsText", productoId.ToString())
    };

    private async Task<PriceChangeBatch> LeerBatchAsync(string nombre)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.PriceChangeBatches.AsNoTracking().FirstAsync(b => b.Nombre == nombre);
    }

    private static string RowVersion(PriceChangeBatch b) => Convert.ToBase64String(b.RowVersion);

    private async Task<PriceChangeBatch> SimularAsync(HttpClient client, string nombre, int listaId, int productoId)
    {
        var r = await PostFormAsync(client, "/CambiosPrecios/Simular", FormSimular(nombre, listaId, productoId));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return await LeerBatchAsync(nombre);
    }

    [Fact]
    public async Task UnMismoUsuarioConLosTresPermisos_CompletaElCircuitoYQuedaAuditado()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync(100m);
        var nombre = "R14-circuito-" + Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosCambioRapidoUserId);

        var simulado = await SimularAsync(client, nombre, listaId, productoId);
        Assert.Equal(EstadoBatch.Simulado, simulado.Estado);
        Assert.False(string.IsNullOrWhiteSpace(simulado.SolicitadoPor));
        Assert.Equal(1, simulado.CantidadProductos);

        var aprobar = await PostFormAsync(client, $"/CambiosPrecios/Aprobar/{simulado.Id}",
            ("rowVersion", RowVersion(simulado)), ("notas", "Aprobado por el mismo usuario"));
        Assert.Equal(HttpStatusCode.Redirect, aprobar.StatusCode);
        var aprobado = await LeerBatchAsync(nombre);
        Assert.Equal(EstadoBatch.Aprobado, aprobado.Estado);

        var aplicar = await PostFormAsync(client, $"/CambiosPrecios/AplicarConfirmed/{aprobado.Id}",
            ("rowVersion", RowVersion(aprobado)));
        Assert.Equal(HttpStatusCode.Redirect, aplicar.StatusCode);
        var aplicado = await LeerBatchAsync(nombre);

        Assert.Equal(EstadoBatch.Aplicado, aplicado.Estado);
        // Auditoría: se registra quién simuló, aprobó y aplicó, y cuándo; que sea la misma persona no la borra.
        Assert.False(string.IsNullOrWhiteSpace(aplicado.SolicitadoPor));
        Assert.Equal(aplicado.SolicitadoPor, aplicado.AprobadoPor);
        Assert.Equal(aplicado.SolicitadoPor, aplicado.AplicadoPor);
        Assert.NotNull(aplicado.FechaAprobacion);
        Assert.NotNull(aplicado.FechaAplicacion);
        Assert.True(aplicado.FechaSolicitud <= aplicado.FechaAprobacion);
        Assert.True(aplicado.FechaAprobacion <= aplicado.FechaAplicacion);
        Assert.Equal("Aprobado por el mismo usuario", aplicado.Notas);

        await using var ctx = await NuevoContextoAsync();
        var vigente = await ctx.ProductosPrecios.AsNoTracking()
            .SingleAsync(p => p.ProductoId == productoId && p.ListaId == listaId && p.EsVigente);
        Assert.Equal(110m, vigente.Precio);
        Assert.Equal(aplicado.Id, vigente.BatchId);
        Assert.Equal(aplicado.AplicadoPor, vigente.CreadoPor);
        // El historial conserva el precio anterior cerrado.
        Assert.Equal(1, await ctx.ProductosPrecios.CountAsync(p => p.ProductoId == productoId && p.ListaId == listaId && !p.EsVigente));
    }

    [Fact]
    public async Task SinSimular_NoSePuedeCrearUnLote_PorPostDirecto()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync();
        var nombre = "R14-sin-simular-" + Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosAprobarAplicarUserId);

        var r = await PostFormAsync(client, "/CambiosPrecios/Simular", FormSimular(nombre, listaId, productoId));

        AssertDenegadoPorPermiso(r);
        await using var ctx = await NuevoContextoAsync();
        Assert.False(await ctx.PriceChangeBatches.AnyAsync(b => b.Nombre == nombre));
    }

    [Fact]
    public async Task SinAprobar_NoSePuedeAprobarRechazarNiCancelar_PorPostDirecto()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync();
        var nombre = "R14-sin-aprobar-" + Guid.NewGuid().ToString("N")[..8];
        using var completo = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosCambioRapidoUserId);
        var simulado = await SimularAsync(completo, nombre, listaId, productoId);

        using var sinAprobar = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosSimularAplicarUserId);
        AssertDenegadoPorPermiso(await PostFormAsync(sinAprobar, $"/CambiosPrecios/Aprobar/{simulado.Id}", ("rowVersion", RowVersion(simulado))));
        AssertDenegadoPorPermiso(await PostFormAsync(sinAprobar, $"/CambiosPrecios/Rechazar/{simulado.Id}", ("rowVersion", RowVersion(simulado)), ("motivo", "no")));
        AssertDenegadoPorPermiso(await PostFormAsync(sinAprobar, $"/CambiosPrecios/Cancelar/{simulado.Id}", ("rowVersion", RowVersion(simulado))));

        var despues = await LeerBatchAsync(nombre);
        Assert.Equal(EstadoBatch.Simulado, despues.Estado);
        Assert.Null(despues.AprobadoPor);
    }

    [Fact]
    public async Task SinAplicar_NoSePuedeAplicarUnLoteAprobado_PorPostDirecto_NiCambiaElPrecio()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync(100m);
        var nombre = "R14-sin-aplicar-" + Guid.NewGuid().ToString("N")[..8];
        using var sinAplicar = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosSimularAprobarUserId);

        // Con simular + aprobar sí puede llegar hasta el estado Aprobado (mismo usuario) ...
        var simulado = await SimularAsync(sinAplicar, nombre, listaId, productoId);
        var aprobar = await PostFormAsync(sinAplicar, $"/CambiosPrecios/Aprobar/{simulado.Id}", ("rowVersion", RowVersion(simulado)));
        Assert.Equal(HttpStatusCode.Redirect, aprobar.StatusCode);
        var aprobado = await LeerBatchAsync(nombre);
        Assert.Equal(EstadoBatch.Aprobado, aprobado.Estado);

        // ... pero no aplicarlo.
        AssertDenegadoPorPermiso(await PostFormAsync(sinAplicar, $"/CambiosPrecios/AplicarConfirmed/{aprobado.Id}", ("rowVersion", RowVersion(aprobado))));
        AssertDenegadoPorPermiso(await PostFormAsync(sinAplicar, $"/CambiosPrecios/RevertirConfirmed/{aprobado.Id}", ("rowVersion", RowVersion(aprobado)), ("motivo", "x")));

        Assert.Equal(EstadoBatch.Aprobado, (await LeerBatchAsync(nombre)).Estado);
        await using var ctx = await NuevoContextoAsync();
        var vigente = await ctx.ProductosPrecios.AsNoTracking().SingleAsync(p => p.ProductoId == productoId && p.ListaId == listaId && p.EsVigente);
        Assert.Equal(100m, vigente.Precio);
    }

    [Fact]
    public async Task ConSimularYAplicarSinAprobar_NoSePuedeAplicarUnLoteSimulado()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync(100m);
        var nombre = "R14-apl-sin-apr-" + Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosSimularAplicarUserId);
        var simulado = await SimularAsync(client, nombre, listaId, productoId);

        // Tiene permiso de aplicar, pero el lote no está aprobado y el usuario no puede aprobarlo: el servicio lo rechaza.
        var r = await PostFormAsync(client, $"/CambiosPrecios/AplicarConfirmed/{simulado.Id}", ("rowVersion", RowVersion(simulado)));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/Aplicar/", r.Headers.Location?.ToString() ?? string.Empty);
        var despues = await LeerBatchAsync(nombre);
        Assert.Equal(EstadoBatch.Simulado, despues.Estado);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(100m, (await ctx.ProductosPrecios.AsNoTracking().SingleAsync(p => p.ProductoId == productoId && p.EsVigente)).Precio);
    }

    [Fact]
    public async Task Aplicar_ElFormularioNoPrecargaUnaVigenciaQueElNavegadorRechaza()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync(100m);
        var nombre = "D5-form-" + Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosCambioRapidoUserId);
        var simulado = await SimularAsync(client, nombre, listaId, productoId);
        await PostFormAsync(client, $"/CambiosPrecios/Aprobar/{simulado.Id}", ("rowVersion", RowVersion(simulado)));

        var html = await (await client.GetAsync($"/CambiosPrecios/Aplicar/{simulado.Id}")).Content.ReadAsStringAsync();
        var campo = Regex.Match(html, "<input[^>]*name=\"FechaVigencia\"[^>]*>").Value;

        Assert.NotEmpty(campo);
        // Vacío = «al confirmar». Precargado con milisegundos, datetime-local falla por stepMismatch y no envía.
        Assert.Matches("value=\"\"", campo);
        Assert.DoesNotContain("data-fecha", campo);
    }

    [Fact]
    public async Task Aplicar_LaVigenciaEscritaEnHoraArgentina_SeGuardaComoInstanteUtc()
    {
        var (listaId, productoId) = await SembrarProductoConPrecioAsync(100m);
        var nombre = "D5-hora-" + Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosCambioRapidoUserId);
        var simulado = await SimularAsync(client, nombre, listaId, productoId);
        await PostFormAsync(client, $"/CambiosPrecios/Aprobar/{simulado.Id}", ("rowVersion", RowVersion(simulado)));
        var aprobado = await LeerBatchAsync(nombre);
        var escrita = TheBuryProject.Services.FechaComercial.AComercial(DateTime.UtcNow).AddMinutes(-5);
        escrita = escrita.AddTicks(-(escrita.Ticks % TimeSpan.TicksPerMinute));

        var aplicar = await PostFormAsync(client, $"/CambiosPrecios/AplicarConfirmed/{aprobado.Id}",
            ("rowVersion", RowVersion(aprobado)),
            ("fechaVigencia", escrita.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(HttpStatusCode.Redirect, aplicar.StatusCode);
        var esperada = TheBuryProject.Services.FechaComercial.AUtc(escrita);
        Assert.Equal(esperada, (await LeerBatchAsync(nombre)).FechaVigencia);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(esperada, (await ctx.ProductosPrecios.AsNoTracking()
            .SingleAsync(p => p.ProductoId == productoId && p.ListaId == listaId && p.EsVigente)).VigenciaDesde);
    }
}
