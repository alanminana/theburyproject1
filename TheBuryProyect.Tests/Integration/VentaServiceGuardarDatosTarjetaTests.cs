using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración directos para VentaService.GuardarDatosTarjetaAsync.
///
/// Antes de esta fase el método no tenía cobertura directa.
/// Estos tests caracterizan el contrato real contra SQLite in-memory.
///
/// Cubre:
/// - TarjetaCredito SinInteres: persiste snapshot con TasaInteres=0, MontoCuota=total/cuotas, MontoTotal=total
/// - TarjetaCredito ConInteres: recalcula vía sistema francés y persiste montos
/// - TarjetaDebito con recargo: suma RecargoAplicado a Venta.Total y persiste snapshot
/// - VentaId inexistente: retorna false sin tocar DB
/// - Segunda llamada con débito+recargo: documenta que el recargo se duplica (bug — Fase 6.3 agrega guard)
/// </summary>
public class VentaServiceGuardarDatosTarjetaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;

    private static int _counter = 900;

    public VentaServiceGuardarDatosTarjetaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = BuildService(_context, mapper);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── Infraestructura ──────────────────────────────────────────────

    private static VentaService BuildService(AppDbContext ctx, IMapper mapper) =>
        new VentaService(
            ctx,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService — no usado en GuardarDatosTarjetaAsync
            null!,   // IMovimientoStockService — no usado
            new FinancialCalculationService(),
            null!,   // IVentaValidator — no usado
            null!,   // VentaNumberGenerator — no usado
            null!,   // IPrecioVigenteResolver — no usado
            null!,   // ICurrentUserService — no usado
            null!,   // IValidacionVentaService — no usado
            null!,   // ICajaService — no usado
            null!,   // ICreditoDisponibleService — no usado
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

    private async Task<Venta> SeedVenta(decimal total = 6_000m, TipoPago tipoPago = TipoPago.TarjetaCredito)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "T",
            NumeroDocumento = $"T{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VT{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = tipoPago,
            Total = total,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<ConfiguracionTarjeta> SeedConfiguracionTarjeta(
        TipoTarjeta tipo,
        TipoCuotaTarjeta? tipoCuota = null,
        decimal? tasa = null,
        string nombre = "Visa Test",
        bool activa = true,
        bool tieneRecargoDebito = false,
        decimal? porcentajeRecargoDebito = null)
    {
        var configPago = new ConfiguracionPago
        {
            TipoPago = tipo == TipoTarjeta.Credito ? TipoPago.TarjetaCredito : TipoPago.TarjetaDebito,
            Nombre = $"Config {nombre}"
        };
        _context.ConfiguracionesPago.Add(configPago);
        await _context.SaveChangesAsync();

        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPago.Id,
            NombreTarjeta = nombre,
            TipoTarjeta = tipo,
            Activa = activa,
            PermiteCuotas = tipo == TipoTarjeta.Credito,
            TipoCuota = tipoCuota,
            TasaInteresesMensual = tasa,
            TieneRecargoDebito = tipo == TipoTarjeta.Debito && tieneRecargoDebito,
            PorcentajeRecargoDebito = tipo == TipoTarjeta.Debito && tieneRecargoDebito
                ? porcentajeRecargoDebito
                : null
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    // ── Tests ────────────────────────────────────────────────────────

    // -------------------------------------------------------------------------
    // 1. Crédito SinInteres: snapshot persiste con TasaInteres=0, MontoCuota=total/cuotas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_TarjetaCreditoSinInteres_PersisteSnapshot()
    {
        var venta = await SeedVenta(total: 6_000m);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Credito, TipoCuotaTarjeta.SinInteres, nombre: "Visa SinInteres");

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 6,
            TipoCuota = TipoCuotaTarjeta.SinInteres
        };

        var result = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        Assert.True(result);
        var datos = await _context.DatosTarjeta.SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(tarjeta.Id, datos.ConfiguracionTarjetaId);
        Assert.Equal(tarjeta.NombreTarjeta, datos.NombreTarjeta);
        Assert.Equal(TipoTarjeta.Credito, datos.TipoTarjeta);
        Assert.Equal(6, datos.CantidadCuotas);
        Assert.Equal(TipoCuotaTarjeta.SinInteres, datos.TipoCuota);
        Assert.Equal(0m, datos.TasaInteres);
        Assert.Equal(1_000m, datos.MontoCuota);       // 6000 / 6
        Assert.Equal(6_000m, datos.MontoTotalConInteres);
    }

    // -------------------------------------------------------------------------
    // 2. Crédito ConInteres: recalcula con sistema francés y persiste montos
    //    PMT(12.000, 10% mensual, 6 cuotas) ≈ 2.755,29
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_TarjetaCreditoConInteres_PersisteSistemaFrances()
    {
        var venta = await SeedVenta(total: 12_000m);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Credito, TipoCuotaTarjeta.ConInteres, tasa: 10m, nombre: "Visa ConInteres");

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 6,
            TipoCuota = TipoCuotaTarjeta.ConInteres
        };

        var result = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        Assert.True(result);
        var datos = await _context.DatosTarjeta.SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(10m, datos.TasaInteres);
        Assert.NotNull(datos.MontoCuota);
        Assert.NotNull(datos.MontoTotalConInteres);
        // Tolerancia ±1 por precisión double→decimal en Math.Pow (mismo criterio que CalcularCuotasTarjetaTests)
        Assert.InRange(datos.MontoCuota!.Value, 2_754m, 2_757m);
        // MontoTotal es exactamente MontoCuota * cuotas
        Assert.Equal(datos.MontoCuota.Value * 6, datos.MontoTotalConInteres!.Value);
        Assert.True(datos.MontoTotalConInteres.Value > 12_000m);
    }

    // -------------------------------------------------------------------------
    // 3. Débito con recargo: Venta.Total se incrementa y RecargoAplicado queda en snapshot
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_TarjetaDebitoConRecargo_SumaRecargoAVentaTotal()
    {
        var venta = await SeedVenta(total: 10_000m, tipoPago: TipoPago.TarjetaDebito);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Debito,
            nombre: "Maestro Debito",
            tieneRecargoDebito: true,
            porcentajeRecargoDebito: 5m);

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = "Maestro Débito",
            TipoTarjeta = TipoTarjeta.Debito,
            RecargoAplicado = 999m
        };

        var result = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        Assert.True(result);

        var ventaActualizada = await _context.Ventas
            .AsNoTracking()
            .SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(10_500m, ventaActualizada.Total);

        var datos = await _context.DatosTarjeta.SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(tarjeta.Id, datos.ConfiguracionTarjetaId);
        Assert.Equal(tarjeta.NombreTarjeta, datos.NombreTarjeta);
        Assert.Equal(500m, datos.RecargoAplicado);
        Assert.Equal(TipoTarjeta.Debito, datos.TipoTarjeta);
    }

    // -------------------------------------------------------------------------
    // 4. VentaId inexistente → retorna false, sin registros en DatosTarjeta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_VentaInexistente_RetornaFalse()
    {
        var vm = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Credito
        };

        var result = await _service.GuardarDatosTarjetaAsync(int.MaxValue, vm);

        Assert.False(result);
        var count = await _context.DatosTarjeta.CountAsync();
        Assert.Equal(0, count);
    }

    // -------------------------------------------------------------------------
    // 5. Segunda llamada con débito+recargo: el guard retorna false y no duplica Total.
    //    Fase 6.3 corrigió el bug confirmado en Fase 6.2.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_SegundaLlamada_NoDuplicaRecargoYRetornaFalse()
    {
        var venta = await SeedVenta(total: 10_000m, tipoPago: TipoPago.TarjetaDebito);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Debito,
            nombre: "Maestro Debito",
            tieneRecargoDebito: true,
            porcentajeRecargoDebito: 5m);

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = "Maestro Débito",
            TipoTarjeta = TipoTarjeta.Debito,
            RecargoAplicado = 999m
        };

        var primera = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);
        var segunda = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        Assert.True(primera);
        Assert.False(segunda);

        var ventaActualizada = await _context.Ventas
            .AsNoTracking()
            .SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(10_500m, ventaActualizada.Total);

        var countDatos = await _context.DatosTarjeta.CountAsync(d => d.VentaId == venta.Id);
        Assert.Equal(1, countDatos);
    }

    // -------------------------------------------------------------------------
    // 6. Segunda llamada con crédito: el guard también bloquea el reemplazo de snapshot.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosTarjeta_SegundaLlamadaCredito_NoReemplazaSnapshotYRetornaFalse()
    {
        var venta = await SeedVenta(total: 6_000m);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Credito, TipoCuotaTarjeta.SinInteres, nombre: "Visa Guard");

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 6,
            TipoCuota = TipoCuotaTarjeta.SinInteres
        };

        var primera = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        // Segunda llamada con cantidad de cuotas distinta — no debe reemplazar el snapshot.
        var vmSegunda = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 3,
            TipoCuota = TipoCuotaTarjeta.SinInteres
        };
        var segunda = await _service.GuardarDatosTarjetaAsync(venta.Id, vmSegunda);

        Assert.True(primera);
        Assert.False(segunda);

        // El snapshot original (6 cuotas) se conserva.
        var datos = await _context.DatosTarjeta.SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(6, datos.CantidadCuotas);
        Assert.Equal(1_000m, datos.MontoCuota);  // 6000 / 6
    }

    [Fact]
    public async Task GuardarDatosTarjeta_TarjetaInactiva_NoGuardaDatos()
    {
        var venta = await SeedVenta(total: 6_000m);
        var tarjeta = await SeedConfiguracionTarjeta(
            TipoTarjeta.Credito,
            TipoCuotaTarjeta.SinInteres,
            nombre: "Visa Inactiva",
            activa: false);

        var vm = new DatosTarjetaViewModel
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 6,
            TipoCuota = TipoCuotaTarjeta.SinInteres
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GuardarDatosTarjetaAsync(venta.Id, vm));

        Assert.Contains("no esta disponible", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _context.DatosTarjeta.CountAsync(d => d.VentaId == venta.Id));
    }
}
