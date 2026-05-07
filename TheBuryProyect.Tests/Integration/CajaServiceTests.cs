using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub de INotificacionService — sin dependencia de Moq
// ---------------------------------------------------------------------------
file sealed class StubNotificacionService : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model)
        => Task.FromResult(new Notificacion());

    public Task CrearNotificacionParaUsuarioAsync(string usuario, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;

    public Task CrearNotificacionParaRolAsync(string rol, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;

    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string usuario, bool soloNoLeidas = false, int limite = 50)
        => Task.FromResult(new List<NotificacionViewModel>());

    public Task<int> ObtenerCantidadNoLeidasAsync(string usuario)
        => Task.FromResult(0);

    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id)
        => Task.FromResult<Notificacion?>(null);

    public Task MarcarComoLeidaAsync(int notificacionId, string usuario, byte[]? rowVersion = null)
        => Task.CompletedTask;

    public Task MarcarTodasComoLeidasAsync(string usuario)
        => Task.CompletedTask;

    public Task EliminarNotificacionAsync(int id, string usuario, byte[]? rowVersion = null)
        => Task.CompletedTask;

    public Task LimpiarNotificacionesAntiguasAsync(int diasAntiguedad = 30)
        => Task.CompletedTask;

    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string usuario)
        => Task.FromResult(new ListaNotificacionesViewModel());
}

/// <summary>
/// Tests de integración para CajaService.
/// Cubren AbrirCajaAsync, RegistrarMovimientoAsync, CalcularSaldoActualAsync
/// y CerrarCajaAsync: reglas de negocio, validaciones, cálculo de arqueo,
/// tolerancia de diferencia y requisito de justificación.
/// </summary>
public class CajaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;

    public CajaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
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

        _service = new CajaService(
            _context,
            mapper,
            NullLogger<CajaService>.Instance,
            new StubNotificacionService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Caja> SeedCajaAsync(bool activa = true, EstadoCaja estado = EstadoCaja.Cerrada)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var caja = new Caja
        {
            Codigo = codigo,
            Nombre = "Caja-" + codigo,
            Activa = activa,
            Estado = estado
        };
        _context.Set<Caja>().Add(caja);
        await _context.SaveChangesAsync();
        return caja;
    }

    private async Task<AperturaCaja> AbrirCajaAsync(Caja caja, decimal montoInicial = 1000m)
    {
        return await _service.AbrirCajaAsync(
            new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = montoInicial },
            "testuser");
    }

    private async Task<Venta> SeedVentaAsync(AperturaCaja apertura, TipoPago tipoPago, decimal total = 1_000m, decimal? recargoDebito = null)
    {
        var cliente = new Cliente
        {
            Nombre = "Cliente",
            Apellido = "Caja",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8]
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            AperturaCajaId = apertura.Id,
            Numero = $"VTA-{Guid.NewGuid():N}"[..12],
            Estado = EstadoVenta.Confirmada,
            TipoPago = tipoPago,
            Total = total
        };

        if (recargoDebito.HasValue)
        {
            venta.DatosTarjeta = new DatosTarjeta
            {
                NombreTarjeta = "Debito Test",
                TipoTarjeta = TipoTarjeta.Debito,
                RecargoAplicado = recargoDebito
            };
        }

        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private MovimientoCajaViewModel BuildMovimiento(int aperturaId, TipoMovimientoCaja tipo, decimal monto) =>
        new()
        {
            AperturaCajaId = aperturaId,
            Tipo = tipo,
            Concepto = ConceptoMovimientoCaja.AjusteCaja,
            Descripcion = "Test",
            Monto = monto
        };

    private Task<CierreCaja> CerrarCajaExactaAsync(AperturaCaja apertura, decimal efectivo = 0m) =>
        _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = efectivo
            },
            "admin");

    // -------------------------------------------------------------------------
    // AbrirCajaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AbrirCaja_CajaActivaCerrada_CreaAperturaYCambiaEstado()
    {
        var caja = await SeedCajaAsync();

        var apertura = await _service.AbrirCajaAsync(
            new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 500m },
            "usuario1");

        Assert.Equal(caja.Id, apertura.CajaId);
        Assert.Equal(500m, apertura.MontoInicial);
        Assert.Equal("usuario1", apertura.UsuarioApertura);
        Assert.False(apertura.Cerrada);

        var cajaBd = await _context.Set<Caja>().FirstAsync(c => c.Id == caja.Id);
        Assert.Equal(EstadoCaja.Abierta, cajaBd.Estado);
    }

    [Fact]
    public async Task AbrirCaja_CajaNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AbrirCajaAsync(
                new AbrirCajaViewModel { CajaId = 99999, MontoInicial = 100m },
                "usuario1"));
    }

    [Fact]
    public async Task AbrirCaja_CajaInactiva_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync(activa: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AbrirCajaAsync(
                new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 100m },
                "usuario1"));
    }

    [Fact]
    public async Task AbrirCaja_CajaYaAbierta_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja); // primera apertura

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AbrirCajaAsync(caja)); // segunda apertura — debe fallar
    }

    // -------------------------------------------------------------------------
    // TieneCajaAbiertaAsync / ExisteAlgunaCajaAbiertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TieneCajaAbierta_SinApertura_RetornaFalse()
    {
        var caja = await SeedCajaAsync();
        var result = await _service.TieneCajaAbiertaAsync(caja.Id);
        Assert.False(result);
    }

    [Fact]
    public async Task TieneCajaAbierta_ConAperturaActiva_RetornaTrue()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var result = await _service.TieneCajaAbiertaAsync(caja.Id);
        Assert.True(result);
    }

    [Fact]
    public async Task ExisteAlgunaCajaAbierta_SinAperturas_RetornaFalse()
    {
        var result = await _service.ExisteAlgunaCajaAbiertaAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task ExisteAlgunaCajaAbierta_ConUnaAbierta_RetornaTrue()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var result = await _service.ExisteAlgunaCajaAbiertaAsync();
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarMovimiento_AperturaActiva_GuardaMovimiento()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        var mov = await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 200m),
            "usuario1");

        Assert.Equal(apertura.Id, mov.AperturaCajaId);
        Assert.Equal(TipoMovimientoCaja.Ingreso, mov.Tipo);
        Assert.Equal(200m, mov.Monto);
        Assert.Equal("usuario1", mov.Usuario);
    }

    [Fact]
    public async Task RegistrarMovimiento_AperturaNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoAsync(
                BuildMovimiento(99999, TipoMovimientoCaja.Ingreso, 100m),
                "usuario1"));
    }

    [Fact]
    public async Task RegistrarMovimiento_CajaCerrada_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);

        // Cerrar la caja manualmente (sin usar el service para aislar este test)
        var aperturaBd = await _context.Set<AperturaCaja>().FirstAsync(a => a.Id == apertura.Id);
        aperturaBd.Cerrada = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoAsync(
                BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 50m),
                "usuario1"));
    }

    // -------------------------------------------------------------------------
    // CalcularSaldoActualAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularSaldo_SinMovimientos_EsMontoInicial()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        var saldo = await _service.CalcularSaldoActualAsync(apertura.Id);

        Assert.Equal(1000m, saldo);
    }

    [Fact]
    public async Task CalcularSaldo_ConIngresosYEgresos_CalculaCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 500m), "u");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 200m), "u");

        // 1000 + 500 - 200 = 1300
        var saldo = await _service.CalcularSaldoActualAsync(apertura.Id);
        Assert.Equal(1300m, saldo);
    }

    [Fact]
    public async Task CalcularSaldo_AperturaNoExiste_RetornaCero()
    {
        var saldo = await _service.CalcularSaldoActualAsync(99999);
        Assert.Equal(0m, saldo);
    }

    // -------------------------------------------------------------------------
    // CerrarCajaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CerrarCaja_SinDiferencia_CierraCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        // Monto esperado = 1000 (sin movimientos), contamos exactamente 1000
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 1000m,
                ChequesContados = 0m,
                ValesContados = 0m
            },
            "usuario1");

        Assert.Equal(1000m, cierre.MontoEsperadoSistema);
        Assert.Equal(1000m, cierre.MontoTotalReal);
        Assert.Equal(0m, cierre.Diferencia);
        Assert.False(cierre.TieneDiferencia);
        Assert.Equal("usuario1", cierre.UsuarioCierre);

        // La apertura debe quedar cerrada
        var aperturaBd = await _context.Set<AperturaCaja>().FirstAsync(a => a.Id == apertura.Id);
        Assert.True(aperturaBd.Cerrada);

        // El estado de la caja debe ser Cerrada
        var cajaBd = await _context.Set<Caja>().FirstAsync(c => c.Id == caja.Id);
        Assert.Equal(EstadoCaja.Cerrada, cajaBd.Estado);
    }

    [Fact]
    public async Task CerrarCaja_DiferenciaConJustificacion_CierraCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        // Contamos 50 pesos menos — hay diferencia real (> 0.01 tolerancia)
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 950m,
                ChequesContados = 0m,
                ValesContados = 0m,
                JustificacionDiferencia = "Faltante de caja"
            },
            "usuario1");

        Assert.True(cierre.TieneDiferencia);
        Assert.Equal(-50m, cierre.Diferencia);
        Assert.Equal("Faltante de caja", cierre.JustificacionDiferencia);
    }

    [Fact]
    public async Task CerrarCaja_DiferenciaSinJustificacion_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel
                {
                    AperturaCajaId = apertura.Id,
                    EfectivoContado = 900m,  // diferencia de 100 sin justificación
                    ChequesContados = 0m,
                    ValesContados = 0m,
                    JustificacionDiferencia = null
                },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_YaCerrada_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);

        // Primer cierre sin diferencia
        await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 500m
            },
            "usuario1");

        // Segundo cierre sobre la misma apertura
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel
                {
                    AperturaCajaId = apertura.Id,
                    EfectivoContado = 500m
                },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_NoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = 99999, EfectivoContado = 0m },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_ConMovimientos_MontoEsperadoIncluye()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 300m), "u");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 100m), "u");

        // Esperado = 1000 + 300 - 100 = 1200
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 1200m
            },
            "usuario1");

        Assert.Equal(300m, cierre.TotalIngresosSistema);
        Assert.Equal(100m, cierre.TotalEgresosSistema);
        Assert.Equal(1200m, cierre.MontoEsperadoSistema);
        Assert.Equal(0m, cierre.Diferencia);
    }

    // -------------------------------------------------------------------------
    // ObtenerAperturaActivaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerAperturaActiva_SinApertura_RetornaNull()
    {
        var caja = await SeedCajaAsync();
        var result = await _service.ObtenerAperturaActivaAsync(caja.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task ObtenerAperturaActiva_ConAperturaActiva_RetornaApertura()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var result = await _service.ObtenerAperturaActivaAsync(caja.Id);

        Assert.NotNull(result);
        Assert.Equal(apertura.Id, result!.Id);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoCuotaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarMovimientoCuota_SinCajaAbierta_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 1, creditoNumero: "CRED-001", numeroCuota: 1,
            monto: 500m, medioPago: "Efectivo", usuario: "testuser");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarMovimientoCuota_ConCajaAbierta_PersistMovimiento()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var resultado = await _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 42, creditoNumero: "CRED-002", numeroCuota: 3,
            monto: 1_000m, medioPago: "Transferencia", usuario: "cajero1");

        Assert.NotNull(resultado);
        Assert.Equal(apertura.Id, resultado!.AperturaCajaId);
        Assert.Equal(1_000m, resultado.Monto);
        Assert.Equal(42, resultado.ReferenciaId);
        Assert.Contains("CRED-002", resultado.Descripcion);
        Assert.Contains("#3", resultado.Descripcion);
    }

    [Fact]
    public async Task RegistrarMovimientoCuota_ConCajaAbierta_TipoEsIngreso()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var resultado = await _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 1, creditoNumero: "CRED-003", numeroCuota: 1,
            monto: 500m, medioPago: "Efectivo", usuario: "cajero1");

        Assert.NotNull(resultado);
        Assert.Equal(TipoMovimientoCaja.Ingreso, resultado!.Tipo);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoVentaAsync — crédito personal y cuenta corriente → null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarMovimientoVenta_CreditoPersonal_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoVentaAsync(
            ventaId: 0, ventaNumero: "VTA-001", monto: 10_000m,
            tipoPago: TipoPago.CreditoPersonal, usuario: "cajero1");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarMovimientoVenta_CuentaCorriente_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoVentaAsync(
            ventaId: 0, ventaNumero: "VTA-002", monto: 5_000m,
            tipoPago: TipoPago.CuentaCorriente, usuario: "cajero1");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarMovimientoVentaAsync_Transferencia_PersisteTipoPagoYNoCuentaComoEfectivo()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);
        var venta = await SeedVentaAsync(apertura, TipoPago.Transferencia, total: 1_500m);

        var movimiento = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero1");

        Assert.NotNull(movimiento);
        Assert.Equal(TipoPago.Transferencia, movimiento!.TipoPago);
        Assert.Equal(venta.Id, movimiento.VentaId);
        Assert.Equal(venta.Numero, movimiento.Referencia);
        Assert.Equal(venta.Id, movimiento.ReferenciaId);
        Assert.Equal("Transferencia", movimiento.MedioPagoDetalle);

        var resumen = await _service.ObtenerDetallesAperturaAsync(apertura.Id);
        Assert.DoesNotContain(resumen.ResumenRealPorMedioPago, r => r.MedioPago == "Efectivo" && r.TotalIngresos == venta.Total);
        var transferencia = Assert.Single(resumen.ResumenRealPorMedioPago, r => r.MedioPago == "Transferencia");
        Assert.Equal(venta.Total, transferencia.TotalIngresos);
    }

    [Fact]
    public async Task RegistrarMovimientoVentaAsync_MercadoPago_PersisteTipoPagoYNoCuentaComoEfectivo()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);
        var venta = await SeedVentaAsync(apertura, TipoPago.MercadoPago, total: 2_500m);

        var movimiento = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero1");

        Assert.NotNull(movimiento);
        Assert.Equal(TipoPago.MercadoPago, movimiento!.TipoPago);
        Assert.Equal(venta.Id, movimiento.VentaId);
        Assert.Equal("MercadoPago", movimiento.MedioPagoDetalle);

        var resumen = await _service.ObtenerDetallesAperturaAsync(apertura.Id);
        Assert.DoesNotContain(resumen.ResumenRealPorMedioPago, r => r.MedioPago == "Efectivo" && r.TotalIngresos == venta.Total);
        var mercadoPago = Assert.Single(resumen.ResumenRealPorMedioPago, r => r.MedioPago == "Mercado Pago");
        Assert.Equal(venta.Total, mercadoPago.TotalIngresos);
    }

    [Fact]
    public async Task RegistrarMovimientoVentaAsync_TarjetaDebito_PersisteRecargoDebito()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);
        var venta = await SeedVentaAsync(apertura, TipoPago.TarjetaDebito, total: 1_100m, recargoDebito: 100m);

        var movimiento = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero1");

        Assert.NotNull(movimiento);
        Assert.Equal(TipoPago.TarjetaDebito, movimiento!.TipoPago);
        Assert.Equal(venta.Total, movimiento.Monto);
        Assert.Equal(venta.Id, movimiento.VentaId);
        Assert.Equal(100m, movimiento.RecargoDebitoAplicado);
        Assert.Equal("Tarjeta débito", movimiento.MedioPagoDetalle);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoAnticipoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAnticipo_MontoCero_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoAnticipoAsync(
            creditoId: 1, creditoNumero: "CRED-ANT-001",
            montoAnticipo: 0m, usuario: "cajero1");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarAnticipo_MontoNegativo_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoAnticipoAsync(
            creditoId: 1, creditoNumero: "CRED-ANT-002",
            montoAnticipo: -100m, usuario: "cajero1");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarAnticipo_SinCajaAbierta_RetornaNull()
    {
        var resultado = await _service.RegistrarMovimientoAnticipoAsync(
            creditoId: 1, creditoNumero: "CRED-ANT-003",
            montoAnticipo: 500m, usuario: "cajero1");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RegistrarAnticipo_ConCajaAbierta_PersistMovimiento()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var resultado = await _service.RegistrarMovimientoAnticipoAsync(
            creditoId: 7, creditoNumero: "CRED-ANT-004",
            montoAnticipo: 2_000m, usuario: "cajero1");

        Assert.NotNull(resultado);
        Assert.Equal(apertura.Id, resultado!.AperturaCajaId);
        Assert.Equal(2_000m, resultado.Monto);
        Assert.Equal(7, resultado.ReferenciaId);
        Assert.Equal(TipoMovimientoCaja.Ingreso, resultado.Tipo);
        Assert.Contains("CRED-ANT-004", resultado.Descripcion);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarDevolucion_MontoCero_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoDevolucionAsync(
                devolucionId: 1, ventaId: 1,
                ventaNumero: "VTA-001", devolucionNumero: "DEV-001",
                monto: 0m, usuario: "cajero1"));
    }

    [Fact]
    public async Task RegistrarDevolucion_SinCajaAbierta_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoDevolucionAsync(
                devolucionId: 1, ventaId: 1,
                ventaNumero: "VTA-001", devolucionNumero: "DEV-002",
                monto: 300m, usuario: "cajero1"));
    }

    [Fact]
    public async Task RegistrarDevolucion_ConCajaAbierta_TipoEsEgreso()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var resultado = await _service.RegistrarMovimientoDevolucionAsync(
            devolucionId: 5, ventaId: 10,
            ventaNumero: "VTA-003", devolucionNumero: "DEV-003",
            monto: 800m, usuario: "cajero1");

        Assert.NotNull(resultado);
        Assert.Equal(TipoMovimientoCaja.Egreso, resultado.Tipo);
        Assert.Equal(800m, resultado.Monto);
        Assert.Equal(5, resultado.ReferenciaId);
        Assert.Contains("DEV-003", resultado.Descripcion);
    }

    // =========================================================================
    // ObtenerTodasCajasAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerTodasCajas_SinCajas_RetornaVacio()
    {
        var resultado = await _service.ObtenerTodasCajasAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodasCajas_ConCajas_DevuelveTodas()
    {
        await SeedCajaAsync();
        await SeedCajaAsync();

        var resultado = await _service.ObtenerTodasCajasAsync();

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ObtenerTodasCajas_ExcluyeEliminadas()
    {
        var caja = await SeedCajaAsync();
        caja.IsDeleted = true;
        _context.Set<Caja>().Update(caja);
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerTodasCajasAsync();

        Assert.Empty(resultado);
    }

    // =========================================================================
    // ObtenerCajaPorIdAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerCajaPorId_Existente_RetornaCaja()
    {
        var caja = await SeedCajaAsync();

        var resultado = await _service.ObtenerCajaPorIdAsync(caja.Id);

        Assert.NotNull(resultado);
        Assert.Equal(caja.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerCajaPorId_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerCajaPorIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // CrearCajaAsync
    // =========================================================================

    [Fact]
    public async Task CrearCaja_CodigoNuevo_PersisteCaja()
    {
        var model = new CajaViewModel
        {
            Codigo = "CAJ-NEW",
            Nombre = "Caja Nueva",
            Activa = true
        };

        var resultado = await _service.CrearCajaAsync(model);

        Assert.True(resultado.Id > 0);
        Assert.Equal("CAJ-NEW", resultado.Codigo);
        Assert.Equal(EstadoCaja.Cerrada, resultado.Estado);
    }

    [Fact]
    public async Task CrearCaja_CodigoDuplicado_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var model = new CajaViewModel
        {
            Codigo = caja.Codigo,
            Nombre = "Duplicada",
            Activa = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearCajaAsync(model));
    }

    // =========================================================================
    // ActualizarCajaAsync
    // =========================================================================

    [Fact]
    public async Task ActualizarCaja_SinRowVersion_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var model = new CajaViewModel
        {
            Codigo = caja.Codigo,
            Nombre = "Actualizada",
            Activa = true,
            RowVersion = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ActualizarCajaAsync(caja.Id, model));
    }

    [Fact]
    public async Task ActualizarCaja_Inexistente_LanzaExcepcion()
    {
        var model = new CajaViewModel
        {
            Codigo = "COD-X",
            Nombre = "X",
            Activa = true,
            RowVersion = new byte[8]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ActualizarCajaAsync(99999, model));
    }

    // =========================================================================
    // EliminarCajaAsync
    // =========================================================================

    [Fact]
    public async Task EliminarCaja_SinRowVersion_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EliminarCajaAsync(caja.Id, null));
    }

    [Fact]
    public async Task EliminarCaja_CajaAbierta_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync(estado: EstadoCaja.Abierta);
        await _context.Entry(caja).ReloadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EliminarCajaAsync(caja.Id, caja.RowVersion));
    }

    [Fact]
    public async Task EliminarCaja_Inexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EliminarCajaAsync(99999, new byte[8]));
    }

    // =========================================================================
    // ExisteCodigoCajaAsync
    // =========================================================================

    [Fact]
    public async Task ExisteCodigoCaja_CodigoExistente_RetornaTrue()
    {
        var caja = await SeedCajaAsync();

        var resultado = await _service.ExisteCodigoCajaAsync(caja.Codigo);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ExisteCodigoCaja_CodigoInexistente_RetornaFalse()
    {
        var resultado = await _service.ExisteCodigoCajaAsync("CODIGO-INEXISTENTE");
        Assert.False(resultado);
    }

    [Fact]
    public async Task ExisteCodigoCaja_MismaCajaExcluida_RetornaFalse()
    {
        var caja = await SeedCajaAsync();

        var resultado = await _service.ExisteCodigoCajaAsync(caja.Codigo, caja.Id);

        Assert.False(resultado);
    }

    // =========================================================================
    // ObtenerAperturaPorIdAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerAperturaPorId_Existente_RetornaApertura()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var resultado = await _service.ObtenerAperturaPorIdAsync(apertura.Id);

        Assert.NotNull(resultado);
        Assert.Equal(apertura.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerAperturaPorId_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerAperturaPorIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // ObtenerAperturasAbiertasAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerAperturasAbiertas_SinAperturas_RetornaVacio()
    {
        var resultado = await _service.ObtenerAperturasAbiertasAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerAperturasAbiertas_ConAperturaAbierta_DevuelveApertura()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var resultado = await _service.ObtenerAperturasAbiertasAsync();

        Assert.Single(resultado);
    }

    // =========================================================================
    // ObtenerAperturaActivaParaUsuarioAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerAperturaActivaParaUsuario_UsuarioConApertura_RetornaApertura()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja); // abre con "testuser"

        var resultado = await _service.ObtenerAperturaActivaParaUsuarioAsync("testuser");

        Assert.NotNull(resultado);
    }

    [Fact]
    public async Task ObtenerAperturaActivaParaUsuario_UsuarioSinApertura_RetornaNull()
    {
        var resultado = await _service.ObtenerAperturaActivaParaUsuarioAsync("otro-user");
        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerAperturaActivaParaUsuario_UsuarioVacio_RetornaNull()
    {
        var resultado = await _service.ObtenerAperturaActivaParaUsuarioAsync("");
        Assert.Null(resultado);
    }

    // =========================================================================
    // ObtenerAperturaActivaParaVentaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerAperturaActivaParaVenta_SinAperturas_RetornaNull()
    {
        var resultado = await _service.ObtenerAperturaActivaParaVentaAsync();
        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerAperturaActivaParaVenta_ConAperturaAbierta_RetornaApertura()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var resultado = await _service.ObtenerAperturaActivaParaVentaAsync();

        Assert.NotNull(resultado);
        Assert.False(resultado!.Cerrada);
    }

    // =========================================================================
    // ObtenerMovimientosDeAperturaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerMovimientosDeApertura_SinMovimientos_RetornaVacio()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var resultado = await _service.ObtenerMovimientosDeAperturaAsync(apertura.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerMovimientosDeApertura_ConMovimientos_RetornaMovimientos()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 300m), "testuser");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 100m), "testuser");

        var resultado = await _service.ObtenerMovimientosDeAperturaAsync(apertura.Id);

        Assert.Equal(2, resultado.Count);
    }

    // =========================================================================
    // ObtenerCierrePorIdAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerCierrePorId_InexistentE_RetornaNull()
    {
        var resultado = await _service.ObtenerCierrePorIdAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerCierrePorId_Existente_RetornaCierre()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);
        var cierre = await CerrarCajaExactaAsync(apertura, efectivo: 500m);

        var resultado = await _service.ObtenerCierrePorIdAsync(cierre!.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cierre.Id, resultado!.Id);
    }

    // =========================================================================
    // ObtenerHistorialCierresAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerHistorialCierres_SinCierres_RetornaVacio()
    {
        var resultado = await _service.ObtenerHistorialCierresAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerHistorialCierres_ConCierre_RetornaCierres()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);
        await CerrarCajaExactaAsync(apertura, efectivo: 500m);

        var resultado = await _service.ObtenerHistorialCierresAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task ObtenerHistorialCierres_FiltroPorCajaId_RetornaSoloDeLaCaja()
    {
        var caja1 = await SeedCajaAsync();
        var caja2 = await SeedCajaAsync();

        var ap1 = await AbrirCajaAsync(caja1, 500m);
        await CerrarCajaExactaAsync(ap1, efectivo: 500m);

        var ap2 = await AbrirCajaAsync(caja2, 200m);
        await CerrarCajaExactaAsync(ap2, efectivo: 200m);

        var resultado = await _service.ObtenerHistorialCierresAsync(cajaId: caja1.Id);

        Assert.Single(resultado);
        Assert.All(resultado, c => Assert.Equal(ap1.Id, c.AperturaCajaId));
    }

    // =========================================================================
    // ObtenerDetallesAperturaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerDetallesApertura_AperturaInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ObtenerDetallesAperturaAsync(99999));
    }

    [Fact]
    public async Task ObtenerDetallesApertura_SinMovimientos_RetornaDetallesConCeros()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 800m);

        var resultado = await _service.ObtenerDetallesAperturaAsync(apertura.Id);

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado.TotalIngresos);
        Assert.Equal(0, resultado.TotalEgresos);
        Assert.Equal(800m, resultado.SaldoActual);
        Assert.Equal(0, resultado.CantidadMovimientos);
    }

    [Fact]
    public async Task ObtenerDetallesApertura_ConMovimientos_CalculaTotalesCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1_000m);
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 500m), "testuser");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 200m), "testuser");

        var resultado = await _service.ObtenerDetallesAperturaAsync(apertura.Id);

        Assert.Equal(500m, resultado.TotalIngresos);
        Assert.Equal(200m, resultado.TotalEgresos);
        Assert.Equal(1_300m, resultado.SaldoActual); // 1000 + 500 - 200
        Assert.Equal(2, resultado.CantidadMovimientos);
    }

    [Fact]
    public async Task ResumenRealPorMedioPago_UsaTipoPagoEstructuradoPrimero()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1_000m);

        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            TipoPago = TipoPago.Transferencia,
            Monto = 700m,
            Descripcion = "Venta estructurada",
            Usuario = "testuser",
            Observaciones = "Pago: Efectivo"
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerDetallesAperturaAsync(apertura.Id);

        var transferencia = Assert.Single(resultado.ResumenRealPorMedioPago, r => r.MedioPago == "Transferencia");
        Assert.Equal(700m, transferencia.TotalIngresos);
        Assert.DoesNotContain(resultado.ResumenRealPorMedioPago, r => r.MedioPago == "Efectivo" && r.TotalIngresos == 700m);
    }

    [Fact]
    public async Task ResumenRealPorMedioPago_LegacySinTipoPago_UsaFallbackActual()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1_000m);

        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaCheque,
            Monto = 800m,
            Descripcion = "Venta legacy",
            Usuario = "testuser",
            Observaciones = "Pago: Cheque"
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerDetallesAperturaAsync(apertura.Id);

        var cheque = Assert.Single(resultado.ResumenRealPorMedioPago, r => r.MedioPago == "Cheque");
        Assert.Equal(800m, cheque.TotalIngresos);
    }

    [Fact]
    public async Task ObtenerDetallesApertura_DebitoConRecargo_MuestraDesgloseSinCambiarTotalCobrado()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1_000m);

        var cliente = new Cliente
        {
            Nombre = "Cliente",
            Apellido = "Debito",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8]
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            AperturaCajaId = apertura.Id,
            Numero = "VD-REC",
            Estado = EstadoVenta.Facturada,
            TipoPago = TipoPago.TarjetaDebito,
            Total = 105m,
            DatosTarjeta = new DatosTarjeta
            {
                NombreTarjeta = "Maestro Debito",
                TipoTarjeta = TipoTarjeta.Debito,
                RecargoAplicado = 5m
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerDetallesAperturaAsync(apertura.Id);

        Assert.Equal(5m, resultado.TotalRecargoDebito);
        var tarjeta = Assert.Single(resultado.TotalesPorTipoPago);
        Assert.Equal("Tarjeta", tarjeta.TipoPago);
        Assert.Equal(105m, tarjeta.Total);
        Assert.Equal(5m, tarjeta.RecargoDebitoAplicado);
        Assert.Equal(105m, Assert.Single(resultado.VentasDelTurno).Total);
    }

    // =========================================================================
    // GenerarReporteCajaAsync
    // =========================================================================

    [Fact]
    public async Task GenerarReporteCaja_SinAperturas_RetornaReporteVacio()
    {
        var resultado = await _service.GenerarReporteCajaAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado.TotalAperturas);
        Assert.Equal(0m, resultado.TotalIngresos);
        Assert.Equal(0m, resultado.TotalEgresos);
    }

    [Fact]
    public async Task GenerarReporteCaja_ConMovimientos_SumaTotales()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1_000m);
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 400m), "testuser");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 150m), "testuser");

        var resultado = await _service.GenerarReporteCajaAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(1, resultado.TotalAperturas);
        Assert.Equal(400m, resultado.TotalIngresos);
        Assert.Equal(150m, resultado.TotalEgresos);
    }

    // =========================================================================
    // ObtenerEstadisticasCierresAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerEstadisticasCierres_SinCierres_RetornaContadoresCero()
    {
        var resultado = await _service.ObtenerEstadisticasCierresAsync();

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado.TotalCierres);
        Assert.Equal(0, resultado.CierresConDiferencia);
        Assert.Equal(0m, resultado.PorcentajeCierresExactos); // sin cierres → 0
    }

    [Fact]
    public async Task ObtenerEstadisticasCierres_CierreExacto_CierresConDiferenciaEsCero()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);
        await CerrarCajaExactaAsync(apertura, efectivo: 500m);

        var resultado = await _service.ObtenerEstadisticasCierresAsync();

        Assert.Equal(1, resultado.TotalCierres);
        Assert.Equal(0, resultado.CierresConDiferencia);
        Assert.Equal(100m, resultado.PorcentajeCierresExactos);
    }
}
