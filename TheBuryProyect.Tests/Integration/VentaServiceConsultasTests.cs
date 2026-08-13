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
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub mínimo — GetAllAsync/GetByIdAsync no usan ICurrentUserService
// ---------------------------------------------------------------------------

file sealed class StubCurrentUserConsultas : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => null;
}

/// <summary>
/// Tests de integración para las consultas de VentaService:
/// GetAllAsync (sin filtro, por clienteId, por número, por fechas, por estado,
/// por tipoPago, excluye eliminadas) y GetByIdAsync (existente, eliminada, inexistente).
/// </summary>
public class VentaServiceConsultasTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private static int _counter = 900;

    public VentaServiceConsultasTests()
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

        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            numberGenerator,
            new PrecioVigenteResolver(_context),
            new StubCurrentUserConsultas(),
            null!,
            null!,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        var c = new Cliente
        {
            Nombre = "Test",
            Apellido = "Consulta",
            TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"),
            Email = $"c{n}@test.com"
        };
        _context.Set<Cliente>().Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId,
        EstadoVenta estado = EstadoVenta.Cotizacion,
        TipoPago tipoPago = TipoPago.Efectivo,
        DateTime? fechaVenta = null,
        bool isDeleted = false,
        string? numero = null,
        EstadoAutorizacionVenta estAutorizacion = EstadoAutorizacionVenta.NoRequiere,
        int? creditoId = null,
        DateTime? fechaConfiguracionCredito = null)
    {
        var n = Interlocked.Increment(ref _counter);
        var v = new Venta
        {
            Numero = numero ?? $"VTA-{n:D6}",
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = tipoPago,
            FechaVenta = fechaVenta ?? DateTime.UtcNow,
            EstadoAutorizacion = estAutorizacion,
            IsDeleted = isDeleted,
            CreditoId = creditoId,
            FechaConfiguracionCredito = fechaConfiguracionCredito
        };
        _context.Set<Venta>().Add(v);
        await _context.SaveChangesAsync();
        return v;
    }

    /// <summary>
    /// VENTA-CREDITO-DATOS-HYDRATION: seedea un Credito con los campos que la autoridad
    /// vigente (Credito + Credito.Cuotas) persiste en cada etapa real observada en
    /// producción — Configurado deja MontoCuota/TotalAPagar en 0 porque solo se
    /// completan al generar cuotas (VentaService.GenerarCuotasCreditoAsync).
    /// </summary>
    private async Task<Credito> SeedCreditoAsync(
        int clienteId,
        EstadoCredito estado,
        decimal montoAprobado,
        int cantidadCuotas,
        decimal tasaInteres,
        decimal montoCuota = 0m,
        decimal totalAPagar = 0m,
        DateTime? fechaPrimeraCuota = null)
    {
        var n = Interlocked.Increment(ref _counter);
        var c = new Credito
        {
            ClienteId = clienteId,
            Numero = $"CRE-{n:D6}",
            MontoSolicitado = montoAprobado,
            MontoAprobado = montoAprobado,
            TasaInteres = tasaInteres,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = montoCuota,
            TotalAPagar = totalAPagar,
            SaldoPendiente = montoAprobado,
            Estado = estado,
            FechaPrimeraCuota = fechaPrimeraCuota
        };
        _context.Set<Credito>().Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — sin filtro
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_SinFiltro_DevuelveSoloNoEliminadas()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id);
        await SeedVentaAsync(cliente.Id, isDeleted: true);

        var resultado = await _service.GetAllAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task GetAll_SinFiltro_OrdenDescendentePorFecha()
    {
        var cliente = await SeedClienteAsync();
        var fecha1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fecha2 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedVentaAsync(cliente.Id, fechaVenta: fecha1);
        await SeedVentaAsync(cliente.Id, fechaVenta: fecha2);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(fecha2, resultado[0].FechaVenta);
        Assert.Equal(fecha1, resultado[1].FechaVenta);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por ClienteId
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroClienteId_DevuelveSoloDelCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        await SeedVentaAsync(c1.Id);
        await SeedVentaAsync(c2.Id);

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { ClienteId = c1.Id });

        Assert.All(resultado, v => Assert.Equal(c1.Id, v.ClienteId));
        Assert.Single(resultado);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por Número
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroNumero_DevuelveSoloCoincidentes()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id, numero: "VTA-MATCH-001");
        await SeedVentaAsync(cliente.Id, numero: "VTA-OTHER-002");

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { Numero = "MATCH" });

        Assert.Single(resultado);
        Assert.Contains("MATCH", resultado[0].Numero);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por fechas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroFechaDesde_ExcluyeAnteriores()
    {
        var cliente = await SeedClienteAsync();
        var anterior = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var posterior = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var corte = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedVentaAsync(cliente.Id, fechaVenta: anterior);
        await SeedVentaAsync(cliente.Id, fechaVenta: posterior);

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { FechaDesde = corte });

        Assert.Single(resultado);
        Assert.True(resultado[0].FechaVenta >= corte);
    }

    [Fact]
    public async Task GetAll_FiltroFechaHasta_ExcluyePosteriores()
    {
        var cliente = await SeedClienteAsync();
        var anterior = new DateTime(2024, 12, 30, 0, 0, 0, DateTimeKind.Utc);
        var posterior = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var corte = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedVentaAsync(cliente.Id, fechaVenta: anterior);
        await SeedVentaAsync(cliente.Id, fechaVenta: posterior);

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { FechaHasta = corte });

        Assert.Single(resultado);
        Assert.True(resultado[0].FechaVenta <= corte);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por Estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroEstado_DevuelveSoloEseEstado()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id, estado: EstadoVenta.Cotizacion);
        await SeedVentaAsync(cliente.Id, estado: EstadoVenta.Confirmada);

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { Estado = EstadoVenta.Confirmada });

        Assert.Single(resultado);
        Assert.All(resultado, v => Assert.Equal(EstadoVenta.Confirmada, v.Estado));
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por TipoPago
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroTipoPago_DevuelveSoloEseTipo()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.Efectivo);
        await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.CreditoPersonal);

        var resultado = await _service.GetAllAsync(new VentaFilterViewModel { TipoPago = TipoPago.CreditoPersonal });

        Assert.Single(resultado);
        Assert.All(resultado, v => Assert.Equal(TipoPago.CreditoPersonal, v.TipoPago));
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — filtro por EstadoAutorizacion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_FiltroEstadoAutorizacion_DevuelveSoloEseEstado()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id, estAutorizacion: EstadoAutorizacionVenta.NoRequiere);
        await SeedVentaAsync(cliente.Id, estAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion);

        var resultado = await _service.GetAllAsync(
            new VentaFilterViewModel { EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion });

        Assert.Single(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_Existente_DevuelveViewModel()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, numero: "VTA-GETBYID-001");

        var resultado = await _service.GetByIdAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(venta.Id, resultado!.Id);
        Assert.Equal("VTA-GETBYID-001", resultado.Numero);
    }

    [Fact]
    public async Task GetById_Eliminada_DevuelveNull()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, isDeleted: true);

        var resultado = await _service.GetByIdAsync(venta.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_Inexistente_DevuelveNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync — hidratación de Crédito Personal (VENTA-CREDITO-DATOS-HYDRATION)
    // Autoridad vigente: Credito + Credito.Cuotas (no VentaCreditoCuotas, legacy sin
    // filas en producción — ver auditoría VENTA-DETAILS-H2-AUDIT).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_CreditoGeneradoConCuotas_HidrataDatosCreditoDesdeCredito()
    {
        // Caso 1 del plan: CreditoConfigurado == true, VentaCreditoCuotas vacío (nunca se
        // escribe), Credito.Cuotas poblado (estado real post-confirmación de venta).
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(
            cliente.Id, EstadoCredito.Generado,
            montoAprobado: 100000m, cantidadCuotas: 2, tasaInteres: 5m,
            montoCuota: 55000m, totalAPagar: 110000m,
            fechaPrimeraCuota: new DateTime(2026, 9, 1));
        _context.Set<Cuota>().AddRange(
            new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                MontoCapital = 50000m,
                MontoInteres = 5000m,
                MontoTotal = 55000m,
                FechaVencimiento = new DateTime(2026, 9, 1),
                Estado = EstadoCuota.Pendiente
            },
            new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 2,
                MontoCapital = 50000m,
                MontoInteres = 5000m,
                MontoTotal = 55000m,
                FechaVencimiento = new DateTime(2026, 10, 1),
                Estado = EstadoCuota.Pendiente
            });
        await _context.SaveChangesAsync();

        var venta = await SeedVentaAsync(
            cliente.Id,
            estado: EstadoVenta.Confirmada,
            tipoPago: TipoPago.CreditoPersonal,
            creditoId: credito.Id,
            fechaConfiguracionCredito: DateTime.UtcNow);

        var resultado = await _service.GetByIdAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.True(resultado!.CreditoConfigurado);
        Assert.NotNull(resultado.DatosCreditoPersonall);
        var datos = resultado.DatosCreditoPersonall!;
        Assert.Equal(credito.Id, datos.CreditoId);
        Assert.Equal(100000m, datos.MontoAFinanciar);
        Assert.Equal(2, datos.CantidadCuotas);
        Assert.Equal(55000m, datos.MontoCuota);
        Assert.Equal(5m, datos.TasaInteresMensual);
        Assert.Equal(110000m, datos.TotalAPagar);
        Assert.True(datos.MontoCuotaDisponible);
        Assert.Equal(2, datos.Cuotas.Count);
        Assert.Equal(new DateTime(2026, 9, 1), datos.FechaPrimeraCuota);
    }

    [Fact]
    public async Task GetById_CreditoConfiguradoSinCuotasGeneradas_MuestraResumenConfigurado()
    {
        // Caso 2 del plan: Credito configurado, VentaCreditoCuotas vacío, Credito.Cuotas
        // vacío también (aún no se confirmó la venta), pero el plan ya tiene campos
        // reales en Credito. No debe interpretarse como "no configurado".
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(
            cliente.Id, EstadoCredito.Configurado,
            montoAprobado: 133100m, cantidadCuotas: 4, tasaInteres: 4m,
            fechaPrimeraCuota: new DateTime(2026, 9, 7));

        var venta = await SeedVentaAsync(
            cliente.Id,
            estado: EstadoVenta.Presupuesto,
            tipoPago: TipoPago.CreditoPersonal,
            creditoId: credito.Id,
            fechaConfiguracionCredito: DateTime.UtcNow);

        var resultado = await _service.GetByIdAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.True(resultado!.CreditoConfigurado);
        Assert.NotNull(resultado.DatosCreditoPersonall);
        var datos = resultado.DatosCreditoPersonall!;
        Assert.Equal(133100m, datos.MontoAFinanciar);
        Assert.Equal(4, datos.CantidadCuotas);
        Assert.Equal(4m, datos.TasaInteresMensual);
        Assert.False(datos.MontoCuotaDisponible);
        Assert.Empty(datos.Cuotas);
    }

    [Fact]
    public async Task GetById_CreditoPendienteConfiguracion_NoHidrataDatosCredito()
    {
        // Caso 3 del plan: PendienteConfiguracion no debe falsear DatosCreditoPersonall
        // como configurado, aunque Credito.MontoAprobado ya venga precargado desde la
        // venta (ver Credito.AnticipoPreseleccionado / intención pre-configuración).
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(
            cliente.Id, EstadoCredito.PendienteConfiguracion,
            montoAprobado: 16957m, cantidadCuotas: 0, tasaInteres: 0m);

        var venta = await SeedVentaAsync(
            cliente.Id,
            estado: EstadoVenta.PendienteFinanciacion,
            tipoPago: TipoPago.CreditoPersonal,
            creditoId: credito.Id);

        var resultado = await _service.GetByIdAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.False(resultado!.CreditoConfigurado);
        Assert.Null(resultado.DatosCreditoPersonall);
    }
}
