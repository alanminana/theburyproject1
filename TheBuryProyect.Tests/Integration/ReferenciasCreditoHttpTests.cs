using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02 por el pipeline HTTP real (antiforgery + permisos + binding + servicios): accesos directos sin pasar por la UI.
/// El usuario autenticado de las pruebas se llama siempre «testuser»: las ventas «de otro usuario» se siembran con
/// CreatedBy distinto. Cubre alta a crédito personal, autorización de la excepción (nunca el creador, nunca sin permiso, nunca
/// dos veces), ids inexistentes, la configuración del mínimo y el endpoint de aptitud del Cotizador.
/// </summary>
[Collection("HttpIntegration.3")]
public class ReferenciasCreditoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 52000;
    private readonly CustomWebApplicationFactory _factory;

    public ReferenciasCreditoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await using var ctx = await NuevoContextoAsync();
        if (!await ctx.AperturasCaja.AnyAsync(a => a.UsuarioApertura == "testuser" && !a.Cerrada && !a.IsDeleted))
        {
            var n = Interlocked.Increment(ref _counter);
            var caja = new Caja { Codigo = $"R{n}", Nombre = $"Caja REF {n}", RowVersion = new byte[8] };
            ctx.Cajas.Add(caja);
            await ctx.SaveChangesAsync();
            ctx.AperturasCaja.Add(new AperturaCaja
            {
                CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false, RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    /// <summary>Configuración única: sin documentación/cupo/mora exigidos; solo la regla de referencias con el mínimo pedido.</summary>
    private async Task ConfigurarAsync(int minimo)
    {
        await using var ctx = await NuevoContextoAsync();
        var config = await ctx.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        if (config == null)
        {
            config = new ConfiguracionCredito();
            ctx.ConfiguracionesCredito.Add(config);
        }
        config.ReferenciasVerificadasMinimas = minimo;
        config.ValidarDocumentacion = false;
        config.ValidarLimiteCredito = false;
        config.ValidarMora = false;
        // Credito personal con tasa global (sin ella ConfigurarVenta responde antes de llegar al gate de referencias).
        var medio = await ctx.ConfiguracionesPago.FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal);
        if (medio == null)
        {
            medio = new ConfiguracionPago { TipoPago = TipoPago.CreditoPersonal, Nombre = "Crédito personal", Activo = true };
            ctx.ConfiguracionesPago.Add(medio);
        }
        medio.TasaInteresMensualCreditoPersonal = 5m;
        await ctx.SaveChangesAsync();
        var preset = await ctx.PuntajesCreditoLimite.FirstOrDefaultAsync(p => p.Puntaje == 1);
        if (preset != null)
        {
            preset.LimiteMonto = 10_000_000m;
            await ctx.SaveChangesAsync();
        }
    }

    private async Task<(int ClienteId, int ProductoId)> SeedAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var cat = new Categoria { Codigo = $"CR{n}", Nombre = $"CatR{n}" };
        var mar = new Marca { Codigo = $"MR{n}", Nombre = $"MarR{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = $"R{n}", Nombre = $"ProdR{n}", PrecioVenta = 1_000m, StockActual = 50, CategoriaId = cat.Id, MarcaId = mar.Id
        };
        var cli = new Cliente
        {
            Nombre = "Ref", Apellido = "Http", TipoDocumento = "DNI", NumeroDocumento = $"6{n}", Telefono = "3415551000",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, PuntajeCliente = 1, Activo = true, CuilCuit = "20123456786",
            SituacionCrediticiaBcra = 1, SituacionCrediticiaConsultaOk = true,
            SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1),
            SituacionCrediticiaBcraUltimoExito = 1, SituacionCrediticiaUltimoExitoUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Productos.Add(prod);
        ctx.Clientes.Add(cli);
        await ctx.SaveChangesAsync();
        return (cli.Id, prod.Id);
    }

    private async Task AgregarReferenciaVerificadaAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        ctx.ClienteReferencias.Add(new ClienteReferencia
        {
            ClienteId = clienteId, Tipo = TipoReferenciaCliente.Personal, Nombre = "Ref verificada", Telefono = "3414440001",
            TelefonoNormalizado = "3414440001", EstadoVerificacion = EstadoVerificacionReferencia.Verificada
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<JsonElement> CrearVentaCreditoAsync(HttpClient client, string token, int clienteId, int productoId)
    {
        var campos = new Dictionary<string, string>
        {
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            ["ClienteId"] = clienteId.ToString(),
            ["FechaVenta"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["TipoPago"] = "CreditoPersonal",
            ["Detalles[0].ProductoId"] = productoId.ToString(),
            ["Detalles[0].Cantidad"] = "1",
            ["Detalles[0].PrecioUnitario"] = "1000",
            ["Detalles[0].Subtotal"] = "1000",
            ["__RequestVerificationToken"] = token
        };
        var resp = await client.PostAsync("/Venta/CreateAjax", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> AutorizarAsync(HttpClient client, string token, int ventaId, string? motivo, bool conToken = true)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("id", ventaId.ToString())
        };
        if (motivo != null) campos.Add(new("motivo", motivo));
        if (conToken) campos.Add(new("__RequestVerificationToken", token));
        return await client.PostAsync($"/Venta/Autorizar/{ventaId}", new FormUrlEncodedContent(campos));
    }

    private async Task<Venta> VentaAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == id);
    }

    private async Task<int> EventosDeAutorizacionAsync(int ventaId)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.SeguridadEventosAuditoria.CountAsync(e => e.Accion == "AutorizarExcepcionReferencias" && e.Entidad == $"Venta:{ventaId}");
    }

    /// <summary>Venta ya pendiente de autorización por referencias, creada por «otro» usuario (CreatedBy distinto de testuser).</summary>
    private async Task<int> SeedVentaPendienteDeOtroUsuarioAsync(int clienteId, int productoId)
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var venta = new Venta
        {
            Numero = $"VTA-REF-{n}", ClienteId = clienteId, Estado = EstadoVenta.PendienteFinanciacion,
            TipoPago = TipoPago.CreditoPersonal, FechaVenta = DateTime.UtcNow, Total = 1_000m,
            RequiereAutorizacion = true, EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion,
            RazonesAutorizacionJson = "[{\"Tipo\":7,\"Descripcion\":\"Referencias verificadas insuficientes: 0 de 1 requeridas\"}]",
            CreatedBy = "vendedor.otro"
        };
        venta.Detalles.Add(new VentaDetalle { ProductoId = productoId, Cantidad = 1, PrecioUnitario = 1_000m, Subtotal = 1_000m });
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return venta.Id;
    }

    // ------------------------------------------------------------------ pruebas

    [Fact]
    public async Task CreateAjax_CreditoPersonalSinReferenciasVerificadas_QuedaPendienteDeAutorizacion()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        var json = await CrearVentaCreditoAsync(client, token, clienteId, productoId);

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        var venta = await VentaAsync(json.GetProperty("ventaId").GetInt32());
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(venta.RequiereAutorizacion);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.Equal("testuser", venta.CreatedBy);
    }

    [Fact]
    public async Task CreateAjax_ConReferenciaVerificada_NoPideAutorizacion()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        await AgregarReferenciaVerificadaAsync(clienteId);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        var json = await CrearVentaCreditoAsync(client, token, clienteId, productoId);

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        var venta = await VentaAsync(json.GetProperty("ventaId").GetInt32());
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
    }

    [Fact]
    public async Task Autorizar_ElCreadorPorPostDirecto_NoPuedeAutorizarseAunSiendoSuperAdmin()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var json = await CrearVentaCreditoAsync(client, token, clienteId, productoId);
        var ventaId = json.GetProperty("ventaId").GetInt32();

        var resp = await AutorizarAsync(client, token, ventaId, "Me lo autorizo");

        Assert.True((int)resp.StatusCode is >= 300 and < 400, $"HTTP {(int)resp.StatusCode}");
        var venta = await VentaAsync(ventaId);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.Equal(0, await EventosDeAutorizacionAsync(ventaId));
    }

    [Fact]
    public async Task Autorizar_ElSegundoUsuario_AutorizaUnaSolaVezConAuditoria_YUnReintentoNoDuplica()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var ventaId = await SeedVentaPendienteDeOtroUsuarioAsync(clienteId, productoId);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        var primera = await AutorizarAsync(client, token, ventaId, "Cliente conocido");
        var reintento = await AutorizarAsync(client, token, ventaId, "Reintento del mismo POST");

        Assert.True((int)primera.StatusCode is >= 300 and < 400);
        Assert.True((int)reintento.StatusCode is >= 300 and < 400);
        var venta = await VentaAsync(ventaId);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal("testuser", venta.UsuarioAutoriza);
        Assert.Equal("Cliente conocido", venta.MotivoAutorizacion);
        Assert.Equal("vendedor.otro", venta.CreatedBy);
        Assert.Equal(1, await EventosDeAutorizacionAsync(ventaId));
    }

    [Fact]
    public async Task Autorizar_SinMotivo_OSinTokenAntiforgery_NoAutoriza()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var ventaId = await SeedVentaPendienteDeOtroUsuarioAsync(clienteId, productoId);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        await AutorizarAsync(client, token, ventaId, motivo: "   ");
        await AutorizarAsync(client, token, ventaId, motivo: null);
        var sinToken = await AutorizarAsync(client, token, ventaId, "Motivo", conToken: false);

        Assert.True(sinToken.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"HTTP {(int)sinToken.StatusCode}");
        var venta = await VentaAsync(ventaId);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
    }

    [Fact]
    public async Task Autorizar_UnUsuarioSinPermiso_EsDenegadoYLaVentaNoCambia()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var ventaId = await SeedVentaPendienteDeOtroUsuarioAsync(clienteId, productoId);
        var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);
        var token = await TokenAsync(client);

        var resp = await AutorizarAsync(client, token, ventaId, "Sin permiso");

        var denegado = resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || ((int)resp.StatusCode is >= 300 and < 400
                && (resp.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.True(denegado, $"HTTP {(int)resp.StatusCode} {resp.Headers.Location}");
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(ventaId)).EstadoAutorizacion);
        Assert.Equal(0, await EventosDeAutorizacionAsync(ventaId));
    }

    [Fact]
    public async Task Autorizar_UnaVentaInexistente_NoRompeNiRegistraNada()
    {
        await ConfigurarAsync(minimo: 1);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        var resp = await AutorizarAsync(client, token, 99_999_999, "Motivo");

        Assert.True((int)resp.StatusCode is >= 300 and < 400, $"HTTP {(int)resp.StatusCode}");
        Assert.Equal(0, await EventosDeAutorizacionAsync(99_999_999));
    }

    [Fact]
    public async Task Autorizar_UnaVentaNoCreditoOSinRazonDeReferencias_NoUsaLaRegla()
    {
        // Una venta pendiente por OTRO motivo (mora no bloqueante) la autoriza el mismo flujo de siempre: no se exige el segundo
        // usuario estricto ni se escribe el evento de referencias.
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        var ventaId = await SeedVentaPendienteDeOtroUsuarioAsync(clienteId, productoId);
        await using (var ctx = await NuevoContextoAsync())
        {
            var venta = await ctx.Ventas.FirstAsync(v => v.Id == ventaId);
            venta.RazonesAutorizacionJson = "[{\"Tipo\":2,\"Descripcion\":\"Mora\"}]";
            venta.CreatedBy = "testuser";
            await ctx.SaveChangesAsync();
        }
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        await AutorizarAsync(client, token, ventaId, "Autorizo por mora");

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(ventaId)).EstadoAutorizacion);
        Assert.Equal(0, await EventosDeAutorizacionAsync(ventaId));
    }

    [Fact]
    public async Task ConfigurarVenta_SiLasReferenciasDejaronDeAlcanzar_RespondeConflictoYNoConfiguraElCredito()
    {
        await ConfigurarAsync(minimo: 1);
        var (clienteId, productoId) = await SeedAsync();
        await AgregarReferenciaVerificadaAsync(clienteId);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var json = await CrearVentaCreditoAsync(client, token, clienteId, productoId);
        var ventaId = json.GetProperty("ventaId").GetInt32();
        var creditoId = json.GetProperty("creditoId").GetInt32();
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, (await VentaAsync(ventaId)).EstadoAutorizacion);

        // Con las referencias vigentes el gate de referencias no interviene (puede fallar por otros motivos, nunca por este).
        var control = await client.GetAsync($"/Credito/ConfigurarVenta?id={creditoId}&ventaId={ventaId}&embedded=true");
        Assert.DoesNotContain("Referencias verificadas insuficientes", await control.Content.ReadAsStringAsync());

        // La referencia deja de valer después de crear la venta.
        await using (var ctx = await NuevoContextoAsync())
        {
            (await ctx.ClienteReferencias.FirstAsync(r => r.ClienteId == clienteId)).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var bloqueado = await client.GetAsync($"/Credito/ConfigurarVenta?id={creditoId}&ventaId={ventaId}&embedded=true");

        Assert.Equal(HttpStatusCode.Conflict, bloqueado.StatusCode);
        Assert.Contains("Referencias verificadas insuficientes", await bloqueado.Content.ReadAsStringAsync());
        await using var ctx2 = await NuevoContextoAsync();
        Assert.Null((await ctx2.Ventas.AsNoTracking().FirstAsync(v => v.Id == ventaId)).FechaConfiguracionCredito);
    }

    [Fact]
    public async Task AptitudDelCotizador_InformaReferenciasVerificadasContraElMinimo()
    {
        await ConfigurarAsync(minimo: 2);
        var (clienteId, _) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();

        var antes = JsonDocument.Parse(await client.GetStringAsync($"/api/cotizacion/aptitud-credito?clienteId={clienteId}&monto=1000")).RootElement;
        await AgregarReferenciaVerificadaAsync(clienteId);
        var uno = JsonDocument.Parse(await client.GetStringAsync($"/api/cotizacion/aptitud-credito?clienteId={clienteId}&monto=1000")).RootElement;

        Assert.Equal("RequiereAutorizacion", antes.GetProperty("estado").GetString());
        var r0 = antes.GetProperty("referencias");
        Assert.True(r0.GetProperty("evaluada").GetBoolean());
        Assert.Equal(2, r0.GetProperty("requeridas").GetInt32());
        Assert.Equal(0, r0.GetProperty("verificadas").GetInt32());
        Assert.False(r0.GetProperty("cumple").GetBoolean());
        Assert.Equal($"/Cliente/Details/{clienteId}#referencias-card", r0.GetProperty("url").GetString());
        Assert.Contains("Referencias verificadas insuficientes: 0 de 2", string.Join("|", antes.GetProperty("motivos").EnumerateArray().Select(m => m.GetString())));
        Assert.Equal(1, uno.GetProperty("referencias").GetProperty("verificadas").GetInt32());
        Assert.False(uno.GetProperty("referencias").GetProperty("cumple").GetBoolean());
    }

    [Fact]
    public async Task Configuracion_ElMinimoFueraDeRango_SeRechazaSinCambiarNada_YCeroLoDesactivaConAuditoria()
    {
        await ConfigurarAsync(minimo: 1);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        Dictionary<string, string> Form(string minimo) => new()
        {
            ["DefaultsGlobales.TasaMensual"] = "0",
            ["DefaultsGlobales.GastosAdministrativos"] = "0",
            ["DefaultsGlobales.MinCuotas"] = "1",
            ["DefaultsGlobales.MaxCuotas"] = "12",
            ["ReferenciasCredito.ReferenciasVerificadasMinimas"] = minimo,
            ["__RequestVerificationToken"] = token
        };

        foreach (var invalido in new[] { "11", "-1", "abc" })
        {
            var rechazo = await client.PostAsync("/ConfiguracionPago/CreditoPersonal", new FormUrlEncodedContent(Form(invalido)));
            Assert.Equal(HttpStatusCode.OK, rechazo.StatusCode); // vuelve al formulario con el error de validación
            await using var ctx = await NuevoContextoAsync();
            Assert.Equal(1, (await ctx.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstAsync()).ReferenciasVerificadasMinimas);
        }

        var ok = await client.PostAsync("/ConfiguracionPago/CreditoPersonal", new FormUrlEncodedContent(Form("0")));
        Assert.True((int)ok.StatusCode is >= 300 and < 400, $"HTTP {(int)ok.StatusCode}");
        await using var ctx2 = await NuevoContextoAsync();
        Assert.Equal(0, (await ctx2.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstAsync()).ReferenciasVerificadasMinimas);
        Assert.True(await ctx2.SeguridadEventosAuditoria.AnyAsync(e => e.Accion == "ReferenciasMinimasCredito" && e.Detalle!.Contains("1 -> 0")));
    }

    [Fact]
    public async Task Configuracion_UnUsuarioSinPermisoDeConfiguracion_NoPuedeCambiarElMinimo()
    {
        await ConfigurarAsync(minimo: 1);
        var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);
        var token = await TokenAsync(client);

        var resp = await client.PostAsync("/ConfiguracionPago/CreditoPersonal", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DefaultsGlobales.TasaMensual"] = "0",
            ["DefaultsGlobales.GastosAdministrativos"] = "0",
            ["DefaultsGlobales.MinCuotas"] = "1",
            ["DefaultsGlobales.MaxCuotas"] = "12",
            ["ReferenciasCredito.ReferenciasVerificadasMinimas"] = "0",
            ["__RequestVerificationToken"] = token
        }));

        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(1, (await ctx.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstAsync()).ReferenciasVerificadasMinimas);
    }
}
