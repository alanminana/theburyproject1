using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// CSR-ML1 — T13 (histórico): cambiar la configuración del plan global DESPUÉS de que un crédito
// ya tiene sus cuotas persistidas no debe alterar retroactivamente esas cuotas.
//
// No modifica producción. Usa exclusivamente superficie ya existente
// (ConfiguracionCreditoPersonalCuota, FinancialCalculationService, Cuota/Credito/Cliente) para
// reproducir, sin depender de VentaService (WIP ajeno congelado), el mismo patrón que ya usa
// VentaService.GenerarCuotasCreditoAsync: simular el plan UNA vez y persistir el vector exacto
// en Cuota — nunca releerlo desde la configuración vigente.
// ---------------------------------------------------------------------------
public class CreditoPersonalCuotaSinRecargoHistoricoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _configuracionPagoService;
    private readonly FinancialCalculationService _financialService = new();

    public CreditoPersonalCuotaSinRecargoHistoricoTests()
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

        _configuracionPagoService = new ConfiguracionPagoService(
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
    public async Task T13_CambiarPorcentajeDelPlanDespuesDeConfirmar_NoAlteraLasCuotasYaPersistidas()
    {
        // 1) Plan global vigente al momento de confirmar: 10 cuotas, 10%.
        var plan = new ConfiguracionCreditoPersonalCuota
        {
            CantidadCuotas = 10,
            TasaMensual = 10m,
            Activo = true,
            Orden = 1
        };
        _context.ConfiguracionCreditoPersonalCuotas.Add(plan);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "CSR",
            Apellido = "ML1",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "csr-ml1@test.com"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Generado,
            MontoSolicitado = 100_000m,
            MontoAprobado = 100_000m,
            SaldoPendiente = 100_000m,
            TasaInteres = plan.TasaMensual!.Value, // snapshot del % vigente al confirmar
            CantidadCuotas = plan.CantidadCuotas,
            FechaSolicitud = DateTime.UtcNow,
            FechaPrimeraCuota = new DateTime(2026, 9, 10)
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        // 2) Generación de cuotas al confirmar: mismo cálculo canónico que
        // VentaService.GenerarCuotasCreditoAsync (SimularPlanCredito), snapshot persistido en
        // Cuota — nunca una referencia viva a la configuración.
        var simulacion = _financialService.SimularPlanCredito(
            credito.MontoAprobado,
            0m,
            credito.CantidadCuotas,
            credito.TasaInteres,
            0m,
            credito.FechaPrimeraCuota!.Value);

        var fechaCuota = credito.FechaPrimeraCuota.Value;
        foreach (var item in simulacion.Cuotas)
        {
            _context.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = item.NumeroCuota,
                MontoCapital = item.Capital,
                MontoInteres = item.Interes,
                MontoTotal = item.Total,
                FechaVencimiento = fechaCuota,
                Estado = EstadoCuota.Pendiente
            });
            fechaCuota = fechaCuota.AddMonths(1);
        }
        await _context.SaveChangesAsync();

        var totalCuota1Original = simulacion.Cuotas[0].Total;
        Assert.Equal(11_000m, totalCuota1Original); // 100000/10 cuotas + 10% -> 11000 c/u

        // 3) Cambia la configuración del plan global DESPUÉS de confirmado (25%, otro plan).
        plan.TasaMensual = 25m;
        await _context.SaveChangesAsync();

        // Confirmamos que ResolverPlanesCreditoPersonalAsync ya ve el nuevo 25% (el cambio es
        // real y vigente para ventas nuevas)...
        var planesVigentes = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(Array.Empty<int>());
        Assert.Equal(25m, planesVigentes.BuscarPlan(10)!.TasaMensual);

        // ...pero las cuotas YA PERSISTIDAS del crédito confirmado no se recalculan ni se leen de
        // nuevo desde la configuración: siguen reflejando el 10% vigente al momento de generarlas.
        var cuotasPersistidas = await _context.Cuotas
            .Where(c => c.CreditoId == credito.Id)
            .OrderBy(c => c.NumeroCuota)
            .ToListAsync();

        Assert.Equal(10, cuotasPersistidas.Count);
        Assert.All(cuotasPersistidas, c => Assert.Equal(11_000m, c.MontoTotal));
        Assert.Equal(110_000m, cuotasPersistidas.Sum(c => c.MontoTotal));
    }
}
