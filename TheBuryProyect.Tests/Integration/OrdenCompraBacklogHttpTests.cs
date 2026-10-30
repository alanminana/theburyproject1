using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// COM-S-007 (backlog de la auditoría QA): marcar una orden de compra como Recibida desde CambiarEstado alcanzaba con
/// ordenescompra.update, saltándose el permiso de recepción (ordenescompra.receive) que sí exige Recepcionar.
/// </summary>
[Collection("HttpIntegration")]
public class OrdenCompraBacklogHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrdenCompraBacklogHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<int> SeedOrdenConfirmadaAsync(EstadoOrdenCompra estado = EstadoOrdenCompra.Confirmada, int recibidas = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var proveedor = new Proveedor { Cuit = sufijo + "123", RazonSocial = "Proveedor-" + sufijo, Activo = true };
        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        context.AddRange(proveedor, categoria, marca);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 15m, PorcentajeIVA = 21m, Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var orden = new OrdenCompra
        {
            Numero = $"OC-{DateTime.UtcNow.Year}-{sufijo}", ProveedorId = proveedor.Id, Estado = estado,
            FechaEmision = DateTime.UtcNow, RowVersion = new byte[8],
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = 5, PrecioUnitario = 100m, Subtotal = 500m, CantidadRecibida = recibidas }
            }
        };
        context.OrdenesCompra.Add(orden);
        await context.SaveChangesAsync();
        return orden.Id;
    }

    private async Task<(EstadoOrdenCompra Estado, DateTime? Recepcion)> LeerAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var o = await context.OrdenesCompra.AsNoTracking().SingleAsync(x => x.Id == id);
        return (o.Estado, o.FechaRecepcion);
    }

    private static async Task<(string Html, string Token)> DetailsAsync(HttpClient client, int id)
    {
        var response = await client.GetAsync($"/OrdenCompra/Details/{id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return (html, WebUtility.HtmlDecode(m.Groups[1].Value));
    }

    private static Task<HttpResponseMessage> CambiarEstadoAsync(HttpClient client, string token, int id, EstadoOrdenCompra nuevo) =>
        client.PostAsync("/OrdenCompra/CambiarEstado", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = id.ToString(),
            ["nuevoEstado"] = ((int)nuevo).ToString()
        }));

    [Fact]
    public async Task CambiarEstado_ARecibida_SinPermisoDeRecepcion_NoCambiaYLaVistaNoLoOfrece()
    {
        await _factory.SeedUserWithOrdenCompraSinRecepcionAsync();
        var id = await SeedOrdenConfirmadaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.OrdenCompraSinRecepcionUserId);
        var (html, token) = await DetailsAsync(client, id);

        var select = Regex.Match(html, "<select\\b[^>]*\\bid=\"select-estado\"[^>]*>.*?</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.DoesNotContain($"value=\"{(int)EstadoOrdenCompra.Recibida}\"", select);

        var response = await CambiarEstadoAsync(client, token, id, EstadoOrdenCompra.Recibida);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found, $"Esperaba redirect y fue {(int)response.StatusCode}.");
        var (estado, recepcion) = await LeerAsync(id);
        Assert.Equal(EstadoOrdenCompra.Confirmada, estado);
        Assert.Null(recepcion);
    }

    [Fact]
    public async Task CambiarEstado_AEnTransito_SinPermisoDeRecepcion_SigueFuncionando()
    {
        await _factory.SeedUserWithOrdenCompraSinRecepcionAsync();
        var id = await SeedOrdenConfirmadaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.OrdenCompraSinRecepcionUserId);
        var (_, token) = await DetailsAsync(client, id);

        await CambiarEstadoAsync(client, token, id, EstadoOrdenCompra.EnTransito);

        Assert.Equal(EstadoOrdenCompra.EnTransito, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task CambiarEstado_ARecibida_AunConPermisoDeRecepcion_NoSeOfreceNiSeAplica()
    {
        // Recibida se alcanza registrando la recepcion (Recepcionar), que es lo que ingresa el stock; CambiarEstado solo
        // la daba por recibida sin stock. El selector no la ofrece y un POST manual tampoco la aplica.
        await _factory.SeedUserWithOrdenCompraConRecepcionAsync();
        var id = await SeedOrdenConfirmadaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.OrdenCompraConRecepcionUserId);
        var (html, token) = await DetailsAsync(client, id);
        var select = Regex.Match(html, "<select\\b[^>]*\\bid=\"select-estado\"[^>]*>.*?</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.DoesNotContain($"value=\"{(int)EstadoOrdenCompra.Recibida}\"", select);
        Assert.Contains("Recepcionar", html);

        await CambiarEstadoAsync(client, token, id, EstadoOrdenCompra.Recibida);

        var (estado, recepcion) = await LeerAsync(id);
        Assert.Equal(EstadoOrdenCompra.Confirmada, estado);
        Assert.Null(recepcion);
    }

    [Fact]
    public async Task CambiarEstado_ACancelada_SinPermisoDeCancelacion_NoCambiaYLaVistaNoLoOfrece()
    {
        // H9-M6: cancelar exige ordenescompra.cancel (como Delete); con solo update alcanzaba.
        await _factory.SeedUserWithOrdenCompraSinRecepcionAsync();
        var id = await SeedOrdenConfirmadaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.OrdenCompraSinRecepcionUserId);
        var (html, token) = await DetailsAsync(client, id);
        var select = Regex.Match(html, "<select\\b[^>]*\\bid=\"select-estado\"[^>]*>.*?</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.DoesNotContain($"value=\"{(int)EstadoOrdenCompra.Cancelada}\"", select);

        await CambiarEstadoAsync(client, token, id, EstadoOrdenCompra.Cancelada);

        Assert.Equal(EstadoOrdenCompra.Confirmada, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task Cancelar_OrdenParcialmenteRecibida_ConservaRecepcionYDejaAuditoria_YNoSePuedeEliminar()
    {
        await _factory.SeedUserWithOrdenCompraCancelaAsync();
        var id = await SeedOrdenConfirmadaAsync(EstadoOrdenCompra.EnTransito, recibidas: 2);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.OrdenCompraCancelaUserId);
        var (_, token) = await DetailsAsync(client, id);

        await CambiarEstadoAsync(client, token, id, EstadoOrdenCompra.Cancelada);

        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();
            var orden = await context.OrdenesCompra.Include(o => o.Detalles).AsNoTracking().SingleAsync(o => o.Id == id);
            Assert.Equal(EstadoOrdenCompra.Cancelada, orden.Estado);
            Assert.Equal(2, orden.Detalles.Single().CantidadRecibida);
            var evento = await context.Set<SeguridadEventoAuditoria>().AsNoTracking()
                .SingleAsync(e => e.Accion == "CancelarOrdenCompra" && e.Entidad == $"OrdenCompra:{id}");
            Assert.Equal("Compras", evento.Modulo);
            Assert.Contains("Recibido=2/5", evento.Detalle);
            Assert.Contains("sin reversión", evento.Detalle);
        }

        // Eliminarla liberaría su número (índice único filtrado) y el Kardex ("Orden de Compra OC-...") quedaría ambiguo.
        var (_, token2) = await DetailsAsync(client, id);
        var delete = await client.PostAsync($"/OrdenCompra/Delete/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token2
        }));
        Assert.True(delete.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found);
        using var scope2 = _factory.Services.CreateScope();
        var cf = scope2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await cf.CreateDbContextAsync();
        Assert.False((await ctx.OrdenesCompra.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == id)).IsDeleted);
    }
}
