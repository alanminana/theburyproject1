using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML2 — Backfill conservador de PagoCuota desde MovimientosCaja históricos.
// Criterio rector (explícito del pedido): ante datos ambiguos, marcar incompleto; nunca
// reconstruir importes o fechas por suposición.
// ---------------------------------------------------------------------------

public class PagoCuotaBackfillServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PagoCuotaBackfillService _service;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj;
    private int _nextId = 1;

    public PagoCuotaBackfillServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _reloj = new TheBuryProject.Tests.Helpers.RelojComercialFake(new DateOnly(2026, 1, 1))
        {
            ZonaComercial = TimeZoneInfo.Utc
        };

        _service = new PagoCuotaBackfillService(_context, _reloj);

        _context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja 1", IsDeleted = false });
        _context.AperturasCaja.Add(new AperturaCaja
        {
            Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "TestUser", Cerrada = false, IsDeleted = false
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Cuota> SeedCuota(decimal montoPunitorio = 0m)
    {
        var id = _nextId++;
        _context.Clientes.Add(new Cliente { Id = id, Nombre = "Cliente", Apellido = $"T{id}", NumeroDocumento = $"9300{id:D5}", IsDeleted = false });
        _context.Creditos.Add(new Credito
        {
            Id = id, ClienteId = id, IsDeleted = false, Numero = $"CRED{id:D4}", Estado = EstadoCredito.Activo,
            TasaInteres = 24m, MontoSolicitado = 1000m, MontoAprobado = 1000m, SaldoPendiente = 800m,
            CantidadCuotas = 1, MontoCuota = 1000m, TotalAPagar = 1000m
        });
        var cuota = new Cuota
        {
            Id = id, CreditoId = id, NumeroCuota = 1, FechaVencimiento = DateTime.UtcNow.AddDays(-30),
            MontoTotal = 1000m, MontoCapital = 800m, MontoInteres = 200m,
            MontoPagado = montoPunitorio > 0 ? 100m : 400m, // valor cualquiera, el backfill no lo toca
            MontoPunitorio = montoPunitorio, Estado = EstadoCuota.Parcial, IsDeleted = false
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    private async Task<MovimientoCaja> SeedMovimientoCobroCuota(
        int? cuotaId, decimal? importeBase, decimal monto, DateTime? fecha = null, string usuario = "operador1")
    {
        var movimiento = new MovimientoCaja
        {
            AperturaCajaId = 1,
            FechaMovimiento = fecha ?? DateTime.UtcNow.AddDays(-30),
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota,
            Monto = monto,
            ImporteBase = importeBase,
            Descripcion = "Cobro cuota",
            ReferenciaId = cuotaId,
            MedioPagoDetalle = "Efectivo",
            Usuario = usuario
        };
        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
        return movimiento;
    }

    [Fact]
    public async Task MovimientoInequivoco_SinPunitorioVigente_GeneraFilaCompleta()
    {
        var cuota = await SeedCuota(montoPunitorio: 0m);
        var movimiento = await SeedMovimientoCobroCuota(cuota.Id, 400m, 400m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.ReconstruidosCompletos);
        Assert.Equal(0, resultado.ReconstruidosParcialmente);
        Assert.Equal(0, resultado.Omitidos);
        Assert.Equal(0, resultado.Ambiguos);

        var fila = await _context.PagosCuota.SingleAsync(p => p.MovimientoCajaId == movimiento.Id);
        Assert.True(fila.HistorialCompleto);
        Assert.Equal(400m, fila.ImporteTotal);
        Assert.Equal(400m, fila.ImporteAplicadoCuota);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
        Assert.Equal(OrigenPagoCuota.BackfillMovimientoCaja, fila.Origen);
        Assert.Equal("operador1", fila.CreatedBy); // preserva el usuario histórico, no "System"
    }

    [Fact]
    public async Task MovimientoInequivoco_ConPunitorioVigente_GeneraFilaParcial_NoInventaComposicion()
    {
        var cuota = await SeedCuota(montoPunitorio: 55.50m);
        var movimiento = await SeedMovimientoCobroCuota(cuota.Id, 100m, 100m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(0, resultado.ReconstruidosCompletos);
        Assert.Equal(1, resultado.ReconstruidosParcialmente);

        var fila = await _context.PagosCuota.SingleAsync(p => p.MovimientoCajaId == movimiento.Id);
        Assert.False(fila.HistorialCompleto);
        Assert.Equal(100m, fila.ImporteTotal); // el total siempre se conserva
        Assert.Null(fila.ImporteAplicadoCuota);
        Assert.Null(fila.ImporteAplicadoPunitorio);
        Assert.Equal(OrigenPagoCuota.BackfillIncompleto, fila.Origen);
        Assert.False(string.IsNullOrWhiteSpace(fila.MotivoIncompleto));
    }

    [Fact]
    public async Task MovimientoSinReferenciaId_SeOmite()
    {
        await SeedMovimientoCobroCuota(cuotaId: null, importeBase: 100m, monto: 100m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.Omitidos);
        Assert.Equal(0, resultado.ReconstruidosCompletos + resultado.ReconstruidosParcialmente + resultado.Ambiguos);
        Assert.Empty(await _context.PagosCuota.ToListAsync());
    }

    [Fact]
    public async Task MovimientoConReferenciaIdHuerfano_SeOmite()
    {
        await SeedMovimientoCobroCuota(cuotaId: 999999, importeBase: 100m, monto: 100m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.Omitidos);
        Assert.Empty(await _context.PagosCuota.ToListAsync());
    }

    [Fact]
    public async Task MovimientoSinImporteBase_SeOmite_NoUsaMontoComoSustituto()
    {
        var cuota = await SeedCuota();
        // Monto incluye recargo (110), ImporteBase ausente: usar Monto contaminaría el ledger.
        await SeedMovimientoCobroCuota(cuota.Id, importeBase: null, monto: 110m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.Omitidos);
        Assert.Empty(await _context.PagosCuota.ToListAsync());
    }

    [Fact]
    public async Task MovimientoConImporteBaseInvalido_SeMarcaAmbiguo()
    {
        var cuota = await SeedCuota();
        await SeedMovimientoCobroCuota(cuota.Id, importeBase: -50m, monto: -50m);

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.Ambiguos);
        Assert.Empty(await _context.PagosCuota.ToListAsync());
    }

    [Fact]
    public async Task EjecutarDosVeces_NoDuplica()
    {
        var cuota = await SeedCuota();
        await SeedMovimientoCobroCuota(cuota.Id, 400m, 400m);

        var primeraCorrida = await _service.EjecutarAsync();
        Assert.Equal(1, primeraCorrida.ReconstruidosCompletos);

        var segundaCorrida = await _service.EjecutarAsync();
        Assert.Equal(0, segundaCorrida.ReconstruidosCompletos);
        Assert.Equal(1, segundaCorrida.Omitidos); // "ya registrado"

        Assert.Single(await _context.PagosCuota.ToListAsync());
    }

    [Fact]
    public async Task PagoYaRegistradoPorElSistema_NoSeVuelveAImportar()
    {
        // Simula un movimiento que YA tiene su fila de ledger (como si viniera de un pago nuevo
        // post-ML2), mezclado con movimientos históricos sin fila todavía.
        var cuotaNueva = await SeedCuota();
        var movimientoNuevo = await SeedMovimientoCobroCuota(cuotaNueva.Id, 400m, 400m);
        _context.PagosCuota.Add(new PagoCuota
        {
            CuotaId = cuotaNueva.Id,
            FechaPagoComercial = _reloj.HoyComercial,
            ImporteTotal = 400m,
            ImporteAplicadoCuota = 400m,
            ImporteAplicadoPunitorio = 0m,
            MovimientoCajaId = movimientoNuevo.Id,
            Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Aplicado,
            HistorialCompleto = true
        });

        var cuotaHistorica = await SeedCuota();
        await SeedMovimientoCobroCuota(cuotaHistorica.Id, 400m, 400m);

        await _context.SaveChangesAsync();

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.ReconstruidosCompletos); // solo la histórica
        Assert.Equal(1, resultado.Omitidos); // la ya registrada
        Assert.Equal(2, await _context.PagosCuota.CountAsync()); // no se duplicó la primera
    }

    [Fact]
    public async Task LaSumaReconstruida_NuncaModificaMontoPagadoDeLaCuota()
    {
        var cuota = await SeedCuota();
        var montoPagadoAntes = cuota.MontoPagado;
        await SeedMovimientoCobroCuota(cuota.Id, 400m, 400m);

        await _service.EjecutarAsync();

        var cuotaTrasBackfill = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(montoPagadoAntes, cuotaTrasBackfill.MontoPagado);
        Assert.Equal(EstadoCuota.Parcial, cuotaTrasBackfill.Estado);
    }

    [Fact]
    public async Task ReporteFinal_DistingueLasCuatroCategoriasYSuman()
    {
        var cuotaCompleta = await SeedCuota(montoPunitorio: 0m);
        await SeedMovimientoCobroCuota(cuotaCompleta.Id, 400m, 400m);

        var cuotaParcial = await SeedCuota(montoPunitorio: 20m);
        await SeedMovimientoCobroCuota(cuotaParcial.Id, 100m, 100m);

        await SeedMovimientoCobroCuota(cuotaId: null, importeBase: 100m, monto: 100m); // omitido
        var cuotaAmbigua = await SeedCuota();
        await SeedMovimientoCobroCuota(cuotaAmbigua.Id, importeBase: 0m, monto: 0m); // ambiguo

        var resultado = await _service.EjecutarAsync();

        Assert.Equal(1, resultado.ReconstruidosCompletos);
        Assert.Equal(1, resultado.ReconstruidosParcialmente);
        Assert.Equal(1, resultado.Omitidos);
        Assert.Equal(1, resultado.Ambiguos);
        Assert.Equal(4, resultado.TotalProcesados);
        Assert.Equal(4, resultado.Detalles.Count);
    }
}
