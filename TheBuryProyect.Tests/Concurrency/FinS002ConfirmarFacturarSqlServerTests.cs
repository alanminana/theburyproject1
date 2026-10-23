using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Integration;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// PROMPT 86 — FIN-S-002: pruebas ADVERSARIALES de Confirmar/Facturar contra SQL Server real (efimero, solo CI).
//
// Servicios reales (VentaService, CreditoService, CajaService, ValidacionVentaService) sobre el catalogo
// "qa_fin_conc_s002". Cada prueba comprueba el ESTADO FINAL en la base (credito, venta, cuotas, stock, facturas y
// movimientos de caja) leido con un contexto nuevo: no basta con que el servicio responda un error.
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// Sin ese gate los escenarios se OMITEN; el workflow fin-s-002-sqlserver.yml falla si alguno no figura Passed.
// ---------------------------------------------------------------------------------------------

public sealed class FinS002SqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_s002";

    protected override void ConfigurarServicios(IServiceCollection services)
    {
        // El contrato real exige plantilla y archivo en disco, ajenos a esta PR (igual que FIN-ANT-P0).
        services.RemoveAll<IContratoVentaCreditoService>();
        services.AddScoped<IContratoVentaCreditoService>(_ => DispatchProxy.Create<IContratoVentaCreditoService, ContratoSiempreGeneradoProxy>());

        // La validacion real de credito exige documentos verificados, CUIL y consulta BCRA/Veraz (red externa). Se
        // reemplaza por la misma validacion "viable" que usan los tests de overposting; todo lo demas (VentaService,
        // CreditoService, caja, stock, facturas) es real y la base es SQL Server.
        services.RemoveAll<IValidacionVentaService>();
        services.AddScoped<IValidacionVentaService, StubValidacionVentaOverposting>();
    }
}

public class FinS002ConfirmarFacturarSqlServerTests : IClassFixture<FinS002SqlHostFixture>, IAsyncLifetime
{
    private const decimal Precio = 10_000m;
    private const decimal Financiado = 8_000m;
    private const int Cuotas = 12;

    private readonly FinS002SqlHostFixture _fx;

    public FinS002ConfirmarFacturarSqlServerTests(FinS002SqlHostFixture fx) => _fx = fx;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>El vendedor de la venta (usuario actual del harness) debe existir: Ventas.VendedorUserId tiene FK a AspNetUsers.</summary>
    public async Task InitializeAsync()
    {
        if (ConcEnv.SkipReason() != null) return;
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = new ConcCurrentUser().GetUserId();
        if (await ctx.Users.AnyAsync(u => u.Id == userId)) return;
        ctx.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = ConcCurrentUser.Usuario, NormalizedUserName = ConcCurrentUser.Usuario.ToUpperInvariant(),
            Email = "qaconc@example.invalid", NormalizedEmail = "QACONC@EXAMPLE.INVALID", Activo = true, RowVersion = new byte[8]
        });
        await ctx.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ siembra

    private async Task<int> SembrarClienteAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "S002", Apellido = "QA" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private async Task<int> SembrarProductoAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(categoria, marca);
        await ctx.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = Precio, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        ctx.Productos.Add(producto);
        if (!await ctx.ConfiguracionCreditoPersonalCuotas.AnyAsync(c => c.CantidadCuotas == Cuotas && c.Activo))
            ctx.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
            {
                CantidadCuotas = Cuotas, TasaMensual = 3m, Activo = true, Orden = Cuotas
            });
        await ctx.SaveChangesAsync();
        return producto.Id;
    }

    private async Task<int> SembrarCreditoAjenoAsync(int clienteAjenoId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credito = new Credito
        {
            ClienteId = clienteAjenoId, Numero = "AJ" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Estado = EstadoCredito.Activo, SaldoPendiente = 1_000_000m, MontoAprobado = 1_000_000m, MontoSolicitado = 1_000_000m
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();
        return credito.Id;
    }

    private static VentaViewModel Vm(int clienteId, int productoId) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        TipoPago = TipoPago.CreditoPersonal,
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = productoId, Cantidad = 1, PrecioUnitario = Precio, Subtotal = Precio }
        }
    };

    // ------------------------------------------------------------------ operaciones (scope propio, como una request)

    private async Task<T> ConServicioAsync<T>(Func<IVentaService, Task<T>> operacion)
    {
        using var scope = _fx.Services.CreateScope();
        return await operacion(scope.ServiceProvider.GetRequiredService<IVentaService>());
    }

    private async Task<VentaViewModel> CrearAsync(VentaViewModel vm) =>
        await ConServicioAsync(s => s.CreateAsync(vm));

    private async Task<byte[]> RowVersionAsync(int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Ventas.AsNoTracking().Where(v => v.Id == ventaId).Select(v => v.RowVersion).FirstAsync();
    }

    private async Task ConfigurarLegitimoAsync(int creditoId, int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICreditoService>().ConfigurarCreditoAsync(new ConfiguracionCreditoComando
        {
            CreditoId = creditoId,
            VentaId = ventaId,
            Monto = Precio,
            Anticipo = Precio - Financiado,
            CantidadCuotas = Cuotas,
            TasaMensual = 3m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo = MetodoCalculoCredito.AutomaticoPorCliente,
            FuenteConfiguracion = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas = 1,
            CuotasMaxPermitidas = Cuotas
        });
    }

    // ------------------------------------------------------------------ estado final en la base

    private sealed record Foto(
        Venta Venta, Credito? Credito, int CuotasDelCredito, decimal Stock, int Facturas, int MovimientosVenta, int MovimientosCredito);

    private async Task<Foto> FotoAsync(int ventaId, int productoId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var venta = await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == ventaId);
        var credito = venta.CreditoId.HasValue
            ? await ctx.Creditos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == venta.CreditoId.Value)
            : null;
        return new Foto(
            venta,
            credito,
            credito == null ? 0 : await ctx.Cuotas.AsNoTracking().CountAsync(c => c.CreditoId == credito.Id && !c.IsDeleted),
            await ctx.Productos.AsNoTracking().Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync(),
            await ctx.Facturas.AsNoTracking().CountAsync(f => f.VentaId == ventaId && !f.IsDeleted),
            await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.VentaId == ventaId && !m.IsDeleted),
            credito == null ? 0 : await ctx.MovimientosCaja.AsNoTracking()
                .CountAsync(m => m.Concepto == ConceptoMovimientoCaja.AnticipoCredito && m.ReferenciaId == credito.Id && !m.IsDeleted));
    }

    private async Task<(EstadoCredito Estado, decimal Saldo, int Cuotas, int VentasAsociadas)> FotoCreditoAjenoAsync(int creditoId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await ctx.Creditos.AsNoTracking().FirstAsync(x => x.Id == creditoId);
        return (c.Estado, c.SaldoPendiente,
            await ctx.Cuotas.AsNoTracking().CountAsync(q => q.CreditoId == creditoId),
            await ctx.Ventas.AsNoTracking().CountAsync(v => v.CreditoId == creditoId));
    }

    private static FacturaViewModel NuevaFactura() => new() { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

    // ================================================================== escenarios

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-01] flujo legitimo: crear, configurar, confirmar y facturar con estado final consistente")]
    public async Task CF01_FlujoLegitimo_ConfigurarConfirmarFacturar()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();

        var creada = await CrearAsync(Vm(clienteId, productoId));
        Assert.NotNull(creada.CreditoId);
        var antes = await FotoAsync(creada.Id, productoId);
        Assert.Equal(clienteId, antes.Credito!.ClienteId);
        Assert.Null(antes.Venta.FechaConfiguracionCredito);   // aun sin configuracion legitima
        Assert.Equal(0, antes.CuotasDelCredito);

        await ConfigurarLegitimoAsync(creada.CreditoId!.Value, creada.Id);
        var configurada = await FotoAsync(creada.Id, productoId);
        Assert.Equal(EstadoCredito.Configurado, configurada.Credito!.Estado);
        Assert.NotNull(configurada.Venta.FechaConfiguracionCredito);   // la fija el servidor al configurar

        Assert.True(await ConServicioAsync(s => s.ConfirmarVentaCreditoAsync(creada.Id)));
        var confirmada = await FotoAsync(creada.Id, productoId);
        Assert.Equal(EstadoVenta.Confirmada, confirmada.Venta.Estado);
        Assert.Equal(EstadoCredito.Generado, confirmada.Credito!.Estado);
        Assert.Equal(Cuotas, confirmada.CuotasDelCredito);
        Assert.Equal(9m, confirmada.Stock);
        Assert.Equal(1, confirmada.MovimientosCredito);   // un solo anticipo asentado
        Assert.Equal(0, confirmada.Facturas);

        Assert.True(await ConServicioAsync(s => s.FacturarVentaAsync(creada.Id, NuevaFactura())));
        var facturada = await FotoAsync(creada.Id, productoId);
        Assert.Equal(EstadoVenta.Facturada, facturada.Venta.Estado);
        Assert.Equal(1, facturada.Facturas);
        Assert.Equal(Cuotas, facturada.CuotasDelCredito);   // facturar no regenera cuotas
        Assert.Equal(1, facturada.MovimientosCredito);       // ni duplica el anticipo
        Assert.Equal(9m, facturada.Stock);                   // ni vuelve a descontar stock
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-02] CreditoId de otro cliente en Create: se rechaza y no queda venta ni cambia el credito ajeno")]
    public async Task CF02_CreateConCreditoDeOtroCliente_NoPersisteNiTocaElAjeno()
    {
        var clienteId = await SembrarClienteAsync();
        var otroId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();
        var ajeno = await SembrarCreditoAjenoAsync(otroId);
        var antes = await FotoCreditoAjenoAsync(ajeno);

        var vm = Vm(clienteId, productoId);
        vm.CreditoId = ajeno;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearAsync(vm));

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ctx.Ventas.AnyAsync(v => v.ClienteId == clienteId));
        Assert.False(await ctx.Creditos.AnyAsync(c => c.ClienteId == clienteId));
        Assert.Equal(antes, await FotoCreditoAjenoAsync(ajeno));
        Assert.Equal(10m, await ctx.Productos.Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync());
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-03] CreditoId inexistente en Create: se rechaza y no persiste nada")]
    public async Task CF03_CreateConCreditoInexistente_NoPersiste()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();

        var vm = Vm(clienteId, productoId);
        vm.CreditoId = 2_000_000_000;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearAsync(vm));

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ctx.Ventas.AnyAsync(v => v.ClienteId == clienteId));
        Assert.False(await ctx.Creditos.AnyAsync(c => c.ClienteId == clienteId));
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-08] CreditoId de un credito PROPIO ya existente en Create: no se engancha, la venta recibe su credito nuevo")]
    public async Task CF08_CreateConCreditoPropioExistente_NoSeEngancha()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();
        var existente = await SembrarCreditoAjenoAsync(clienteId);   // credito Activo del MISMO cliente, ajeno a esta venta
        var antes = await FotoCreditoAjenoAsync(existente);

        var vm = Vm(clienteId, productoId);
        vm.CreditoId = existente;
        var creada = await CrearAsync(vm);

        var f = await FotoAsync(creada.Id, productoId);
        Assert.NotNull(f.Venta.CreditoId);
        Assert.NotEqual(existente, f.Venta.CreditoId);                 // la venta NO adopta el credito del POST
        Assert.Equal(EstadoCredito.PendienteConfiguracion, f.Credito!.Estado);   // crea el suyo, pendiente de configuracion
        Assert.Equal(antes, await FotoCreditoAjenoAsync(existente));   // el existente: mismo estado, saldo, cuotas y sin ventas
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-04] CreditoId forjado en Update: la venta conserva su credito, el ajeno no se toca y confirmar usa el propio")]
    public async Task CF04_UpdateConCreditoIdForjado_ConservaElPropio()
    {
        var clienteId = await SembrarClienteAsync();
        var otroId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();
        var ajeno = await SembrarCreditoAjenoAsync(otroId);
        var antesAjeno = await FotoCreditoAjenoAsync(ajeno);

        var creada = await CrearAsync(Vm(clienteId, productoId));
        var propio = creada.CreditoId!.Value;
        Assert.NotEqual(ajeno, propio);

        var vm = Vm(clienteId, productoId);
        vm.CreditoId = ajeno;                                  // forjado
        vm.RowVersion = await RowVersionAsync(creada.Id);
        vm.Observaciones = "update forjado";
        await ConServicioAsync(s => s.UpdateAsync(creada.Id, vm));

        var foto = await FotoAsync(creada.Id, productoId);
        Assert.Equal("update forjado", foto.Venta.Observaciones);   // el update legitimo si se aplico
        Assert.Equal(propio, foto.Venta.CreditoId);                  // pero el credito asociado no cambio
        Assert.Equal(clienteId, foto.Credito!.ClienteId);
        Assert.Equal(antesAjeno, await FotoCreditoAjenoAsync(ajeno));

        await ConfigurarLegitimoAsync(propio, creada.Id);
        Assert.True(await ConServicioAsync(s => s.ConfirmarVentaCreditoAsync(creada.Id)));
        var confirmada = await FotoAsync(creada.Id, productoId);
        Assert.Equal(propio, confirmada.Venta.CreditoId);
        Assert.Equal(EstadoCredito.Generado, confirmada.Credito!.Estado);
        Assert.Equal(antesAjeno, await FotoCreditoAjenoAsync(ajeno));   // ni cuotas ni saldo ni estado del ajeno
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-05] FechaConfiguracionCredito forjada en Create y Update: se ignora y confirmar se rechaza sin efectos")]
    public async Task CF05_FechaConfiguracionForjada_NoHabilitaConfirmar()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();

        var vm = Vm(clienteId, productoId);
        vm.FechaConfiguracionCredito = new DateTime(2020, 1, 1);
        var creada = await CrearAsync(vm);
        Assert.Null((await FotoAsync(creada.Id, productoId)).Venta.FechaConfiguracionCredito);

        var vmUpdate = Vm(clienteId, productoId);
        vmUpdate.FechaConfiguracionCredito = new DateTime(2020, 1, 1);
        vmUpdate.RowVersion = await RowVersionAsync(creada.Id);
        await ConServicioAsync(s => s.UpdateAsync(creada.Id, vmUpdate));
        Assert.Null((await FotoAsync(creada.Id, productoId)).Venta.FechaConfiguracionCredito);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConServicioAsync(s => s.ConfirmarVentaCreditoAsync(creada.Id)));

        var f = await FotoAsync(creada.Id, productoId);
        Assert.NotEqual(EstadoVenta.Confirmada, f.Venta.Estado);
        Assert.NotEqual(EstadoCredito.Generado, f.Credito!.Estado);
        Assert.Equal(0, f.CuotasDelCredito);
        Assert.Equal(10m, f.Stock);
        Assert.Equal(0, f.MovimientosCredito);
        Assert.Equal(0, f.MovimientosVenta);
        Assert.Equal(0, f.Facturas);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-06] confirmar una venta con credito nunca configurado se rechaza sin cuotas, stock ni caja")]
    public async Task CF06_ConfirmarSinConfiguracionLegitima_SeRechazaSinEfectos()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();
        var creada = await CrearAsync(Vm(clienteId, productoId));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConServicioAsync(s => s.ConfirmarVentaCreditoAsync(creada.Id)));
        Assert.Contains("Configurado", ex.Message);

        var f = await FotoAsync(creada.Id, productoId);
        Assert.NotEqual(EstadoVenta.Confirmada, f.Venta.Estado);
        Assert.NotEqual(EstadoCredito.Configurado, f.Credito!.Estado);
        Assert.NotEqual(EstadoCredito.Generado, f.Credito.Estado);
        Assert.Equal(0, f.CuotasDelCredito);
        Assert.Equal(10m, f.Stock);
        Assert.Equal(0, f.MovimientosCredito);
        Assert.Equal(0, f.Facturas);

        // Y facturar tampoco es un atajo: la venta no esta Confirmada.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConServicioAsync(s => s.FacturarVentaAsync(creada.Id, NuevaFactura())));
        Assert.Equal(0, (await FotoAsync(creada.Id, productoId)).Facturas);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 86 [S2-CF-07] facturar una venta Confirmada cuyo credito no cumple las invariantes financieras se rechaza sin factura ni movimientos")]
    public async Task CF07_FacturarConCreditoSinInvariantes_SeRechazaSinEfectos()
    {
        var clienteId = await SembrarClienteAsync();
        var productoId = await SembrarProductoAsync();
        var creada = await CrearAsync(Vm(clienteId, productoId));

        // Estado que el flujo legitimo NO produce (dato legado o manipulado): venta Confirmada a credito cuyo
        // credito nunca se configuro ni genero cuotas.
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Ventas.Where(v => v.Id == creada.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Estado, EstadoVenta.Confirmada));
        }
        var antes = await FotoAsync(creada.Id, productoId);
        Assert.Equal(EstadoVenta.Confirmada, antes.Venta.Estado);
        Assert.Equal(0, antes.CuotasDelCredito);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConServicioAsync(s => s.FacturarVentaAsync(creada.Id, NuevaFactura())));

        var f = await FotoAsync(creada.Id, productoId);
        Assert.Equal(EstadoVenta.Confirmada, f.Venta.Estado);   // no paso a Facturada
        Assert.Equal(0, f.Facturas);
        Assert.Equal(0, f.MovimientosVenta);
        Assert.Equal(0, f.MovimientosCredito);
        Assert.Equal(antes.Credito!.Estado, f.Credito!.Estado);
    }
}
