using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H4-E2 — la nota de crédito (de una devolución) se canjea como medio de pago de UNA venta, por el total a cobrar.
/// Servicios reales (VentaService, CajaService) sobre SQLite; el usuario actual es el del harness (ConcCurrentUser) para
/// que existan sesión y caja. Cada prueba verifica el ESTADO FINAL en la base (saldo de la nota, uso, venta, caja).
/// </summary>
[Collection("HttpIntegration")]
public sealed class NotaCreditoCanjeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotaCreditoCanjeTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Escenario(int ClienteId, int ProductoId, int AperturaId);

    private async Task<Escenario> SembrarAsync(int? aperturaId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = new ConcCurrentUser().GetUserId();
        if (!await ctx.Users.AnyAsync(u => u.Id == userId))
        {
            ctx.Users.Add(new ApplicationUser
            {
                Id = userId, UserName = ConcCurrentUser.Usuario, NormalizedUserName = ConcCurrentUser.Usuario.ToUpperInvariant(),
                Email = "qaconc@example.invalid", NormalizedEmail = "QACONC@EXAMPLE.INVALID", Activo = true, RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();
        }

        var apertura = aperturaId ?? await ConcSeed.SembrarAperturaAsync(ctx);
        var cliente = await NuevoClienteAsync(ctx);
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var cat = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(cat, marca);
        await ctx.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 50m, PorcentajeIVA = 21m, StockActual = 50m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return new Escenario(cliente, producto.Id, apertura);
    }

    private static async Task<int> NuevoClienteAsync(AppDbContext ctx)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var cliente = new Cliente
        {
            Nombre = "NC", Apellido = "Canje" + sufijo, TipoDocumento = "DNI", NumeroDocumento = sufijo,
            Email = $"{sufijo}@test.com", Activo = true, Telefono = "0000000000", Domicilio = "QA 1"
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    /// <summary>Nota de crédito real: cuelga de una devolución real de una venta histórica del mismo cliente.</summary>
    private async Task<int> SembrarNotaAsync(
        Escenario e, decimal monto, decimal utilizado = 0m, EstadoNotaCredito estado = EstadoNotaCredito.Vigente,
        DateTime? vence = null, int? clienteId = null, EstadoVenta estadoOrigen = EstadoVenta.Facturada)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cli = clienteId ?? e.ClienteId;
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var origen = new Venta
        {
            Numero = "VO" + sufijo, ClienteId = cli, Estado = estadoOrigen, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, Subtotal = monto, Total = monto, AperturaCajaId = e.AperturaId
        };
        ctx.Ventas.Add(origen);
        await ctx.SaveChangesAsync();
        var dev = new Devolucion
        {
            NumeroDevolucion = "DV" + sufijo, VentaId = origen.Id, ClienteId = cli, Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "origen NC", Estado = EstadoDevolucion.Completada, TipoResolucion = TipoResolucionDevolucion.NotaCredito,
            FechaDevolucion = DateTime.UtcNow, TotalDevolucion = monto
        };
        ctx.Devoluciones.Add(dev);
        await ctx.SaveChangesAsync();
        var nc = new NotaCredito
        {
            NumeroNotaCredito = "NC" + sufijo, ClienteId = cli, DevolucionId = dev.Id, FechaEmision = DateTime.UtcNow,
            MontoTotal = monto, MontoUtilizado = utilizado, Estado = estado, FechaVencimiento = vence ?? DateTime.UtcNow.AddYears(1)
        };
        ctx.NotasCredito.Add(nc);
        await ctx.SaveChangesAsync();
        return nc.Id;
    }

    /// <summary>Venta en Cotización: 2 × $50 = $100 (sin envío), medio "Nota de crédito".</summary>
    private async Task<int> SembrarVentaAsync(Escenario e, int? notaCreditoId, int? clienteId = null, decimal total = 100m,
        TipoPago tipoPago = TipoPago.NotaCredito)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var venta = new Venta
        {
            Numero = "VNC" + sufijo, ClienteId = clienteId ?? e.ClienteId, Estado = EstadoVenta.Cotizacion, TipoPago = tipoPago,
            NotaCreditoId = notaCreditoId, FechaVenta = DateTime.UtcNow, Subtotal = total, Total = total,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = e.ProductoId, Cantidad = 2, PrecioUnitario = total / 2, Descuento = 0m, Subtotal = total,
                    CostoUnitarioAlMomento = 17.5m, CostoTotalAlMomento = 35m
                }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return venta.Id;
    }

    private IVentaService Servicio(IServiceScope scope) =>
        ActivatorUtilities.CreateInstance<VentaService>(scope.ServiceProvider, (ICurrentUserService)new ConcCurrentUser());

    private async Task<bool> ConfirmarAsync(int ventaId)
    {
        using var scope = _factory.Services.CreateScope();
        return await Servicio(scope).ConfirmarVentaAsync(ventaId);
    }

    private async Task<string> ConfirmarRechazadaAsync(int ventaId)
    {
        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(scope).ConfirmarVentaAsync(ventaId));
        return ex.Message;
    }

    private async Task<NotaCredito> NotaAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotasCredito.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    private async Task<List<NotaCreditoUso>> UsosAsync(int notaId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotasCreditoUso.AsNoTracking()
            .Where(u => u.NotaCreditoId == notaId).ToListAsync();
    }

    private async Task<Venta> VentaAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Ventas.AsNoTracking().SingleAsync(v => v.Id == id);
    }

    private async Task<List<MovimientoCaja>> MovimientosAsync(int ventaId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja.AsNoTracking()
            .Where(m => m.VentaId == ventaId).OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<decimal> StockAsync(int productoId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Productos.AsNoTracking()
            .Where(p => p.Id == productoId).Select(p => p.StockActual).SingleAsync();
    }

    // ------------------------------------------------------------------ canje

    [Fact(DisplayName = "H4-E2 [NC-01] confirmar con nota que cubre el total: descuenta saldo, deja el uso y asienta caja sin efectivo")]
    public async Task Canje_NotaCubreTotal_DescuentaSaldoYAsientaCajaSinEfectivo()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m);
        var venta = await SembrarVentaAsync(e, nc);

        Assert.True(await ConfirmarAsync(venta));

        var nota = await NotaAsync(nc);
        Assert.Equal(100m, nota.MontoUtilizado);
        Assert.Equal(400m, nota.MontoDisponible);               // el remanente se conserva
        Assert.Equal(EstadoNotaCredito.UtilizadaParcialmente, nota.Estado);
        Assert.Contains("Canje", nota.Observaciones);

        var uso = Assert.Single(await UsosAsync(nc));
        Assert.Equal(venta, uso.VentaId);
        Assert.Equal(100m, uso.Monto);
        Assert.Null(uso.FechaReversion);
        Assert.Equal(ConcCurrentUser.Usuario, uso.Usuario);

        Assert.Equal(EstadoVenta.Confirmada, (await VentaAsync(venta)).Estado);
        Assert.Equal(48m, await StockAsync(e.ProductoId));

        var mov = Assert.Single(await MovimientosAsync(venta));
        Assert.Equal(TipoMovimientoCaja.Ingreso, mov.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.VentaNotaCredito, mov.Concepto);
        Assert.Equal(TipoPago.NotaCredito, mov.TipoPago);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.NoAplica, mov.EstadoAcreditacion);
        Assert.Equal(100m, mov.Monto);

        // El canje NO es efectivo: ni el saldo real ni la caja física esperada se mueven.
        using var scope = _factory.Services.CreateScope();
        var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
        Assert.Equal(0m, await caja.CalcularSaldoRealAsync(e.AperturaId));
        var detalle = await caja.ObtenerDetallesAperturaAsync(e.AperturaId);
        Assert.Equal(0m, detalle.TotalIngresosFisicos);
        Assert.Equal(0m, detalle.SaldoPendienteAcreditacion);
    }

    [Fact(DisplayName = "H4-E2 [NC-02] nota con saldo exacto queda UtilizadaTotalmente")]
    public async Task Canje_SaldoExacto_QuedaUtilizadaTotalmente()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 100m);
        var venta = await SembrarVentaAsync(e, nc);

        Assert.True(await ConfirmarAsync(venta));

        var nota = await NotaAsync(nc);
        Assert.Equal(0m, nota.MontoDisponible);
        Assert.Equal(EstadoNotaCredito.UtilizadaTotalmente, nota.Estado);
    }

    [Fact(DisplayName = "H4-E2 [NC-03] nota que no alcanza: se rechaza sin efectos (venta, stock, nota y caja intactos)")]
    public async Task Canje_SaldoInsuficiente_SeRechazaSinEfectos()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 150m, utilizado: 100m);   // disponible 50 < 100
        var venta = await SembrarVentaAsync(e, nc);

        var mensaje = await ConfirmarRechazadaAsync(venta);

        Assert.Contains("no cubre el total", mensaje);
        await AssertSinEfectosAsync(e, nc, venta, utilizadoEsperado: 100m);
    }

    [Theory(DisplayName = "H4-E2 [NC-04] nota cancelada, vencida o totalmente usada: se rechaza sin efectos")]
    [InlineData("cancelada")]
    [InlineData("vencida-fecha")]
    [InlineData("usada")]
    public async Task Canje_NotaNoVigente_SeRechazaSinEfectos(string caso)
    {
        var e = await SembrarAsync();
        var nc = caso switch
        {
            "cancelada" => await SembrarNotaAsync(e, 500m, estado: EstadoNotaCredito.Cancelada),
            "vencida-fecha" => await SembrarNotaAsync(e, 500m, vence: DateTime.UtcNow.AddDays(-1)),
            _ => await SembrarNotaAsync(e, 500m, utilizado: 500m, estado: EstadoNotaCredito.UtilizadaTotalmente)
        };
        var antes = (await NotaAsync(nc)).MontoUtilizado;
        var venta = await SembrarVentaAsync(e, nc);

        await ConfirmarRechazadaAsync(venta);

        await AssertSinEfectosAsync(e, nc, venta, antes);
    }

    [Fact(DisplayName = "H4-E2 [NC-05] nota de OTRO cliente: se rechaza sin efectos")]
    public async Task Canje_NotaDeOtroCliente_SeRechazaSinEfectos()
    {
        var e = await SembrarAsync();
        int otro;
        using (var scope = _factory.Services.CreateScope())
            otro = await NuevoClienteAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var nc = await SembrarNotaAsync(e, 500m, clienteId: otro);
        var venta = await SembrarVentaAsync(e, nc);

        var mensaje = await ConfirmarRechazadaAsync(venta);

        Assert.Contains("no pertenece al cliente", mensaje);
        await AssertSinEfectosAsync(e, nc, venta, 0m);
    }

    [Fact(DisplayName = "H4-E2 [NC-05b] nota emitida sobre una venta que nunca se concretó: no se canjea (misma regla que el uso manual)")]
    public async Task Canje_NotaDeOrigenNoConcretado_SeRechazaSinEfectos()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, 500m, estadoOrigen: EstadoVenta.Cotizacion);
        var venta = await SembrarVentaAsync(e, nc);

        var mensaje = await ConfirmarRechazadaAsync(venta);

        Assert.Contains("no se concretó", mensaje);
        await AssertSinEfectosAsync(e, nc, venta, 0m);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await Servicio(scope).ObtenerNotasCreditoDisponiblesAsync(e.ClienteId));
    }

    [Fact(DisplayName = "H4-E2 [NC-06] venta con medio Nota de crédito sin nota seleccionada: se rechaza")]
    public async Task Canje_SinNotaSeleccionada_SeRechaza()
    {
        var e = await SembrarAsync();
        var venta = await SembrarVentaAsync(e, null);

        var mensaje = await ConfirmarRechazadaAsync(venta);

        Assert.Contains("nota de crédito", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoVenta.Cotizacion, (await VentaAsync(venta)).Estado);
        Assert.Empty(await MovimientosAsync(venta));
    }

    [Fact(DisplayName = "H4-E2 [NC-07] dos ventas con la misma nota: la primera aplica, la segunda se rechaza (no se usa dos veces)")]
    public async Task Canje_DosVentasMismaNota_SoloLaPrimeraAplica()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 100m);
        var v1 = await SembrarVentaAsync(e, nc);
        var v2 = await SembrarVentaAsync(e, nc);

        Assert.True(await ConfirmarAsync(v1));
        var mensaje = await ConfirmarRechazadaAsync(v2);

        Assert.Contains("no admite usos", mensaje);
        Assert.Equal(100m, (await NotaAsync(nc)).MontoUtilizado);
        Assert.Single(await UsosAsync(nc));
        Assert.Equal(EstadoVenta.Cotizacion, (await VentaAsync(v2)).Estado);
        Assert.Empty(await MovimientosAsync(v2));
        Assert.Equal(48m, await StockAsync(e.ProductoId));    // solo descontó la primera
    }

    [Fact(DisplayName = "H4-E2 [NC-08] confirmar dos veces la misma venta no descuenta dos veces")]
    public async Task Canje_ReconfirmarLaMismaVenta_NoDescuentaDeNuevo()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m);
        var venta = await SembrarVentaAsync(e, nc);

        Assert.True(await ConfirmarAsync(venta));
        await ConfirmarRechazadaAsync(venta);               // ya está Confirmada

        Assert.Equal(100m, (await NotaAsync(nc)).MontoUtilizado);
        Assert.Single(await UsosAsync(nc));
    }

    [Fact(DisplayName = "H4-E2 [NC-09] consumo atómico directo: reintento sobre la misma venta es idempotente")]
    public async Task Consumir_MismaVentaDosVeces_Idempotente()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m);
        var ventaId = await SembrarVentaAsync(e, nc);

        for (var i = 0; i < 2; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var venta = await ctx.Ventas.SingleAsync(v => v.Id == ventaId);
            await NotaCreditoCanje.ConsumirAsync(ctx, venta, "t");
        }

        Assert.Equal(100m, (await NotaAsync(nc)).MontoUtilizado);
        Assert.Single(await UsosAsync(nc));
    }

    private async Task AssertSinEfectosAsync(Escenario e, int nc, int venta, decimal utilizadoEsperado)
    {
        var v = await VentaAsync(venta);
        Assert.Equal(EstadoVenta.Cotizacion, v.Estado);
        Assert.Equal(utilizadoEsperado, (await NotaAsync(nc)).MontoUtilizado);
        Assert.Empty(await UsosAsync(nc));
        Assert.Empty(await MovimientosAsync(venta));
        Assert.Equal(50m, await StockAsync(e.ProductoId));
    }

    // ------------------------------------------------------------------ cancelación

    [Fact(DisplayName = "H4-E2 [NC-10] cancelar la venta restituye el saldo de la nota, revierte la caja y no se duplica")]
    public async Task Cancelar_RestituyeSaldo_RevierteCajaYEsIdempotente()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 100m);
        var venta = await SembrarVentaAsync(e, nc);
        Assert.True(await ConfirmarAsync(venta));
        Assert.Equal(EstadoNotaCredito.UtilizadaTotalmente, (await NotaAsync(nc)).Estado);

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await Servicio(scope).CancelarVentaAsync(venta, "arrepentimiento"));

        var nota = await NotaAsync(nc);
        Assert.Equal(0m, nota.MontoUtilizado);
        Assert.Equal(100m, nota.MontoDisponible);
        Assert.Equal(EstadoNotaCredito.Vigente, nota.Estado);
        Assert.Contains("Restituido", nota.Observaciones);

        var uso = Assert.Single(await UsosAsync(nc));
        Assert.NotNull(uso.FechaReversion);
        Assert.Equal("arrepentimiento", uso.MotivoReversion);

        var movs = await MovimientosAsync(venta);
        Assert.Equal(2, movs.Count);
        Assert.Equal(TipoMovimientoCaja.Egreso, movs[1].Tipo);
        Assert.Equal(ConceptoMovimientoCaja.ReversionVenta, movs[1].Concepto);
        Assert.Equal(100m, movs[1].Monto);
        Assert.Equal(EstadoVenta.Cancelada, (await VentaAsync(venta)).Estado);
        Assert.Equal(50m, await StockAsync(e.ProductoId));

        // Idempotencia: revertir otra vez (reintento) no devuelve saldo de más.
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var v = await ctx.Ventas.SingleAsync(x => x.Id == venta);
            Assert.Equal(0m, await NotaCreditoCanje.RevertirAsync(ctx, v, "otra vez", "t"));
        }
        Assert.Equal(0m, (await NotaAsync(nc)).MontoUtilizado);
    }

    [Fact(DisplayName = "SEG-BAJA: canje, cancelación y restitución de nota de crédito dejan eventos de auditoría de seguridad")]
    public async Task Canje_Cancelacion_Restitucion_RegistranEventosDeAuditoria()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 100m);
        var venta = await SembrarVentaAsync(e, nc);
        Assert.True(await ConfirmarAsync(venta));

        var uso = Assert.Single(await EventosAsync("NotaCredito:" + nc));
        Assert.Equal("UsarNotaCredito", uso.Accion);
        Assert.Equal("NotasCredito", uso.Modulo);
        Assert.Contains("Monto=100", uso.Detalle);

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await Servicio(scope).CancelarVentaAsync(venta, "arrepentimiento"));

        var eventosNota = await EventosAsync("NotaCredito:" + nc);
        Assert.Contains(eventosNota, x => x.Accion == "RestituirNotaCredito" && x.Detalle!.Contains("Monto=100"));
        var cancelacion = Assert.Single(await EventosAsync("Venta:" + venta));
        Assert.Equal("CancelarVenta", cancelacion.Accion);
        Assert.Contains("arrepentimiento", cancelacion.Detalle);
        Assert.Contains("EstadoPrevio=Confirmada", cancelacion.Detalle);
    }

    private async Task<List<SeguridadEventoAuditoria>> EventosAsync(string entidad)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SeguridadEventosAuditoria
            .AsNoTracking().Where(x => x.Entidad == entidad).OrderBy(x => x.Id).ToListAsync();
    }

    [Fact(DisplayName = "H4-E2 [NC-11] cancelar tras un uso parcial restituye solo lo canjeado")]
    public async Task Cancelar_NotaConRemanente_RestituyeSoloLoCanjeado()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m, utilizado: 50m, estado: EstadoNotaCredito.UtilizadaParcialmente);
        var venta = await SembrarVentaAsync(e, nc);
        Assert.True(await ConfirmarAsync(venta));
        Assert.Equal(150m, (await NotaAsync(nc)).MontoUtilizado);

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await Servicio(scope).CancelarVentaAsync(venta, "x"));

        var nota = await NotaAsync(nc);
        Assert.Equal(50m, nota.MontoUtilizado);              // el uso manual previo no se toca
        Assert.Equal(EstadoNotaCredito.UtilizadaParcialmente, nota.Estado);
    }

    // ------------------------------------------------------------------ otros medios / devolución

    [Fact(DisplayName = "H4-E2 [NC-12] una venta con otro medio ignora la nota de crédito: no consume saldo")]
    public async Task OtroMedio_NoConsumeNota()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m);
        var venta = await SembrarVentaAsync(e, nc, tipoPago: TipoPago.Efectivo);

        Assert.True(await ConfirmarAsync(venta));

        Assert.Equal(0m, (await NotaAsync(nc)).MontoUtilizado);
        Assert.Empty(await UsosAsync(nc));
        var mov = Assert.Single(await MovimientosAsync(venta));
        Assert.Equal(TipoPago.Efectivo, mov.TipoPago);
    }

    [Fact(DisplayName = "H4-E2 [NC-13] Crear y Editar validan la nota (cliente, saldo) y descartan la selección con otro medio")]
    public async Task CrearYEditar_ValidanLaNota()
    {
        var e = await SembrarAsync();
        var nc = await SembrarNotaAsync(e, monto: 500m);
        var chica = await SembrarNotaAsync(e, monto: 10m);

        VentaViewModel Vm(int? notaId, TipoPago medio) => new()
        {
            ClienteId = e.ClienteId, TipoPago = medio, NotaCreditoId = notaId, FechaVenta = DateTime.UtcNow,
            IdempotencyKey = Guid.NewGuid(),
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = e.ProductoId, Cantidad = 2, PrecioUnitario = 50m, Descuento = 0m }
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var s = Servicio(scope);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.CreateAsync(Vm(chica, TipoPago.NotaCredito)));
            Assert.Contains("no cubre el total", ex.Message);
        }

        int ventaId;
        using (var scope = _factory.Services.CreateScope())
            ventaId = (await Servicio(scope).CreateAsync(Vm(nc, TipoPago.NotaCredito))).Id;
        var creada = await VentaAsync(ventaId);
        Assert.Equal(nc, creada.NotaCreditoId);
        Assert.Equal(EstadoVenta.Cotizacion, creada.Estado);
        Assert.Equal(0m, (await NotaAsync(nc)).MontoUtilizado);       // crear NO consume

        using (var scope = _factory.Services.CreateScope())
        {
            var vm = Vm(nc, TipoPago.Efectivo);
            int otraId = (await Servicio(scope).CreateAsync(vm)).Id;
            Assert.Null((await VentaAsync(otraId)).NotaCreditoId);   // otro medio: la selección se descarta
        }
    }

    [Fact(DisplayName = "H4-E2 [NC-14] la devolución de una venta pagada con nota no reembolsa efectivo")]
    public void Devolucion_VentaPagadaConNota_NoPermiteReembolsoEnCaja()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
        Assert.False(svc.PermiteImpactoCaja(TipoPago.NotaCredito));
        Assert.True(svc.PermiteImpactoCaja(TipoPago.Efectivo));
    }

    [Fact(DisplayName = "H4-E2 [NC-15] ObtenerNotasCreditoDisponibles lista solo las vigentes, con saldo y no vencidas del cliente")]
    public async Task Disponibles_SoloVigentesDelCliente()
    {
        var e = await SembrarAsync();
        var ok = await SembrarNotaAsync(e, 200m, utilizado: 50m, estado: EstadoNotaCredito.UtilizadaParcialmente);
        await SembrarNotaAsync(e, 200m, estado: EstadoNotaCredito.Cancelada);
        await SembrarNotaAsync(e, 200m, vence: DateTime.UtcNow.AddDays(-2));
        await SembrarNotaAsync(e, 200m, utilizado: 200m, estado: EstadoNotaCredito.UtilizadaTotalmente);

        using var scope = _factory.Services.CreateScope();
        var lista = await Servicio(scope).ObtenerNotasCreditoDisponiblesAsync(e.ClienteId);

        var unica = Assert.Single(lista);
        Assert.Equal(ok, unica.Id);
        Assert.Equal(150m, unica.MontoDisponible);
    }
}
