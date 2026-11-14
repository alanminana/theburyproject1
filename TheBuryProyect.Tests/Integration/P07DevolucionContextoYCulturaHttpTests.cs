using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// P-07 (recorridos integrales en Docker). Dos defectos reales hallados en la instalación descartable:
/// <list type="bullet">
/// <item><b>D-02</b>: el contexto del modal de devolución rápida ofrecía siempre lo vendido (<c>cantidadDisponible</c>),
/// sin descontar lo ya devuelto; tras devolver todo el modal seguía dejando elegir unidades que el servidor rechazaba
/// («Disponible: 0»). Ahora descuenta con el mismo criterio que <c>CrearDevolucionAsync</c> (pendiente/aprobada/completada
/// cuentan; rechazada y cancelada no).</item>
/// <item><b>D-01</b>: en el contenedor Linux no hay cultura del sistema y .NET usa la invariante: los importes salían como
/// «¤363,000.00». La aplicación fija es-AR (<c>UseRequestLocalization</c> sin proveedores de cliente).</item>
/// </list>
/// </summary>
[Collection("HttpIntegration.1")]
public sealed class P07DevolucionContextoYCulturaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public P07DevolucionContextoYCulturaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record VentaSembrada(int VentaId, int ClienteId, int ProductoId);

    /// <summary>Venta confirmada de contado: 2 unidades × $181.500 = $363.000.</summary>
    private async Task<VentaSembrada> SembrarVentaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, ConcCurrentUser.Usuario);
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var cliente = new Cliente
        {
            Nombre = "P07", Apellido = "Devol" + sufijo, TipoDocumento = "DNI", NumeroDocumento = sufijo,
            Email = $"{sufijo}@test.com", Activo = true, Telefono = "0000000000", Domicilio = "QA 1"
        };
        var cat = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(cliente, cat, marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 100000m, PrecioVenta = 181500m, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = "VP07" + sufijo, ClienteId = cliente.Id, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, AperturaCajaId = apertura, Subtotal = 363000m, Total = 363000m,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = 181500m, Descuento = 0m, Subtotal = 363000m,
                    CostoUnitarioAlMomento = 100000m, CostoTotalAlMomento = 200000m
                }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return new VentaSembrada(venta.Id, cliente.Id, producto.Id);
    }

    private async Task<Devolucion> CrearDevolucionAsync(VentaSembrada v, int cantidad)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "P07",
                TipoResolucion = TipoResolucionDevolucion.NotaCredito
            },
            new List<DevolucionDetalle>
            {
                new() { ProductoId = v.ProductoId, Cantidad = cantidad, AccionRecomendada = AccionProducto.ReintegrarStock }
            });
    }

    private async Task<JsonElement> ContextoAsync(int ventaId)
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();
        var resp = await client.GetAsync($"/Devolucion/GetQuickCreateContext?ventaId={ventaId}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.Clone().GetProperty("venta");
    }

    [Fact(DisplayName = "REGRESION [P07 D-02]: el modal de devolucion ofrece lo vendido menos lo ya devuelto y se cierra cuando no queda nada")]
    public async Task Contexto_DescuentaLoYaDevuelto_Y_NoOfreceDevolverSiNoQuedaNada()
    {
        var v = await SembrarVentaAsync();

        var inicial = await ContextoAsync(v.VentaId);
        Assert.Equal(2, inicial.GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());
        Assert.True(inicial.GetProperty("puedeDevolver").GetBoolean());

        await CrearDevolucionAsync(v, 1);
        var trasUna = await ContextoAsync(v.VentaId);
        Assert.Equal(1, trasUna.GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());
        Assert.True(trasUna.GetProperty("puedeDevolver").GetBoolean());

        await CrearDevolucionAsync(v, 1);
        var trasDos = await ContextoAsync(v.VentaId);
        Assert.Equal(0, trasDos.GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());
        Assert.False(trasDos.GetProperty("puedeDevolver").GetBoolean());
    }

    [Fact(DisplayName = "REGRESION [P07 D-02]: una devolucion rechazada o cancelada no resta unidades disponibles (mismo criterio que la validacion)")]
    public async Task Contexto_NoDescuentaDevolucionesRechazadasNiCanceladas()
    {
        var v = await SembrarVentaAsync();
        var d1 = await CrearDevolucionAsync(v, 1);
        var d2 = await CrearDevolucionAsync(v, 1);
        Assert.Equal(0, (await ContextoAsync(v.VentaId)).GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
            await svc.RechazarDevolucionAsync(d1.Id, "no procede", d1.RowVersion);
            await svc.CancelarDevolucionAsync(d2.Id, "el cliente desistio", d2.RowVersion);
        }

        var ctx = await ContextoAsync(v.VentaId);
        Assert.Equal(2, ctx.GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());
        Assert.True(ctx.GetProperty("puedeDevolver").GetBoolean());
    }

    [Fact(DisplayName = "REGRESION [P07 D-01]: los importes del servidor salen en es-AR aunque el SO del contenedor no tenga cultura")]
    public async Task Importes_SeFormateanEnEsAr_NoConLaCulturaInvariante()
    {
        var v = await SembrarVentaAsync();
        var venta = await ContextoAsync(v.VentaId);

        var total = venta.GetProperty("totalDisplay").GetString()!;
        Assert.DoesNotContain("¤", total);                 // «¤» = signo de moneda de la cultura invariante
        Assert.Contains("363.000,00", total);                   // es-AR: punto de miles, coma decimal
        var unitario = venta.GetProperty("items")[0].GetProperty("precioUnitarioDisplay").GetString()!;
        Assert.Contains("181.500,00", unitario);
    }

    [Fact(DisplayName = "REGRESION [P07 D-01]: la cultura del navegador (Accept-Language) no cambia el formato del servidor")]
    public async Task Importes_NoDependenDelAcceptLanguageDelNavegador()
    {
        var v = await SembrarVentaAsync();
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

        var resp = await client.GetAsync($"/Devolucion/GetQuickCreateContext?ventaId={v.VentaId}");
        resp.EnsureSuccessStatusCode();
        var cuerpo = await resp.Content.ReadAsStringAsync();

        Assert.Contains("363.000,00", cuerpo);
        Assert.DoesNotContain("363,000.00", cuerpo);
    }
}
