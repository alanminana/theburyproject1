using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class VentaContactoLibreTests
{
    [Fact]
    public async Task Efectivo_ConfirmaFacturaYCaja_SinAltaNiVinculoPorDni_YNoDuplicaCobro()
    {
        await using var factory = new CustomWebApplicationFactory();
        await factory.SeedTestUserAsync();
        using var client = factory.CreateClient();
        int cotizacionId, productoId, aperturaId, clientesAntes;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Incluso si coincide el DNI, nunca debe asociar o modificar un cliente existente.
            db.Clientes.Add(new Cliente { Nombre = "Registrado", Apellido = "Original", NumeroDocumento = "30111222" });
            var producto = new Producto { Codigo = "LIBRE", Nombre = "Mesa libre", PrecioVenta = 1000m, StockActual = 10,
                Categoria = new Categoria { Nombre = "Muebles" }, Marca = new Marca { Nombre = "Prueba" } };
            var apertura = new AperturaCaja { Caja = new Caja { Codigo = "LIBRE", Nombre = "Caja libre", Estado = EstadoCaja.Abierta }, UsuarioApertura = "testuser" };
            db.AddRange(producto, apertura);
            await db.SaveChangesAsync();
            var cotizacion = CrearCotizacion(producto.Id);
            db.Cotizaciones.Add(cotizacion);
            await db.SaveChangesAsync();
            cotizacionId = cotizacion.Id;
            productoId = producto.Id;
            aperturaId = apertura.Id;
            clientesAntes = await db.Clientes.CountAsync();
        }

        var request = new CotizacionConversionRequest { ConfirmarVenta = true, Facturar = true, ConfirmarAdvertencias = true };
        var preflightResponse = await client.PostAsJsonAsync($"/api/cotizacion/{cotizacionId}/conversion/preflight", request);
        preflightResponse.EnsureSuccessStatusCode();
        var preflight = await preflightResponse.Content.ReadFromJsonAsync<CotizacionMiVentaPreflightResultado>();
        Assert.True(preflight!.Listo, string.Join("; ", preflight.Bloqueos.Select(b => b.Mensaje)));

        var response = await client.PostAsJsonAsync($"/api/cotizacion/{cotizacionId}/conversion/convertir", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var resultado = await response.Content.ReadFromJsonAsync<CotizacionConversionResultado>();
        Assert.True(resultado!.VentaConfirmada, resultado.MensajeConfirmacion);
        Assert.True(resultado.Facturada, resultado.MensajeConfirmacion);

        var repetido = await client.PostAsJsonAsync($"/api/cotizacion/{cotizacionId}/conversion/convertir", request);
        Assert.False(repetido.IsSuccessStatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var venta = await db.Ventas.Include(v => v.Cliente).Include(v => v.Facturas).Include(v => v.Detalles).SingleAsync();
            Assert.Null(venta.ClienteId);
            Assert.Null(venta.Cliente);
            Assert.Equal("Ana Libre", venta.NombreClienteLibre);
            Assert.Equal("30111222", venta.DniClienteLibre);
            Assert.Equal("1122334455", venta.TelefonoClienteLibre);
            Assert.Equal(clientesAntes, await db.Clientes.CountAsync());
            Assert.Equal("Registrado", (await db.Clientes.SingleAsync()).Nombre);
            Assert.Equal(8, (await db.Productos.FindAsync(productoId))!.StockActual);
            var movimiento = await db.MovimientosCaja.SingleAsync(m => m.VentaId == venta.Id);
            Assert.Equal(aperturaId, movimiento.AperturaCajaId);
            Assert.Equal(venta.TotalACobrar, movimiento.Monto);
            var comprobante = FacturaComprobanteBuilder.Build(Assert.Single(venta.Facturas));
            Assert.Equal("Ana Libre", comprobante.Cliente.Nombre);
            Assert.Equal("30111222", comprobante.Cliente.Documento);
            var model = await scope.ServiceProvider.GetRequiredService<IVentaService>().GetByIdAsync(venta.Id);
            Assert.Equal("Ana Libre", model!.ClienteNombre);
            Assert.Equal("30111222", model.ClienteDocumento);
            var caja = await scope.ServiceProvider.GetRequiredService<ICajaService>().ObtenerDetallesAperturaAsync(aperturaId);
            Assert.NotNull(caja);
            var conciliacion = TheBuryProject.Services.CajaConciliacionBuilder.Build(caja!, null, true);
            Assert.Equal("Ana Libre", Assert.Single(conciliacion.Ventas).Cliente);
        }
    }

    [Theory]
    [InlineData(CotizacionMedioPagoTipo.Efectivo, "", "30111222", "1122334455")]
    [InlineData(CotizacionMedioPagoTipo.Efectivo, "Ana", "", "1122334455")]
    [InlineData(CotizacionMedioPagoTipo.Efectivo, "Ana", "abc12345", "1122334455")]
    [InlineData(CotizacionMedioPagoTipo.Efectivo, "Ana", "30111222", "")]
    [InlineData(CotizacionMedioPagoTipo.CreditoPersonal, "Ana", "30111222", "1122334455")]
    public async Task ContactoIncompletoOMedioCredito_RechazaSinCrearVentaNiCobro(
        CotizacionMedioPagoTipo medio, string nombre, string dni, string telefono)
    {
        await using var factory = new CustomWebApplicationFactory();
        await factory.SeedTestUserAsync();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cotizacion = new Cotizacion { Numero = "LIBRE-INVALIDA", MedioPagoSeleccionado = medio,
            NombreClienteLibre = nombre, DniClienteLibre = dni, TelefonoClienteLibre = telefono };
        db.Cotizaciones.Add(cotizacion);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/api/cotizacion/{cotizacion.Id}/conversion/convertir",
            new CotizacionConversionRequest { ConfirmarVenta = true });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(await db.Ventas.ToListAsync());
        Assert.Empty(await db.MovimientosCaja.ToListAsync());
        Assert.Empty(await db.Clientes.ToListAsync());
    }

    // Sólo Crédito personal exige cliente registrado: con datos de contacto libre completos, el
    // resto de los medios (Transferencia, Tarjeta crédito/débito, MercadoPago) puede convertirse.
    [Theory]
    [InlineData(CotizacionMedioPagoTipo.Transferencia)]
    [InlineData(CotizacionMedioPagoTipo.TarjetaCredito)]
    [InlineData(CotizacionMedioPagoTipo.TarjetaDebito)]
    [InlineData(CotizacionMedioPagoTipo.MercadoPago)]
    public async Task ContactoLibreConDatosValidos_PermiteMedioDistintoDeCreditoPersonal(CotizacionMedioPagoTipo medio)
    {
        await using var factory = new CustomWebApplicationFactory();
        await factory.SeedTestUserAsync();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cotizacion = new Cotizacion { Numero = $"LIBRE-{medio}", MedioPagoSeleccionado = medio,
            NombreClienteLibre = "Ana Libre", DniClienteLibre = "30111222", TelefonoClienteLibre = "1122334455" };
        db.Cotizaciones.Add(cotizacion);
        await db.SaveChangesAsync();

        var response = await client.PostAsJsonAsync($"/api/cotizacion/{cotizacion.Id}/conversion/convertir",
            new CotizacionConversionRequest { ConfirmarVenta = false });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var venta = await db.Ventas.SingleAsync();
        Assert.Null(venta.ClienteId);
        Assert.Equal("Ana Libre", venta.NombreClienteLibre);
        Assert.Equal("30111222", venta.DniClienteLibre);
    }

    private static Cotizacion CrearCotizacion(int productoId) => new()
    {
        Numero = "LIBRE-VALIDA", NombreClienteLibre = "Ana Libre", DniClienteLibre = "30111222", TelefonoClienteLibre = "1122334455",
        MedioPagoSeleccionado = CotizacionMedioPagoTipo.Efectivo, TotalBase = 2000m, Subtotal = 2000m,
        Detalles = { new CotizacionDetalle { ProductoId = productoId, NombreProductoSnapshot = "Mesa libre",
            CodigoProductoSnapshot = "LIBRE", PrecioUnitarioSnapshot = 1000m, Cantidad = 2, Subtotal = 2000m } }
    };
}
