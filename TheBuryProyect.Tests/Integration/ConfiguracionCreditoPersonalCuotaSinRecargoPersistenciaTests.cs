using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// CSR-ML2 — Persistencia de "cuotas específicas sin recargo, configurables por plan" (Crédito
// Personal). Cubre V7 (guardar/leer), V8 (update sin huérfanos) y V9 (cascade al eliminar el
// plan) del spec CSR-ML2, contra el modelo y el servicio reales:
// `ConfiguracionCreditoPersonalCuotaSinRecargo` + `ConfiguracionPagoService`
// .GetCuotasSinRecargoAsync/GuardarCuotasSinRecargoCreditoPersonalAsync. Las validaciones puras
// (V1-V6) viven en CreditoPersonalCuotaSinRecargoContratoTests (Unit, sin DB). No toca
// FinancialCalculationService ni ningún call site financiero: sigue siendo persistencia pura.
// ---------------------------------------------------------------------------
public class ConfiguracionCreditoPersonalCuotaSinRecargoPersistenciaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _service;

    public ConfiguracionCreditoPersonalCuotaSinRecargoPersistenciaTests()
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

        _service = new ConfiguracionPagoService(_context, mapper, NullLogger<ConfiguracionPagoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<ConfiguracionCreditoPersonalCuota> CrearPlanAsync(
        int cantidadCuotas = 10, decimal? tasaMensual = 10m)
    {
        var plan = new ConfiguracionCreditoPersonalCuota
        {
            CantidadCuotas = cantidadCuotas,
            TasaMensual = tasaMensual,
            Activo = true,
            Orden = 1
        };
        _context.ConfiguracionCreditoPersonalCuotas.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    [Fact]
    public async Task V7_GuardarUnoTresCinco_RecargarDevuelveExactamenteUnoTresCinco()
    {
        var plan = await CrearPlanAsync();

        var (ok, errores) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 1, 3, 5 }, "tester");

        Assert.True(ok, string.Join("; ", errores));
        Assert.Empty(errores);

        var recargadas = await _service.GetCuotasSinRecargoAsync(plan.Id);

        Assert.Equal(new[] { 1, 3, 5 }, recargadas);
    }

    [Fact]
    public async Task V8_Update_CambiarUnoTresCincoPorDosDiez_NoDejaFilasHuerfanas()
    {
        var plan = await CrearPlanAsync();

        var (ok1, _) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 1, 3, 5 }, "tester");
        Assert.True(ok1);

        var (ok2, errores2) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 2, 10 }, "tester");
        Assert.True(ok2, string.Join("; ", errores2));

        var recargadas = await _service.GetCuotasSinRecargoAsync(plan.Id);
        Assert.Equal(new[] { 2, 10 }, recargadas);

        // Sin filas huérfanas: la tabla completa para este plan tiene exactamente 2 filas (no 5).
        var totalFilasDelPlan = await _context.ConfiguracionCreditoPersonalCuotasSinRecargo
            .CountAsync(c => c.ConfiguracionCreditoPersonalCuotaId == plan.Id);
        Assert.Equal(2, totalFilasDelPlan);
    }

    [Fact]
    public async Task V9_EliminarElPlan_BorraEnCascadaLasCuotasSinRecargo_SinHuerfanos()
    {
        var plan = await CrearPlanAsync();

        var (ok, _) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 1, 3, 5 }, "tester");
        Assert.True(ok);

        Assert.Equal(3, await _context.ConfiguracionCreditoPersonalCuotasSinRecargo.CountAsync());

        _context.ConfiguracionCreditoPersonalCuotas.Remove(plan);
        await _context.SaveChangesAsync();

        var huerfanas = await _context.ConfiguracionCreditoPersonalCuotasSinRecargo
            .Where(c => c.ConfiguracionCreditoPersonalCuotaId == plan.Id)
            .ToListAsync();
        Assert.Empty(huerfanas);
        Assert.Equal(0, await _context.ConfiguracionCreditoPersonalCuotasSinRecargo.CountAsync());
    }

    [Fact]
    public async Task GuardarCuotasSinRecargo_PlanInexistente_DevuelveErrorSinPersistirNada()
    {
        var (ok, errores) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            configuracionCreditoPersonalCuotaId: 999_999, new[] { 1 }, "tester");

        Assert.False(ok);
        Assert.NotEmpty(errores);
        Assert.Equal(0, await _context.ConfiguracionCreditoPersonalCuotasSinRecargo.CountAsync());
    }

    [Fact]
    public async Task GuardarCuotasSinRecargo_TotalidadConRecargoPositivo_NoPersisteYDevuelveError()
    {
        var plan = await CrearPlanAsync(cantidadCuotas: 10, tasaMensual: 10m);

        var (ok, errores) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, Enumerable.Range(1, 10).ToArray(), "tester");

        Assert.False(ok);
        Assert.NotEmpty(errores);
        Assert.Equal(0, await _context.ConfiguracionCreditoPersonalCuotasSinRecargo.CountAsync());
    }

    [Fact]
    public async Task GuardarCuotasSinRecargo_TotalidadConCeroPorciento_PersisteLasDiez()
    {
        var plan = await CrearPlanAsync(cantidadCuotas: 10, tasaMensual: 0m);

        var (ok, errores) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, Enumerable.Range(1, 10).ToArray(), "tester");

        Assert.True(ok, string.Join("; ", errores));

        var recargadas = await _service.GetCuotasSinRecargoAsync(plan.Id);
        Assert.Equal(Enumerable.Range(1, 10), recargadas);
    }

    // -------------------------------------------------------------------------
    // CSR-ML5 — "Cambio de cantidad de cuotas": si el plan pasa de 10 a 6 cuotas, una selección
    // previa que incluía números 7-10 queda fuera de rango del NUEVO tamaño y el guardado debe
    // rechazarla sin dejar estado parcial (ni tocar las filas existentes); una selección dentro
    // del nuevo rango sigue guardando/actualizando normalmente, sin filas huérfanas del tamaño
    // anterior.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambioDeCantidadDeCuotas_SeleccionPreviaFueraDelNuevoRango_SeRechazaSinTocarLoExistente()
    {
        var plan = await CrearPlanAsync(cantidadCuotas: 10, tasaMensual: 10m);

        var (okInicial, erroresIniciales) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 1, 3, 5, 9 }, "tester");
        Assert.True(okInicial, string.Join("; ", erroresIniciales));

        // El plan pasa de 10 a 6 cuotas (simula el cambio de CantidadCuotas del plan).
        plan.CantidadCuotas = 6;
        await _context.SaveChangesAsync();

        // Reintentar la MISMA seleccion (incluye 9, ahora fuera de [1,6]) debe rechazarse.
        var (okRepeticion, erroresRepeticion) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(
            plan.Id, new[] { 1, 3, 5, 9 }, "tester");

        Assert.False(okRepeticion);
        Assert.NotEmpty(erroresRepeticion);

        // No dejo estado parcial: la seleccion previa (del tamano de 10 cuotas) sigue intacta.
        var recargadasTrasElRechazo = await _service.GetCuotasSinRecargoAsync(plan.Id);
        Assert.Equal(new[] { 1, 3, 5, 9 }, recargadasTrasElRechazo);
    }

    [Fact]
    public async Task CambioDeCantidadDeCuotas_SeleccionDentroDelNuevoRango_ReemplazaSinFilasHuerfanas()
    {
        var plan = await CrearPlanAsync(cantidadCuotas: 10, tasaMensual: 10m);

        await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(plan.Id, new[] { 1, 3, 5, 9 }, "tester");

        plan.CantidadCuotas = 6;
        await _context.SaveChangesAsync();

        var (ok, errores) = await _service.GuardarCuotasSinRecargoCreditoPersonalAsync(plan.Id, new[] { 1, 3 }, "tester");

        Assert.True(ok, string.Join("; ", errores));

        var recargadas = await _service.GetCuotasSinRecargoAsync(plan.Id);
        Assert.Equal(new[] { 1, 3 }, recargadas);

        // Sin filas huerfanas del tamano anterior: exactamente 2 filas para este plan (no 4).
        var totalFilas = await _context.ConfiguracionCreditoPersonalCuotasSinRecargo
            .CountAsync(c => c.ConfiguracionCreditoPersonalCuotaId == plan.Id);
        Assert.Equal(2, totalFilas);
    }
}
