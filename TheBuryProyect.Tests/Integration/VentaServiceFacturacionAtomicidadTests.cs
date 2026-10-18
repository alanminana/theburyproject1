using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

file sealed class StubCurrentUserFacturacionAtomica : TheBuryProject.Services.Interfaces.ICurrentUserService
{
    public string GetUsername() => "testfact";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "testfact@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

/// <summary>Falla (una excepción real de persistencia) al guardar un MovimientoCaja, si está armado.</summary>
internal sealed class FallaAlGuardarMovimientoCajaInterceptor : SaveChangesInterceptor
{
    public bool Armado { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Armado && eventData.Context!.ChangeTracker.Entries<MovimientoCaja>().Any(e => e.State == EntityState.Added))
            throw new DbUpdateException("Falla inyectada al guardar el movimiento de caja.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// FIN-FACT-P0 (H-FACT-1). Contratos de atomicidad de <c>FacturarVentaAsync</c> con <c>CajaService</c> REAL sobre
/// SQLite: factura, estado de la venta e ingreso de respaldo se confirman o se revierten juntos; no se duplica el
/// ingreso; las ventas a crédito no asientan ingreso inmediato; una transacción ambiental no se confirma ni se
/// revierte desde adentro. La serialización contra la cancelación (locks) sólo se puede observar en SQL Server:
/// ver <c>FinResidualSqlServerTests.R10_*</c>.
/// </summary>
public class VentaServiceFacturacionAtomicidadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FallaAlGuardarMovimientoCajaInterceptor _falla = new();
    private readonly VentaService _service;
    private readonly AperturaCaja _apertura;

    private static int _counter = 7000;

    public VentaServiceFacturacionAtomicidadTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_falla)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

        var caja = new Caja { Codigo = "FA01", Nombre = "Caja facturacion", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        _apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "testfact", MontoInicial = 0m, Cerrada = false };
        _context.AperturasCaja.Add(_apertura);
        _context.SaveChanges();

        var cajaService = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, null!);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!, null!, null!,
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserFacturacionAtomica(),
            null!,
            cajaService,
            null!,
            new StubContratoVentaCreditoService(true),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<int> SeedVentaAsync(TipoPago tipoPago, bool conIngreso, decimal total = 100m)
    {
        var s = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente { Nombre = "Fact", Apellido = "Atom" + s, NumeroDocumento = "9" + s };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id, Numero = "VA" + s, Estado = EstadoVenta.Confirmada, TipoPago = tipoPago,
            Subtotal = total, Total = total, AperturaCajaId = _apertura.Id,
            RequiereAutorizacion = false, EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        if (conIngreso)
        {
            _context.MovimientosCaja.Add(new MovimientoCaja
            {
                AperturaCajaId = _apertura.Id, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaEfectivo,
                EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, TipoPago = TipoPago.Efectivo, Monto = total,
                Descripcion = "Venta " + venta.Numero, Referencia = venta.Numero, ReferenciaId = venta.Id, VentaId = venta.Id,
                Usuario = "testfact", FechaMovimiento = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        _context.ChangeTracker.Clear();
        return venta.Id;
    }

    private Task<bool> FacturarAsync(int ventaId) =>
        _service.FacturarVentaAsync(ventaId, new FacturaViewModel { VentaId = ventaId, Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

    private async Task<(EstadoVenta estado, int facturas, int ingresos, decimal montoIngresos)> EstadoAsync(int ventaId)
    {
        var estado = await _context.Ventas.AsNoTracking().Where(v => v.Id == ventaId).Select(v => v.Estado).FirstAsync();
        var facturas = await _context.Facturas.AsNoTracking().CountAsync(f => f.VentaId == ventaId && !f.IsDeleted);
        var ingresos = await _context.MovimientosCaja.AsNoTracking()
            .Where(m => m.VentaId == ventaId && m.Tipo == TipoMovimientoCaja.Ingreso && !m.IsDeleted).ToListAsync();
        return (estado, facturas, ingresos.Count, ingresos.Sum(m => m.Monto));
    }

    // ------------------------------------------------------------------ 1. no duplica

    [Fact]
    public async Task Facturar_VentaConIngresoYaRegistrado_NoDuplicaElIngreso()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: true);

        Assert.True(await FacturarAsync(id));

        var (estado, facturas, ingresos, monto) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Facturada, estado);
        Assert.Equal(1, facturas);
        Assert.Equal(1, ingresos);
        Assert.Equal(100m, monto);
    }

    // ------------------------------------------------------------------ 2. venta legada

    [Fact]
    public async Task Facturar_VentaLegadaSinIngreso_CreaExactamenteUnIngreso()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        Assert.True(await FacturarAsync(id));

        var (estado, facturas, ingresos, monto) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Facturada, estado);
        Assert.Equal(1, facturas);
        Assert.Equal(1, ingresos);
        Assert.Equal(100m, monto);
    }

    // ------------------------------------------------------------------ 5. rollback coherente ante falla

    [Fact]
    public async Task Facturar_FallaElRegistroDelIngreso_RevierteFacturaYEstado()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        _falla.Armado = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        var (estado, facturas, ingresos, _) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Confirmada, estado);
        Assert.Equal(0, facturas);
        Assert.Equal(0, ingresos);
        Assert.Null(_context.Database.CurrentTransaction);
        // Nada quedó pendiente en el tracker: un SaveChanges posterior del mismo contexto no re-persiste la factura.
        Assert.Empty(_context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged));
        await _context.SaveChangesAsync();
        Assert.Equal(0, (await EstadoAsync(id)).facturas);

        // Reintento sin la falla: se factura una sola vez, con un solo ingreso.
        Assert.True(await FacturarAsync(id));
        var final = await EstadoAsync(id);
        Assert.Equal((EstadoVenta.Facturada, 1, 1), (final.estado, final.facturas, final.ingresos));
    }

    // ------------------------------------------------------------------ 6. repetición secuencial

    [Fact]
    public async Task Facturar_DosVeces_LaSegundaSeRechazaSinFacturaNiIngresoExtra()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        Assert.True(await FacturarAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => FacturarAsync(id));

        var (estado, facturas, ingresos, monto) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Facturada, estado);
        Assert.Equal(1, facturas);
        Assert.Equal(1, ingresos);
        Assert.Equal(100m, monto);
    }

    // ------------------------------------------------------------------ 7. caja cerrada durante la operación

    [Fact]
    public async Task Facturar_CajaCerradaAntesDelIngresoDeRespaldo_NoDejaCambiosParciales()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        // La caja se cierra DESPUÉS de que Facturar la validó y ANTES de que asiente el ingreso de respaldo
        // (en SQL Server lo impide el lock de apertura; acá se fuerza el peor caso).
        await using var cierre = new CierraAperturaAlGuardarFactura(_connection, _apertura.Id);
        var contexto = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(cierre).Options);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var servicio = new VentaService(
            contexto, mapper, NullLogger<VentaService>.Instance, null!, null!, null!, new VentaValidator(),
            new VentaNumberGenerator(contexto, NullLogger<VentaNumberGenerator>.Instance), new PrecioVigenteResolver(contexto),
            new StubCurrentUserFacturacionAtomica(), null!, new CajaService(contexto, mapper, NullLogger<CajaService>.Instance, null!),
            null!, new StubContratoVentaCreditoService(true), new StubConfiguracionPagoServiceVenta());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.FacturarVentaAsync(id, new FacturaViewModel { VentaId = id, Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
        Assert.Contains("caja cerrada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(cierre.Disparado);

        // Todo se revirtió: venta Confirmada, sin factura, sin ingreso, y la apertura sigue como estaba.
        var (estado, facturas, ingresos, _) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Confirmada, estado);
        Assert.Equal(0, facturas);
        Assert.Equal(0, ingresos);
        Assert.False(await _context.AperturasCaja.AsNoTracking().Where(a => a.Id == _apertura.Id).Select(a => a.Cerrada).FirstAsync());
    }

    // ------------------------------------------------------------------ 8. crédito: sin ingreso inmediato

    [Fact]
    public async Task Facturar_VentaACreditoPersonal_NoRegistraIngresoInmediato()
    {
        var id = await SeedVentaAsync(TipoPago.CreditoPersonal, conIngreso: false);

        Assert.True(await FacturarAsync(id));

        var (estado, facturas, ingresos, _) = await EstadoAsync(id);
        Assert.Equal(EstadoVenta.Facturada, estado);
        Assert.Equal(1, facturas);
        Assert.Equal(0, ingresos);
    }

    // ------------------------------------------------------------------ 9. transacción ambiental

    [Fact]
    public async Task Facturar_ConTransaccionAmbiental_NoLaConfirmaNiLaRevierte()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        await using var ambiental = await _context.Database.BeginTransactionAsync();
        Assert.True(await FacturarAsync(id));

        // Facturar terminó pero la transacción ajena sigue abierta y es la misma.
        Assert.Equal(ambiental.TransactionId, _context.Database.CurrentTransaction!.TransactionId);
        Assert.Equal((EstadoVenta.Facturada, 1, 1), Resumen(await EstadoAsync(id)));

        await ambiental.RollbackAsync();

        // El rollback del dueño de la transacción deshace TODO lo que hizo Facturar (no había commit propio).
        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(await EstadoAsync(id)));
    }

    [Fact]
    public async Task Facturar_ConTransaccionAmbientalYFalla_NoRevierteLaAjenaAlSalir()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        await using var ambiental = await _context.Database.BeginTransactionAsync();
        _falla.Armado = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        // La transacción ajena sigue viva (Facturar no la cerró): su dueño decide.
        Assert.NotNull(_context.Database.CurrentTransaction);
        await ambiental.RollbackAsync();
        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(await EstadoAsync(id)));
    }

    // ------------------------------------------------------------------ FIN-FACT-HARDENING-P0 / riesgo 1

    [Fact]
    public async Task Facturar_VentaYaRastreadaYFalla_TrackerCoherenteConLaBaseYSinReaparicion()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        // El llamador ya tenía la venta (con sus colecciones) rastreada antes de facturar.
        var venta = await _context.Ventas.Include(v => v.Facturas).Include(v => v.Detalles).FirstAsync(v => v.Id == id);
        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        _falla.Armado = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        // Misma instancia rastreada: coherente con la base (no quedó "Facturada" en memoria ni con una factura fantasma).
        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        Assert.Null(venta.FechaFacturacion);
        Assert.Empty(venta.Facturas);
        Assert.Empty(_context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged));
        Assert.Empty(_context.ChangeTracker.Entries<Factura>());

        // Un SaveChanges posterior (y un cambio ajeno posterior) no hace reaparecer nada de lo revertido.
        venta.Observaciones = "tocada despues";
        await _context.SaveChangesAsync();
        var final = await EstadoAsync(id);
        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(final));
        Assert.Equal("tocada despues", await _context.Ventas.AsNoTracking().Where(v => v.Id == id).Select(v => v.Observaciones).FirstAsync());

        // Y facturar de nuevo sobre la misma instancia rastreada funciona una sola vez.
        Assert.True(await FacturarAsync(id));
        Assert.Equal((EstadoVenta.Facturada, 1, 1), Resumen(await EstadoAsync(id)));
    }

    [Fact]
    public async Task Facturar_VentaRastreadaConCambioPendienteDelLlamadorYFalla_ConservaSoloElCambioDelLlamador()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var venta = await _context.Ventas.Include(v => v.Facturas).FirstAsync(v => v.Id == id);
        venta.Observaciones = "pendiente del llamador";   // Modified, sin guardar
        _falla.Armado = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        Assert.Equal("pendiente del llamador", venta.Observaciones);
        Assert.Equal(EntityState.Modified, _context.Entry(venta).State);
        var modificadas = _context.Entry(venta).Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList();
        Assert.Equal(new[] { nameof(Venta.Observaciones) }, modificadas);   // solo lo del llamador, no se ensancha el UPDATE

        await _context.SaveChangesAsync();
        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(await EstadoAsync(id)));
        Assert.Equal("pendiente del llamador", await _context.Ventas.AsNoTracking().Where(v => v.Id == id).Select(v => v.Observaciones).FirstAsync());
    }

    // ------------------------------------------------------------------ FIN-FACT-HARDENING-P0 / riesgo 2

    [Fact]
    public async Task Facturar_TransaccionAmbientalFallaElLlamadorContinuaYConfirma_NoQuedaFacturaSinAsiento()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var otraId = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: true);

        await using var ambiental = await _context.Database.BeginTransactionAsync();

        // Trabajo previo del llamador en SU transacción: debe sobrevivir.
        var otra = await _context.Ventas.FirstAsync(v => v.Id == otraId);
        otra.Observaciones = "previo del llamador";
        await _context.SaveChangesAsync();

        _falla.Armado = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));   // el llamador captura…
        _falla.Armado = false;

        // …la transacción sigue siendo la suya y está usable: nada de la facturación quedó adentro.
        Assert.NotNull(_context.Database.CurrentTransaction);
        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(await EstadoAsync(id)));

        await ambiental.CommitAsync();   // …y confirma

        Assert.Equal((EstadoVenta.Confirmada, 0, 0), Resumen(await EstadoAsync(id)));
        Assert.Equal("previo del llamador", await _context.Ventas.AsNoTracking().Where(v => v.Id == otraId).Select(v => v.Observaciones).FirstAsync());
    }

    [Fact]
    public async Task Facturar_TransaccionAmbientalReintentaTrasLaFalla_FacturaUnaSolaVezConSuIngreso()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);

        await using var ambiental = await _context.Database.BeginTransactionAsync();
        _falla.Armado = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        Assert.True(await FacturarAsync(id));
        await ambiental.CommitAsync();

        var (estado, facturas, ingresos, monto) = await EstadoAsync(id);
        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), (estado, facturas, ingresos, monto));
    }

    private static (EstadoVenta, int, int) Resumen((EstadoVenta estado, int facturas, int ingresos, decimal monto) r) =>
        (r.estado, r.facturas, r.ingresos);

    /// <summary>Cierra la apertura (UPDATE directo) justo después de guardar la Factura, antes del ingreso de respaldo.</summary>
    private sealed class CierraAperturaAlGuardarFactura(SqliteConnection conexion, int aperturaId) : SaveChangesInterceptor, IAsyncDisposable
    {
        public bool Disparado { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Disparado && eventData.Context!.ChangeTracker.Entries<Factura>().Any())
            {
                Disparado = true;
                await eventData.Context.Database.ExecuteSqlRawAsync(
                    "UPDATE AperturasCaja SET Cerrada = 1 WHERE Id = {0}", [aperturaId], cancellationToken);
            }
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
