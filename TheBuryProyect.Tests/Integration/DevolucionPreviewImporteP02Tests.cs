using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// P-02 (cierre): el importe que el operador ve en el modal «Crear devolución» (<c>precioUnitario</c>, <c>remanente</c> y <c>cantidadDisponible</c> del contexto, con la regla que aplica el modal) es EXACTAMENTE
/// el que persiste <c>CrearDevolucionAsync</c> en el detalle y el total. Antes el modal mostraba el precio de lista
/// ($3.002,01) aunque lo cobrado fuera menor ($2.851,90). Cubre descuento y recargo del medio de pago, descuento de línea,
/// alícuotas mixtas, devolución parcial y total, y que lista − descuentos + ajuste = total reintegrable.
/// </summary>
[Collection("HttpIntegration.1")]
public sealed class DevolucionPreviewImporteP02Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DevolucionPreviewImporteP02Tests(CustomWebApplicationFactory factory) => _factory = factory;

    /// <summary>Línea sembrada: cantidad, precio de lista, % descuento de línea, subtotal final cobrado y % de IVA.</summary>
    public sealed record Linea(int Cantidad, decimal Precio, decimal DescuentoPct, decimal SubtotalFinal, decimal Iva);

    private sealed record Sembrada(int VentaId, int ClienteId, int[] ProductoIds, decimal Total);

    private async Task<Sembrada> SembrarAsync(decimal total, params Linea[] lineas)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, ConcCurrentUser.Usuario);
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var cliente = new Cliente
        {
            Nombre = "P02", Apellido = "Prev" + sufijo, TipoDocumento = "DNI", NumeroDocumento = sufijo,
            Email = $"{sufijo}@test.com", Activo = true, Telefono = "0000000000", Domicilio = "QA 1"
        };
        var cat = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(cliente, cat, marca);
        await ctx.SaveChangesAsync();

        var productos = lineas.Select((l, i) => new Producto
        {
            Codigo = $"{sufijo}{i}", Nombre = $"Prod-{sufijo}-{i}", CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = l.Precio / 2m, PrecioVenta = l.Precio, PorcentajeIVA = l.Iva, StockActual = 50m, Activo = true
        }).ToList();
        ctx.Productos.AddRange(productos);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = "VP02" + sufijo, ClienteId = cliente.Id, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, AperturaCajaId = apertura,
            Subtotal = lineas.Sum(l => l.SubtotalFinal), Total = total,
            Detalles = lineas.Select((l, i) => new VentaDetalle
            {
                ProductoId = productos[i].Id, Cantidad = l.Cantidad, PrecioUnitario = l.Precio, Descuento = l.DescuentoPct,
                Subtotal = l.SubtotalFinal, SubtotalFinal = l.SubtotalFinal,
                CostoUnitarioAlMomento = l.Precio / 2m, CostoTotalAlMomento = l.Precio / 2m * l.Cantidad
            }).ToList()
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return new Sembrada(venta.Id, cliente.Id, productos.Select(p => p.Id).ToArray(), total);
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

    private static JsonElement Item(JsonElement venta, int productoId)
        => venta.GetProperty("items").EnumerateArray().First(i => i.GetProperty("productoId").GetInt32() == productoId);

    // Misma regla que venta-devolucion-modal.js (importeDeItem): agotar las unidades = remanente; si no, precio unitario x cantidad con tope.
    private static decimal Importe(JsonElement item, int cantidad)
    {
        var remanente = item.GetProperty("remanente").GetDecimal();
        if (cantidad == item.GetProperty("cantidadDisponible").GetInt32()) return remanente;
        return Math.Min(Math.Round(cantidad * item.GetProperty("precioUnitario").GetDecimal(), 2), remanente);
    }

    private async Task<Devolucion> CrearAsync(Sembrada v, int productoId, int cantidad)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "P02 preview",
                TipoResolucion = TipoResolucionDevolucion.NotaCredito
            },
            new List<DevolucionDetalle>
            {
                new() { ProductoId = productoId, Cantidad = cantidad, AccionRecomendada = AccionProducto.ReintegrarStock }
            });
    }

    [Fact(DisplayName = "REGRESION [P-02]: si otra devolucion cambia el remanente tras la vista previa, la confirmacion se rechaza (no se persiste un importe distinto al visto)")]
    public async Task Confirmar_ConImporteEsperadoDistinto_SeRechazaYNoPersiste()
    {
        var v = await SembrarAsync(100m, new Linea(3, 33.34m, 0m, 100.02m, 21m));
        var visto = Item(await ContextoAsync(v.VentaId), v.ProductoIds[0]);
        var esperadoParaDos = Importe(visto, 2);

        await CrearAsync(v, v.ProductoIds[0], 1);   // otra operadora devuelve 1 unidad antes

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CrearDevolucionAsync(
            new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "P02 esperado", TipoResolucion = TipoResolucionDevolucion.NotaCredito },
            new List<DevolucionDetalle> { new() { ProductoId = v.ProductoIds[0], Cantidad = 2, ImporteEsperado = esperadoParaDos, AccionRecomendada = AccionProducto.ReintegrarStock } }));
        Assert.Contains("cambió", ex.Message);

        // Con el importe correcto (el que corresponde ahora) sí se registra.
        var ahora = Item(await ContextoAsync(v.VentaId), v.ProductoIds[0]);
        var ok = await svc.CrearDevolucionAsync(
            new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "P02 esperado ok", TipoResolucion = TipoResolucionDevolucion.NotaCredito },
            new List<DevolucionDetalle> { new() { ProductoId = v.ProductoIds[0], Cantidad = 2, ImporteEsperado = Importe(ahora, 2), AccionRecomendada = AccionProducto.ReintegrarStock } });
        Assert.Equal(Importe(ahora, 2), ok.TotalDevolucion);
    }

    public static IEnumerable<object[]> Escenarios()
    {
        // Descuento del medio de pago: lista $3.002,01, total cobrado $2.851,90 (caso documentado).
        yield return new object[] { "descuento medio de pago", 2851.90m, new[] { new Linea(1, 3002.01m, 0m, 3002.01m, 21m) } };
        // Recargo del medio de pago (3 %) con alícuotas mixtas (21 / 10,5) y varias unidades.
        yield return new object[] { "recargo medio de pago, alicuotas mixtas", 3605m,
            new[] { new Linea(3, 1000m, 0m, 3000m, 21m), new Linea(1, 500m, 0m, 500m, 10.5m) } };
        // Descuento de línea 10 % sin ajuste de medio de pago.
        yield return new object[] { "descuento de linea", 2700m, new[] { new Linea(3, 1000m, 10m, 2700m, 21m) } };
        // Descuento de línea + descuento del medio de pago + alícuotas mixtas.
        yield return new object[] { "descuento de linea y medio de pago, mixtas", 3386.50m,
            new[] { new Linea(2, 999.99m, 5m, 1899.98m, 21m), new Linea(2, 800.01m, 0m, 1600.02m, 10.5m) } };
    }

    [Theory(DisplayName = "REGRESION [P-02]: el importe de la vista previa es el que persiste la devolucion (parcial y total, descuentos y recargos)")]
    [MemberData(nameof(Escenarios))]
    public async Task Preview_CoincideConLoPersistido(string _, decimal total, Linea[] lineas)
    {
        var v = await SembrarAsync(total, lineas);
        var inicial = await ContextoAsync(v.VentaId);

        // Identidad del desglose y total reintegrable = lo cobrado por los productos (el ajuste neto reparte el total de la venta).
        decimal sumaReintegrable = 0m;
        foreach (var pid in v.ProductoIds)
        {
            var it = Item(inicial, pid);
            var lista = it.GetProperty("valorLista").GetDecimal();
            var desc = it.GetProperty("descuentos").GetDecimal();
            var ajuste = it.GetProperty("ajusteMedioPago").GetDecimal();
            var reintegrable = it.GetProperty("totalReintegrable").GetDecimal();
            Assert.Equal(reintegrable, lista - desc + ajuste);
            Assert.Equal(reintegrable, Importe(it, it.GetProperty("cantidadDisponible").GetInt32()));
            Assert.True(reintegrable <= lista + Math.Abs(ajuste));
            sumaReintegrable += reintegrable;
        }
        // Devolver todo reintegra lo cobrado (el total de la venta, salvo el truncado a pesos que la venta no cobra).
        Assert.InRange(total - sumaReintegrable, 0m, 1m);

        // Parcial sobre el primer producto con más de una unidad (o la única), luego el resto: cada paso == vista previa.
        var primero = v.ProductoIds[0];
        var itemPrimero = Item(inicial, primero);
        var disponible = itemPrimero.GetProperty("cantidadDisponible").GetInt32();
        decimal persistido = 0m;

        if (disponible > 1)
        {
            var esperadoParcial = Importe(itemPrimero, 1);
            var parcial = await CrearAsync(v, primero, 1);
            Assert.Equal(esperadoParcial, parcial.TotalDevolucion);
            Assert.Equal(esperadoParcial, parcial.Detalles.Single().Subtotal);
            persistido += parcial.TotalDevolucion;

            var tras = Item(await ContextoAsync(v.VentaId), primero);
            var resto = tras.GetProperty("cantidadDisponible").GetInt32();
            Assert.Equal(disponible - 1, resto);
            var esperadoResto = Importe(tras, resto);
            var final = await CrearAsync(v, primero, resto);
            Assert.Equal(esperadoResto, final.TotalDevolucion);
            persistido += final.TotalDevolucion;
        }
        else
        {
            var unica = await CrearAsync(v, primero, 1);
            Assert.Equal(Importe(itemPrimero, 1), unica.TotalDevolucion);
            persistido += unica.TotalDevolucion;
        }

        // Entre las dos (o la única) devoluciones del producto se reintegra exactamente lo reintegrable, ni un centavo más.
        Assert.Equal(itemPrimero.GetProperty("totalReintegrable").GetDecimal(), persistido);

        // Los demás productos completos: también coinciden.
        foreach (var pid in v.ProductoIds.Skip(1))
        {
            var it = Item(inicial, pid);
            var n = it.GetProperty("cantidadDisponible").GetInt32();
            var dev = await CrearAsync(v, pid, n);
            Assert.Equal(Importe(it, n), dev.TotalDevolucion);
        }
    }
}
