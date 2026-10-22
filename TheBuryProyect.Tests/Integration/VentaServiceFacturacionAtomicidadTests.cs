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
    private readonly CajaService _cajaService;
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
        _cajaService = cajaService;

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

    // FIN-S-002: una venta a credito Confirmada debe tener su credito generado (cuotas) para poder facturarse.
    private async Task AdjuntarCreditoGeneradoAsync(int ventaId)
    {
        var v = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == ventaId);
        var credito = new Credito
        {
            ClienteId = v.ClienteId!.Value, Numero = "CRF" + ventaId, Estado = EstadoCredito.Generado,
            MontoAprobado = v.Total, MontoSolicitado = v.Total, SaldoPendiente = v.Total, CantidadCuotas = 1
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        _context.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, MontoCapital = v.Total, MontoInteres = 0m, MontoTotal = v.Total,
            FechaVencimiento = DateTime.UtcNow.AddMonths(1), Estado = EstadoCuota.Pendiente
        });
        await _context.SaveChangesAsync();
        await _context.Ventas.Where(x => x.Id == ventaId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditoId, credito.Id));
        _context.ChangeTracker.Clear();
    }

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
        await AdjuntarCreditoGeneradoAsync(id);

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

    // ------------------------------------------------------------------ FIN-FACT-REFERENCIA-P0
    // MovimientoCaja.ReferenciaId es polimórfico: el id de una venta legada puede coincidir con el de una cuota, un crédito,
    // una devolución… El fallback de FacturarVentaAsync no puede dar por "ya registrado" un ingreso ajeno por esa coincidencia.

    private static readonly ConceptoMovimientoCaja[] ConceptosVenta =
    {
        ConceptoMovimientoCaja.VentaEfectivo, ConceptoMovimientoCaja.VentaTarjeta, ConceptoMovimientoCaja.VentaCheque,
        ConceptoMovimientoCaja.VentaTransferencia, ConceptoMovimientoCaja.VentaMercadoPago
    };

    /// <summary>
    /// Cobro de cuota GENUINO (producido por <c>CajaService.RegistrarMovimientoCuotaAsync</c>) de una cuota cuyo
    /// <c>Cuota.Id</c> es exactamente el id de la venta: el movimiento queda con VentaId = null y ReferenciaId = ventaId.
    /// </summary>
    private async Task<(int cuotaId, int creditoId, int movimientoId)> SembrarCobroDeCuotaConElMismoIdAsync(int ventaId)
    {
        var s = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente { Nombre = "Cuota", Apellido = "Ajena" + s, NumeroDocumento = "8" + s };
        var credito = new Credito
        {
            Cliente = cliente, Numero = "CRE-FR" + s, MontoSolicitado = 1_000m, MontoAprobado = 1_000m, TasaInteres = 0m,
            CantidadCuotas = 1, MontoCuota = 1_000m, TotalAPagar = 1_000m, SaldoPendiente = 1_000m,
            Estado = EstadoCredito.Activo, FechaSolicitud = DateTime.UtcNow.AddMonths(-1), PuntajeRiesgoInicial = 100m
        };
        credito.Cuotas.Add(new Cuota
        {
            Id = ventaId, NumeroCuota = 1, MontoCapital = 1_000m, MontoInteres = 0m, MontoTotal = 1_000m, MontoPagado = 0m,
            FechaVencimiento = DateTime.UtcNow.AddDays(30)
        });
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var mov = await _cajaService.RegistrarMovimientoCuotaAsync(ventaId, credito.Numero, 1, 100m, "Efectivo", "testfact");
        Assert.NotNull(mov);
        Assert.Equal(ventaId, mov!.ReferenciaId);
        Assert.Null(mov.VentaId);
        var movId = mov.Id;
        _context.ChangeTracker.Clear();
        return (ventaId, credito.Id, movId);
    }

    private async Task<int> SembrarMovimientoAsync(Action<MovimientoCaja> configurar)
    {
        var m = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, TipoPago = TipoPago.Efectivo, Monto = 100m,
            Usuario = "testfact", FechaMovimiento = DateTime.UtcNow
        };
        configurar(m);
        _context.MovimientosCaja.Add(m);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return m.Id;
    }

    private Task<string> NumeroDeVentaAsync(int ventaId) =>
        _context.Ventas.AsNoTracking().Where(v => v.Id == ventaId).Select(v => v.Numero).FirstAsync();

    /// <summary>Oráculo independiente del código de producción: ingresos de ESTA venta (vínculo directo o histórico con su número).</summary>
    private async Task<List<MovimientoCaja>> IngresosDeLaVentaAsync(int ventaId)
    {
        var numero = await NumeroDeVentaAsync(ventaId);
        return await _context.MovimientosCaja.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimientoCaja.Ingreso && !m.IsDeleted && ConceptosVenta.Contains(m.Concepto)
                     && (m.VentaId == ventaId || (m.VentaId == null && m.ReferenciaId == ventaId && m.Referencia == numero)))
            .ToListAsync();
    }

    private async Task AssertCobroDeCuotaIntactoAsync(int movimientoId, int cuotaId, int creditoId)
    {
        var cobro = await _context.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == movimientoId);
        Assert.Equal(ConceptoMovimientoCaja.CobroCuota, cobro.Concepto);
        Assert.Equal(100m, cobro.Monto);
        Assert.False(cobro.IsDeleted);
        Assert.Null(cobro.VentaId);
        Assert.Equal(cuotaId, cobro.ReferenciaId);
        Assert.Equal(1, await _context.MovimientosCaja.AsNoTracking()
            .CountAsync(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.ReferenciaId == cuotaId));
        var credito = await _context.Creditos.AsNoTracking().FirstAsync(c => c.Id == creditoId);
        Assert.Equal(1_000m, credito.SaldoPendiente);
        Assert.Equal(0m, (await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuotaId)).MontoPagado);
    }

    private static (EstadoVenta, int, int, decimal) Completo((EstadoVenta estado, int facturas, int ingresos, decimal monto) r) =>
        (r.estado, r.facturas, r.ingresos, r.monto);

    // FACT-REF-01
    [Fact]
    public async Task FactRef01_CobroDeCuotaConElMismoId_NoImpideElIngresoDeLaVenta()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);
        Assert.Empty(await IngresosDeLaVentaAsync(id));   // precondición: la venta no tiene ingreso propio

        Assert.True(await FacturarAsync(id));

        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
        var propios = await IngresosDeLaVentaAsync(id);
        Assert.Single(propios);
        Assert.Equal(ConceptoMovimientoCaja.VentaEfectivo, propios[0].Concepto);
        Assert.Equal(id, propios[0].VentaId);
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);
    }

    // FACT-REF-02
    [Fact]
    public async Task FactRef02_IngresoHistoricoLegitimoSinVentaId_NoSeDuplica()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var numero = await NumeroDeVentaAsync(id);
        await SembrarMovimientoAsync(m =>
        {
            m.VentaId = null; m.ReferenciaId = id; m.Referencia = numero; m.Descripcion = "Venta " + numero;
        });

        Assert.True(await FacturarAsync(id));

        Assert.Equal(EstadoVenta.Facturada, (await EstadoAsync(id)).estado);
        Assert.Single(await IngresosDeLaVentaAsync(id));
        Assert.Equal(0, (await EstadoAsync(id)).ingresos);   // no se creó ningún ingreso con VentaId
    }

    // FACT-REF-03
    [Theory]
    [InlineData(ConceptoMovimientoCaja.CobroCuota)]
    [InlineData(ConceptoMovimientoCaja.AnticipoCredito)]
    [InlineData(ConceptoMovimientoCaja.DepositoEfectivo)]
    [InlineData(ConceptoMovimientoCaja.LiquidacionMercadoLibre)]
    [InlineData(ConceptoMovimientoCaja.Otro)]
    public async Task FactRef03_IngresoAjenoConElMismoReferenciaId_NoCuentaComoIngresoDeVenta(ConceptoMovimientoCaja ajeno)
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var numero = await NumeroDeVentaAsync(id);
        // Peor caso: además del mismo ReferenciaId, hasta la Referencia textual coincide; sólo el concepto lo delata.
        var ajenoId = await SembrarMovimientoAsync(m =>
        {
            m.Concepto = ajeno; m.VentaId = null; m.ReferenciaId = id; m.Referencia = numero; m.Descripcion = "ajeno";
        });

        Assert.True(await FacturarAsync(id));

        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
        var intacto = await _context.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == ajenoId);
        Assert.Equal(ajeno, intacto.Concepto);
        Assert.Null(intacto.VentaId);
        Assert.False(intacto.IsDeleted);
    }

    // FACT-REF-04
    [Fact]
    public async Task FactRef04_MismoConceptoYReferenciaDeOtraVenta_NoSuprimeElIngresoCorrecto()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var otra = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var numeroOtra = await NumeroDeVentaAsync(otra);
        await SembrarMovimientoAsync(m =>
        {
            m.VentaId = null; m.ReferenciaId = id; m.Referencia = numeroOtra; m.Descripcion = "Venta " + numeroOtra;
        });

        Assert.True(await FacturarAsync(id));

        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
    }

    // FACT-REF-05
    [Fact]
    public async Task FactRef05_IngresoConVinculoDirecto_SigueReconocidoAunConColision()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: true);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);

        Assert.True(await FacturarAsync(id));

        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
        Assert.Single(await IngresosDeLaVentaAsync(id));
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);
    }

    // FACT-REF-07
    [Fact]
    public async Task FactRef07_ConColisionYFallaAlAsentarElIngreso_RevierteTodoYNoTocaElCobroAjeno()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);
        _falla.Armado = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        Assert.Equal((EstadoVenta.Confirmada, 0, 0, 0m), Completo(await EstadoAsync(id)));
        Assert.Empty(await IngresosDeLaVentaAsync(id));
        Assert.Null(_context.Database.CurrentTransaction);
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);

        // El reintento factura una sola vez y SÍ asienta el ingreso de la venta pese a la colisión.
        Assert.True(await FacturarAsync(id));
        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
    }

    // FACT-REF-08
    [Fact]
    public async Task FactRef08_ConColisionYTransaccionAmbiental_ConservaSavepointsYTrabajoPrevioDelLlamador()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var otraId = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: true);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);

        await using var ambiental = await _context.Database.BeginTransactionAsync();
        var otra = await _context.Ventas.FirstAsync(v => v.Id == otraId);
        otra.Observaciones = "previo del llamador";
        await _context.SaveChangesAsync();

        _falla.Armado = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => FacturarAsync(id));
        _falla.Armado = false;

        Assert.NotNull(_context.Database.CurrentTransaction);
        Assert.Equal((EstadoVenta.Confirmada, 0, 0, 0m), Completo(await EstadoAsync(id)));

        Assert.True(await FacturarAsync(id));   // reintento dentro de la misma transacción ajena
        await ambiental.CommitAsync();

        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
        Assert.Equal("previo del llamador", await _context.Ventas.AsNoTracking().Where(v => v.Id == otraId).Select(v => v.Observaciones).FirstAsync());
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);
    }

    [Fact]
    public async Task FactRef08B_ConColisionYRollbackDelDuenioDeLaTransaccion_DeshaceTodoLoDeLaFacturacion()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);

        await using var ambiental = await _context.Database.BeginTransactionAsync();
        Assert.True(await FacturarAsync(id));
        Assert.Equal((EstadoVenta.Facturada, 1, 1, 100m), Completo(await EstadoAsync(id)));
        await ambiental.RollbackAsync();

        Assert.Equal((EstadoVenta.Confirmada, 0, 0, 0m), Completo(await EstadoAsync(id)));
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);
    }

    // FACT-REF-09
    [Fact]
    public async Task FactRef09_ConColisionYCajaCerradaAntesDelIngreso_NoInsertaNiPersisteParcialmente()
    {
        var id = await SeedVentaAsync(TipoPago.Efectivo, conIngreso: false);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);

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

        Assert.Equal((EstadoVenta.Confirmada, 0, 0, 0m), Completo(await EstadoAsync(id)));
        Assert.Empty(await IngresosDeLaVentaAsync(id));
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);
    }

    // FACT-REF-10
    [Theory]
    [InlineData(TipoPago.Efectivo, ConceptoMovimientoCaja.VentaEfectivo)]
    [InlineData(TipoPago.Tarjeta, ConceptoMovimientoCaja.VentaTarjeta)]
    [InlineData(TipoPago.TarjetaDebito, ConceptoMovimientoCaja.VentaTarjeta)]
    [InlineData(TipoPago.TarjetaCredito, ConceptoMovimientoCaja.VentaTarjeta)]
    [InlineData(TipoPago.Transferencia, ConceptoMovimientoCaja.VentaTransferencia)]
    [InlineData(TipoPago.Cheque, ConceptoMovimientoCaja.VentaCheque)]
    [InlineData(TipoPago.MercadoPago, ConceptoMovimientoCaja.VentaMercadoPago)]
    public async Task FactRef10_TodosLosMediosDePago_ConColision_RegistranSuIngreso_YConHistoricoNoDuplican(
        TipoPago tipoPago, ConceptoMovimientoCaja concepto)
    {
        // a) colisión con un cobro de cuota: el ingreso faltante se registra con el concepto del medio.
        var id = await SeedVentaAsync(tipoPago, conIngreso: false);
        var (cuotaId, creditoId, cobroId) = await SembrarCobroDeCuotaConElMismoIdAsync(id);
        Assert.True(await FacturarAsync(id));
        var propios = await IngresosDeLaVentaAsync(id);
        Assert.Single(propios);
        Assert.Equal(concepto, propios[0].Concepto);
        Assert.Equal(id, propios[0].VentaId);
        await AssertCobroDeCuotaIntactoAsync(cobroId, cuotaId, creditoId);

        // b) ingreso histórico legítimo del mismo medio (sin VentaId): se reconoce y no se duplica.
        var id2 = await SeedVentaAsync(tipoPago, conIngreso: false);
        var numero2 = await NumeroDeVentaAsync(id2);
        await SembrarMovimientoAsync(m =>
        {
            m.Concepto = concepto; m.TipoPago = tipoPago; m.VentaId = null; m.ReferenciaId = id2; m.Referencia = numero2;
        });
        Assert.True(await FacturarAsync(id2));
        Assert.Single(await IngresosDeLaVentaAsync(id2));
        Assert.Equal(EstadoVenta.Facturada, (await EstadoAsync(id2)).estado);
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
