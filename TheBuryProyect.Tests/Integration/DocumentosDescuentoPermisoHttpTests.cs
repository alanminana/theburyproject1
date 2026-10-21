using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// DESCUENTO-PERMISO-03 (PROMPT 76) sobre el pipeline HTTP real: facturas, contratos y documentos emitidos que
/// contienen descuentos comerciales NO se entregan a quien no tiene ventas.discount (aunque tenga ventas.view,
/// ventas.invoice o documentos.view/reprint), tanto en documentos históricos como nuevos y por URL directa. Los
/// documentos sin descuentos siguen entregándose con los permisos de siempre. La denegación nunca incluye importes.
/// </summary>
[Collection("HttpIntegration")]
public class DocumentosDescuentoPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private static int _counter = 76300;
    private readonly CustomWebApplicationFactory _factory;

    public DocumentosDescuentoPermisoHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Sin() => _factory.CreateClientWithUserId(CustomWebApplicationFactory.DocumentosSinDescuentoUserId);
    private HttpClient Con() => _factory.CreateClientWithUserId(CustomWebApplicationFactory.DocumentosConDescuentoUserId);

    private async Task PrepararUsuariosAsync()
    {
        await _factory.SeedUserWithDocumentosSinDescuentoAsync();
        await _factory.SeedUserWithDocumentosConDescuentoAsync();
    }

    // ------------------------------------------------------------------ Factura

    [Fact]
    public async Task ComprobanteFactura_ConDescuentos_SinPermiso_Se_Deniega_ConPermiso_Se_Entrega()
    {
        await PrepararUsuariosAsync();
        var (_, facturaId) = await SeedVentaAsync(conDescuentos: true, conFactura: true);

        using (var sin = Sin())
        {
            var response = await sin.GetAsync($"/Venta/ComprobanteFactura?facturaId={facturaId}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var cuerpo = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("1.800", cuerpo);
            Assert.DoesNotContain("500", cuerpo);
        }

        using (var con = Con())
        {
            var response = await con.GetAsync($"/Venta/ComprobanteFactura?facturaId={facturaId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // el comprobante histórico se entrega intacto
        }
    }

    [Fact]
    public async Task ComprobanteFactura_SinDescuentos_SinPermiso_SigueEntregandose()
    {
        await PrepararUsuariosAsync();
        var (_, facturaId) = await SeedVentaAsync(conDescuentos: false, conFactura: true);

        using var sin = Sin();
        var response = await sin.GetAsync($"/Venta/ComprobanteFactura?facturaId={facturaId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VentaDetails_SinPermiso_ConDescuentos_NoOfreceImprimirFactura()
    {
        await PrepararUsuariosAsync();
        var (ventaConDescuento, _) = await SeedVentaAsync(conDescuentos: true, conFactura: true);
        var (ventaSinDescuento, _) = await SeedVentaAsync(conDescuentos: false, conFactura: true);

        using var sin = Sin();
        var conDesc = await (await sin.GetAsync($"/Venta/Details/{ventaConDescuento}")).Content.ReadAsStringAsync();
        var sinDesc = await (await sin.GetAsync($"/Venta/Details/{ventaSinDescuento}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("ComprobanteFactura", conDesc);
        Assert.Contains("ComprobanteFactura", sinDesc);
    }

    // ------------------------------------------------------------------ Contrato

    [Fact]
    public async Task ContratoVer_ConDescuentos_SinPermiso_Se_Deniega_ConPermiso_NoEsDenegado()
    {
        await PrepararUsuariosAsync();
        var (ventaId, _) = await SeedVentaAsync(conDescuentos: true, conFactura: false);

        using (var sin = Sin())
        {
            var response = await sin.GetAsync($"/ContratoVentaCredito/Ver?ventaId={ventaId}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var con = Con())
        {
            // Sin contrato generado el servicio responde con su redirección habitual: lo importante es que NO sea 403.
            var response = await con.GetAsync($"/ContratoVentaCredito/Ver?ventaId={ventaId}");
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task ContratoVer_SinDescuentos_SinPermiso_NoEsDenegado()
    {
        await PrepararUsuariosAsync();
        var (ventaId, _) = await SeedVentaAsync(conDescuentos: false, conFactura: false);

        using var sin = Sin();
        var response = await sin.GetAsync($"/ContratoVentaCredito/Ver?ventaId={ventaId}");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------------------ Documentos emitidos

    [Fact]
    public async Task Documento_VentaConDescuentos_TodasLasRutasDeEntrega_SinPermiso_SeDeniegan_SinContarReimpresion()
    {
        await PrepararUsuariosAsync();
        var (ventaId, _) = await SeedVentaAsync(conDescuentos: true, conFactura: false);
        var grupo = Guid.NewGuid();
        var pagoId = 760000 + ventaId;
        var docId = await SeedDocumentoAsync(ventaId: ventaId, grupo: grupo, pagoId: pagoId);

        using (var sin = Sin())
        {
            foreach (var url in new[]
            {
                $"/Documento/Ver/{docId}",
                $"/Documento/VerVarios?ids={docId}",
                $"/Documento/VerGrupo/{grupo}",
                $"/Documento/VerPorPago/{pagoId}",
                $"/Documento/Reimprimir?ids={docId}"
            })
            {
                var response = await sin.GetAsync(url);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} -> {(int)response.StatusCode}");
                Assert.DoesNotContain("1.800", await response.Content.ReadAsStringAsync());
            }
        }

        Assert.Equal(0, await ContadorReimpresionesAsync(docId));

        using (var con = Con())
        {
            var ver = await con.GetAsync($"/Documento/Ver/{docId}");
            Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
            Assert.Equal("application/pdf", ver.Content.Headers.ContentType?.MediaType);

            var reimprimir = await con.GetAsync($"/Documento/Reimprimir?ids={docId}");
            Assert.Equal(HttpStatusCode.OK, reimprimir.StatusCode);
        }
        Assert.Equal(1, await ContadorReimpresionesAsync(docId));
    }

    [Fact]
    public async Task Documento_VentaSinDescuentos_SinPermiso_SigueEntregandose()
    {
        await PrepararUsuariosAsync();
        var (ventaId, _) = await SeedVentaAsync(conDescuentos: false, conFactura: false);
        var docId = await SeedDocumentoAsync(ventaId: ventaId);

        using var sin = Sin();
        var response = await sin.GetAsync($"/Documento/Ver/{docId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DocumentoIndexYDetails_SinPermiso_OcultanAccesosDeDocumentosConDescuentos_ConPermisoLosMuestran()
    {
        await PrepararUsuariosAsync();
        var (conDesc, _) = await SeedVentaAsync(conDescuentos: true, conFactura: false);
        var (sinDesc, _) = await SeedVentaAsync(conDescuentos: false, conFactura: false);
        var docRestringido = await SeedDocumentoAsync(ventaId: conDesc);
        var docLibre = await SeedDocumentoAsync(ventaId: sinDesc);

        using (var sin = Sin())
        {
            var index = await (await sin.GetAsync("/Documento/Index")).Content.ReadAsStringAsync();
            Assert.DoesNotContain($"/Documento/Ver/{docRestringido}\"", index);
            Assert.DoesNotContain($"/Documento/Reimprimir?ids={docRestringido}\"", index);
            Assert.Contains("data-doc-restringido", index);
            Assert.Contains($"/Documento/Ver/{docLibre}\"", index);   // sin descuentos: sigue accesible

            var detalles = await (await sin.GetAsync($"/Venta/Details/{conDesc}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain($"/Documento/Ver/{docRestringido}\"", detalles);
            Assert.Contains("data-doc-restringido", detalles);

            // La UI oculta, pero el 403 por URL directa se mantiene.
            Assert.Equal(HttpStatusCode.Forbidden, (await sin.GetAsync($"/Documento/Ver/{docRestringido}")).StatusCode);
        }

        using (var con = Con())
        {
            var index = await (await con.GetAsync("/Documento/Index")).Content.ReadAsStringAsync();
            Assert.Contains($"/Documento/Ver/{docRestringido}\"", index);
            Assert.DoesNotContain("data-doc-restringido", index);
        }
    }

    [Fact]
    public async Task Documento_LoteMixto_BastaUnoConDescuentos_ParaDenegarTodoElLote()
    {
        await PrepararUsuariosAsync();
        var (conDesc, _) = await SeedVentaAsync(conDescuentos: true, conFactura: false);
        var (sinDesc, _) = await SeedVentaAsync(conDescuentos: false, conFactura: false);
        var d1 = await SeedDocumentoAsync(ventaId: sinDesc);
        var d2 = await SeedDocumentoAsync(ventaId: conDesc);

        using var sin = Sin();
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.GetAsync($"/Documento/VerVarios?ids={d1},{d2}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sin.GetAsync($"/Documento/VerVarios?ids={d1}")).StatusCode);
    }

    [Fact]
    public async Task Documento_DeCotizacionConDescuentos_SinPermiso_SeDeniega()
    {
        await PrepararUsuariosAsync();
        var cotizacionId = await SeedCotizacionAsync(conDescuento: true);
        var docId = await SeedDocumentoAsync(cotizacionId: cotizacionId);

        using (var sin = Sin())
            Assert.Equal(HttpStatusCode.Forbidden, (await sin.GetAsync($"/Documento/Ver/{docId}")).StatusCode);
        using (var con = Con())
            Assert.Equal(HttpStatusCode.OK, (await con.GetAsync($"/Documento/Ver/{docId}")).StatusCode);
    }

    [Fact]
    public async Task Documento_DeCreditoDeUnaVentaConDescuentos_SinPermiso_SeDeniega()
    {
        await PrepararUsuariosAsync();
        var (ventaId, _) = await SeedVentaAsync(conDescuentos: true, conFactura: false);
        var creditoId = await VincularCreditoAsync(ventaId);
        var docId = await SeedDocumentoAsync(creditoId: creditoId);   // anclado solo al crédito (constancia/recibo de cuota)

        using var sin = Sin();
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.GetAsync($"/Documento/Ver/{docId}")).StatusCode);
    }

    // ------------------------------------------------------------------ Datos

    private async Task<T> ConContextoAsync<T>(Func<AppDbContext, Task<T>> accion)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        return await accion(context);
    }

    private Task<int> ContadorReimpresionesAsync(int docId) =>
        ConContextoAsync(c => c.DocumentosGenerados.AsNoTracking().Where(d => d.Id == docId).Select(d => d.ContadorReimpresiones).SingleAsync());

    private Task<int> VincularCreditoAsync(int ventaId) => ConContextoAsync(async c =>
    {
        var venta = await c.Ventas.SingleAsync(v => v.Id == ventaId);
        var credito = new Credito { ClienteId = venta.ClienteId!.Value, Numero = $"CR-DP-{Interlocked.Increment(ref _counter)}" };
        c.Creditos.Add(credito);
        await c.SaveChangesAsync();
        venta.CreditoId = credito.Id;
        await c.SaveChangesAsync();
        return credito.Id;
    });

    private Task<(int VentaId, int FacturaId)> SeedVentaAsync(bool conDescuentos, bool conFactura) => ConContextoAsync(async context =>
    {
        var suffix = Interlocked.Increment(ref _counter);
        var marca = new Marca { Codigo = $"MF{suffix}", Nombre = $"Marca DOC {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CF{suffix}", Nombre = $"Categoria DOC {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = $"ClienteDoc{suffix}", Apellido = "DESC-DOC", NumeroDocumento = $"60{suffix}",
            Telefono = "1111-2222", Domicilio = "Calle Documento 76"
        };
        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PF{suffix}", Nombre = $"ProductoDoc{suffix}", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 600m, PrecioVenta = 1000m, PorcentajeIVA = 0m, StockActual = 100, Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VDOC-{suffix}",
            FechaVenta = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
            Estado = conFactura ? EstadoVenta.Facturada : EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            Subtotal = conDescuentos ? 1800m : 2000m,
            Descuento = conDescuentos ? 500m : 0m,
            IVA = 0m,
            Total = conDescuentos ? 1300m : 2000m,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        venta.Detalles.Add(new VentaDetalle
        {
            ProductoId = producto.Id,
            Cantidad = 2,
            PrecioUnitario = 1000m,
            Descuento = conDescuentos ? 10m : 0m,
            Subtotal = conDescuentos ? 1800m : 2000m,
            DescuentoGeneralProrrateado = conDescuentos ? 500m : 0m,
            PorcentajeIVA = 0m,
            SubtotalFinalNeto = conDescuentos ? 1300m : 2000m,
            SubtotalFinalIVA = 0m,
            SubtotalFinal = conDescuentos ? 1300m : 2000m
        });
        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        var facturaId = 0;
        if (conFactura)
        {
            var factura = new Factura
            {
                VentaId = venta.Id,
                Numero = $"0001-{suffix:00000000}",
                Tipo = TipoFactura.B,
                PuntoVenta = "0001",
                FechaEmision = DateTime.UtcNow,
                Subtotal = venta.Total,
                IVA = 0m,
                Total = venta.Total
            };
            context.Facturas.Add(factura);
            await context.SaveChangesAsync();
            facturaId = factura.Id;
        }

        return (venta.Id, facturaId);
    });

    private Task<int> SeedCotizacionAsync(bool conDescuento) => ConContextoAsync(async context =>
    {
        var suffix = Interlocked.Increment(ref _counter);
        var cotizacion = new Cotizacion
        {
            Numero = $"COT-DOC-{suffix}",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            NombreClienteLibre = "Cliente mostrador",
            Subtotal = 2000m,
            DescuentoTotal = conDescuento ? 100m : 0m,
            TotalBase = conDescuento ? 1900m : 2000m
        };
        context.Cotizaciones.Add(cotizacion);
        await context.SaveChangesAsync();
        return cotizacion.Id;
    });

    private Task<int> SeedDocumentoAsync(int? ventaId = null, int? cotizacionId = null, int? creditoId = null, Guid? grupo = null, int? pagoId = null) =>
        ConContextoAsync(async context =>
        {
            var suffix = Interlocked.Increment(ref _counter);
            var tipo = new TipoDocumento { Codigo = $"TDOC{suffix}", Nombre = $"Tipo doc {suffix}", Prefijo = "TST" };
            context.TiposDocumento.Add(tipo);
            await context.SaveChangesAsync();
            var plantilla = new PlantillaDocumento { TipoDocumentoId = tipo.Id, Codigo = $"PDOC{suffix}", Nombre = $"Plantilla {suffix}", VersionActual = 1 };
            context.PlantillasDocumento.Add(plantilla);
            await context.SaveChangesAsync();
            var version = new PlantillaDocumentoVersion { PlantillaDocumentoId = plantilla.Id, Numero = 1, Contenido = "Contenido de prueba" };
            context.PlantillasDocumentoVersion.Add(version);
            await context.SaveChangesAsync();

            var doc = new DocumentoGenerado
            {
                TipoDocumentoId = tipo.Id,
                PlantillaDocumentoId = plantilla.Id,
                PlantillaDocumentoVersionId = version.Id,
                Numero = $"TST-{suffix}",
                VentaId = ventaId,
                CotizacionId = cotizacionId,
                CreditoId = creditoId,
                PagoCuotaId = pagoId,
                GrupoImpresionId = grupo,
                EventoOrigen = "TEST",
                ClaveIdempotencia = $"dp3|{suffix}",
                Estado = EstadoDocumentoGenerado.Generado,
                FechaGeneracionUtc = DateTime.UtcNow,
                UsuarioGeneracion = "tester",
                ContenidoRenderizado = "Contenido de prueba",
                DatosSnapshotJson = "{}",
                ContentHash = "h"
            };
            context.DocumentosGenerados.Add(doc);
            await context.SaveChangesAsync();
            return doc.Id;
        });
}
