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
/// Tests de integración para ConfiguracionMoraService.AplicarAlertasMoraAsync.
/// Verifican que los campos ColorAlerta/DescripcionAlerta/NivelPrioridad se asignan
/// correctamente según los días de atraso y la configuración de alertas activas.
/// </summary>
public class ConfiguracionMoraServiceAplicarAlertasTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionMoraService _service;

    public ConfiguracionMoraServiceAplicarAlertasTests()
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

        _service = new ConfiguracionMoraService(
            _context,
            mapper,
            NullLogger<ConfiguracionMoraService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private CuotaViewModel CuotaVencidaHace(int diasAtras) => new()
    {
        FechaVencimiento = DateTime.Today.AddDays(-diasAtras),
        Estado = EstadoCuota.Vencida,
        MontoTotal = 1000m
    };

    private async Task SeedAlertas(params (int dias, string color, string? desc, int prioridad)[] alertas)
    {
        // ConfiguracionMora requerida como padre (AlertaMora tiene FK a ella)
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 0.1m,
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        int orden = 1;
        foreach (var (dias, color, desc, prioridad) in alertas)
        {
            _context.Set<AlertaMora>().Add(new AlertaMora
            {
                ConfiguracionMoraId = config.Id,
                DiasRelativoVencimiento = dias,
                ColorAlerta = color,
                Descripcion = desc,
                NivelPrioridad = prioridad,
                Activa = true,
                Orden = orden++
            });
        }

        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Lista vacía
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_ListaVacia_NoHaceNada()
    {
        // No debe lanzar ni acceder a BD innecesariamente
        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel>());
    }

    // -------------------------------------------------------------------------
    // Sin configuración en BD → defaults
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_SinConfiguracion_UsaDefaultRojo()
    {
        // Sin ConfiguracionMora en BD → GetConfiguracionAsync devuelve default
        // con una alerta: DiasRelativoVencimiento=0, color=#FF0000, prioridad=5
        var cuota = CuotaVencidaHace(1); // 1 día de atraso >= 0 → aplica default

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        Assert.Equal("#FF0000", cuota.ColorAlerta);
        Assert.Equal("Cuota vencida", cuota.DescripcionAlerta);
        Assert.Equal(5, cuota.NivelPrioridad);
    }

    // -------------------------------------------------------------------------
    // Alerta por días de atraso
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_CuotaEnPrimerRango_AsignaAlertaCorrecta()
    {
        // 3 alertas: >=0 amarillo, >=5 naranja, >=15 rojo
        await SeedAlertas(
            (0, "#FFFF00", "Vencida", 3),
            (5, "#FFA500", "Mora moderada", 4),
            (15, "#FF0000", "Mora grave", 5));

        var cuota = CuotaVencidaHace(2); // 2 días >= 0 pero < 5 → amarillo

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        Assert.Equal("#FFFF00", cuota.ColorAlerta);
        Assert.Equal("Vencida", cuota.DescripcionAlerta);
        Assert.Equal(3, cuota.NivelPrioridad);
    }

    [Fact]
    public async Task AplicarAlertasMora_CuotaEnSegundoRango_AsignaAlertaMasAlta()
    {
        await SeedAlertas(
            (0, "#FFFF00", "Vencida", 3),
            (5, "#FFA500", "Mora moderada", 4),
            (15, "#FF0000", "Mora grave", 5));

        var cuota = CuotaVencidaHace(7); // 7 días >= 5 pero < 15 → naranja

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        Assert.Equal("#FFA500", cuota.ColorAlerta);
        Assert.Equal("Mora moderada", cuota.DescripcionAlerta);
        Assert.Equal(4, cuota.NivelPrioridad);
    }

    [Fact]
    public async Task AplicarAlertasMora_CuotaEnTercerRango_AsignaAlertaMaxima()
    {
        await SeedAlertas(
            (0, "#FFFF00", "Vencida", 3),
            (5, "#FFA500", "Mora moderada", 4),
            (15, "#FF0000", "Mora grave", 5));

        var cuota = CuotaVencidaHace(20); // 20 días >= 15 → rojo

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        Assert.Equal("#FF0000", cuota.ColorAlerta);
        Assert.Equal("Mora grave", cuota.DescripcionAlerta);
        Assert.Equal(5, cuota.NivelPrioridad);
    }

    [Fact]
    public async Task AplicarAlertasMora_MultiplesAlertasMismoDia_UsaLaMasAlta()
    {
        // Dos alertas con mismo umbral — OrderByDescending toma la de mayor días,
        // pero con mismo valor (5) ambas califican; la de mayor DiasRelativoVencimiento
        // (5 vs 5) tiene el mismo valor → cualquiera de las dos. Lo importante es
        // que no explota y asigna una.
        await SeedAlertas(
            (0, "#FFFF00", "Vencida leve", 2),
            (5, "#FFA500", "Mora A", 4),
            (5, "#FF6600", "Mora B", 3));

        var cuota = CuotaVencidaHace(5);

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        // Cualquiera de las dos alertas de 5 días es válida
        Assert.True(cuota.ColorAlerta == "#FFA500" || cuota.ColorAlerta == "#FF6600");
        Assert.NotNull(cuota.DescripcionAlerta);
    }

    // -------------------------------------------------------------------------
    // Alertas inactivas no aplican
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_AlertaInactiva_NoSeAplica()
    {
        // Alerta de 0 días pero inactiva → no hay ninguna alerta aplicable
        // → usa defaults del view model (ColorAlerta="#FF0000", NivelPrioridad=5)
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 0.1m,
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        _context.Set<AlertaMora>().Add(new AlertaMora
        {
            ConfiguracionMoraId = config.Id,
            DiasRelativoVencimiento = 0,
            ColorAlerta = "#00FF00",
            Descripcion = "No debería aplicar",
            NivelPrioridad = 1,
            Activa = false, // <-- inactiva
            Orden = 1
        });
        await _context.SaveChangesAsync();

        var cuota = CuotaVencidaHace(5);
        cuota.ColorAlerta = "#AAAAAA"; // valor previo
        cuota.DescripcionAlerta = null;
        cuota.NivelPrioridad = 1;

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        // La alerta inactiva no debe aplicarse
        Assert.NotEqual("#00FF00", cuota.ColorAlerta);
        // El fallback del service es "#FF0000" cuando no hay alerta aplicable
        Assert.Equal("#FF0000", cuota.ColorAlerta);
        Assert.Equal("Cuota vencida", cuota.DescripcionAlerta);
        Assert.Equal(5, cuota.NivelPrioridad);
    }

    // -------------------------------------------------------------------------
    // Múltiples cuotas con distintos rangos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_MultiplesCuotas_CadaUnaRecibeSuAlerta()
    {
        await SeedAlertas(
            (0, "#FFFF00", "Vencida", 3),
            (10, "#FF0000", "Mora grave", 5));

        var cuota1 = CuotaVencidaHace(3);   // 3 días → amarillo
        var cuota2 = CuotaVencidaHace(15);  // 15 días → rojo

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota1, cuota2 });

        Assert.Equal("#FFFF00", cuota1.ColorAlerta);
        Assert.Equal("#FF0000", cuota2.ColorAlerta);
    }

    // -------------------------------------------------------------------------
    // Cuota con exactamente el umbral de días → aplica esa alerta (>=)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarAlertasMora_CuotaExactamenteEnUmbral_AplicaAlerta()
    {
        await SeedAlertas(
            (0, "#FFFF00", "Vencida", 2),
            (5, "#FF0000", "Grave", 5));

        var cuota = CuotaVencidaHace(5); // exactamente 5 días → aplica la de 5

        await _service.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        Assert.Equal("#FF0000", cuota.ColorAlerta);
        Assert.Equal(5, cuota.NivelPrioridad);
    }
}
