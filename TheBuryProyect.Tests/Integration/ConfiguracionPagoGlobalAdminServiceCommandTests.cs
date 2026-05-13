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

public sealed class ConfiguracionPagoGlobalAdminServiceCommandTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _service;

    public ConfiguracionPagoGlobalAdminServiceCommandTests()
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

        _service = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CrearPlanGlobal_CreaPlanGeneralValido()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.Transferencia);

        var resultado = await _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
        {
            ConfiguracionPagoId = medio.Id,
            CantidadCuotas = 1,
            AjustePorcentaje = -5m,
            Etiqueta = "Transferencia",
            Orden = 1
        });

        var plan = await _context.ConfiguracionPagoPlanes.SingleAsync();
        Assert.Equal(resultado.Id, plan.Id);
        Assert.Null(plan.ConfiguracionTarjetaId);
        Assert.Equal(TipoPago.Transferencia, plan.TipoPago);
        Assert.Equal("Transferencia", plan.Etiqueta);
    }

    [Fact]
    public async Task CrearPlanGlobal_CreaPlanPorTarjetaValido()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaAsync(medio);

        var resultado = await _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
        {
            ConfiguracionPagoId = medio.Id,
            ConfiguracionTarjetaId = tarjeta.Id,
            CantidadCuotas = 6,
            AjustePorcentaje = 12.5m,
            Etiqueta = "Visa 6"
        });

        Assert.Equal(tarjeta.Id, resultado.ConfiguracionTarjetaId);
        Assert.Equal("Visa", resultado.NombreTarjeta);
        Assert.Equal(6, Assert.Single(await _context.ConfiguracionPagoPlanes.ToListAsync()).CantidadCuotas);
    }

    [Fact]
    public async Task CrearPlanGlobal_RechazaCuotasCero()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.Efectivo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
            {
                ConfiguracionPagoId = medio.Id,
                CantidadCuotas = 0,
                AjustePorcentaje = 0m
            }));

        Assert.Contains("cuotas", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_context.ConfiguracionPagoPlanes);
    }

    [Theory]
    [InlineData(-100.0001)]
    [InlineData(1000)]
    public async Task CrearPlanGlobal_RechazaPorcentajeFueraDeRango(decimal ajuste)
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.MercadoPago);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
            {
                ConfiguracionPagoId = medio.Id,
                CantidadCuotas = 1,
                AjustePorcentaje = ajuste
            }));

        Assert.Contains("porcentaje", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_context.ConfiguracionPagoPlanes);
    }

    [Fact]
    public async Task CrearPlanGlobal_RechazaDuplicadoActivoAntesDeDb()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        await SeedPlanAsync(medio, cuotas: 3, activo: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
            {
                ConfiguracionPagoId = medio.Id,
                CantidadCuotas = 3,
                AjustePorcentaje = 5m,
                Activo = true
            }));

        Assert.Contains("Ya existe un plan activo", ex.Message);
        Assert.Single(_context.ConfiguracionPagoPlanes);
    }

    [Fact]
    public async Task CrearPlanGlobal_PermiteDuplicadoInactivoHistorico()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.MercadoPago);
        await SeedPlanAsync(medio, cuotas: 3, activo: true);

        await _service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
        {
            ConfiguracionPagoId = medio.Id,
            CantidadCuotas = 3,
            AjustePorcentaje = 8m,
            Activo = false
        });

        Assert.Equal(2, await _context.ConfiguracionPagoPlanes.CountAsync());
    }

    [Fact]
    public async Task CambiarEstadoPlanGlobal_ActivaEInactivaPlan()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.Cheque);
        var plan = await SeedPlanAsync(medio, cuotas: 1, activo: true);

        var inactivado = await _service.CambiarEstadoPlanGlobalAsync(plan.Id, activo: false);
        var activado = await _service.CambiarEstadoPlanGlobalAsync(plan.Id, activo: true);

        Assert.True(inactivado);
        Assert.True(activado);
        Assert.True((await _context.ConfiguracionPagoPlanes.SingleAsync()).Activo);
    }

    [Fact]
    public async Task CambiarEstadoPlanGlobal_RechazaActivarDuplicadoActivo()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        await SeedPlanAsync(medio, cuotas: 6, activo: true);
        var historico = await SeedPlanAsync(medio, cuotas: 6, activo: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CambiarEstadoPlanGlobalAsync(historico.Id, activo: true));

        Assert.Contains("Ya existe un plan activo", ex.Message);
    }

    private async Task<ConfiguracionPago> SeedConfiguracionPagoAsync(TipoPago tipoPago)
    {
        var medio = new ConfiguracionPago
        {
            TipoPago = tipoPago,
            Nombre = tipoPago.ToString(),
            Activo = true
        };

        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        return medio;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaAsync(ConfiguracionPago medio)
    {
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = medio.Id,
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Credito,
            Activa = true,
            PermiteCuotas = true,
            CantidadMaximaCuotas = 12
        };

        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ConfiguracionPagoPlan> SeedPlanAsync(ConfiguracionPago medio, int cuotas, bool activo)
    {
        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id,
            TipoPago = medio.TipoPago,
            CantidadCuotas = cuotas,
            Activo = activo,
            AjustePorcentaje = 0m
        };

        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }
}
