using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// En un servidor con la zona del proceso en UTC (Docker/Linux), entre las 21:00 y las 24:00 de Argentina <c>TheBuryProject.Services.RelojComercial.Sistema.InicioDiaComercial</c> y
/// <c>DateTime.UtcNow.Date</c> ya son el día siguiente. Las reglas de negocio por día (alertas de mora, vencimiento de documentos)
/// deben usar el día comercial argentino. Aquí el instante fijo es 2026-11-19 00:55 UTC = 2026-11-18 21:55 en Argentina.
/// </summary>
public class DiaComercialServidorUtcTests : IDisposable
{
    private static readonly RelojComercial Reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 11, 19, 0, 55, 0, TimeSpan.Zero));
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public DiaComercialServidorUtcTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void ElInstanteDePrueba_EsElDia18ComercialAunqueEnUtcYaSeaElDia19()
    {
        Assert.Equal(new DateOnly(2026, 11, 18), Reloj.HoyComercial);
    }

    [Fact]
    public async Task MarcarVencidos_UnDocumentoQueVenceHoyComercial_NoSeMarcaVencido_ElDeAyerSi()
    {
        var cliente = new Cliente { Nombre = "Dia", Apellido = "Comercial", NumeroDocumento = "30111000", TipoDocumento = "DNI" };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        DocumentoCliente Doc(TipoDocumentoCliente tipo, DateTime vence) => new()
        {
            ClienteId = cliente.Id, TipoDocumento = tipo, NombreArchivo = "a.pdf", RutaArchivo = "uploads/a.pdf",
            Estado = EstadoDocumento.Verificado, FechaSubida = DateTime.UtcNow, FechaVencimiento = vence
        };
        var venceHoy = Doc(TipoDocumentoCliente.DNI, new DateTime(2026, 11, 18));
        var venceAyer = Doc(TipoDocumentoCliente.ReciboSueldo, new DateTime(2026, 11, 17));
        _context.DocumentosCliente.AddRange(venceHoy, venceAyer);
        await _context.SaveChangesAsync();
        var servicio = new DocumentoClienteService(_context, _mapper, NullLogger<DocumentoClienteService>.Instance, null!, Reloj);

        await servicio.MarcarVencidosAsync();

        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoDocumento.Verificado, (await _context.DocumentosCliente.FindAsync(venceHoy.Id))!.Estado);
        Assert.Equal(EstadoDocumento.Vencido, (await _context.DocumentosCliente.FindAsync(venceAyer.Id))!.Estado);
    }

    [Fact]
    public async Task AlertasDeMora_UnaCuotaQueVenceHoyComercial_TieneCeroDiasRelativos()
    {
        var config = new ConfiguracionMora { TasaMoraBase = 0.1m, DiasGracia = 3, ProcesoAutomaticoActivo = true, HoraEjecucionDiaria = new TimeSpan(8, 0, 0) };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();
        _context.Set<AlertaMora>().AddRange(
            new AlertaMora { ConfiguracionMoraId = config.Id, DiasRelativoVencimiento = 0, ColorAlerta = "#00AA00", Descripcion = "Vence hoy", NivelPrioridad = 1, Activa = true, Orden = 1 },
            new AlertaMora { ConfiguracionMoraId = config.Id, DiasRelativoVencimiento = 1, ColorAlerta = "#FF0000", Descripcion = "Un día", NivelPrioridad = 2, Activa = true, Orden = 2 });
        await _context.SaveChangesAsync();
        var servicio = new ConfiguracionMoraService(_context, _mapper, NullLogger<ConfiguracionMoraService>.Instance, Reloj);
        var cuota = new CuotaViewModel { FechaVencimiento = new DateTime(2026, 11, 18), Estado = EstadoCuota.Vencida, MontoTotal = 1000m };

        await servicio.AplicarAlertasMoraAsync(new List<CuotaViewModel> { cuota });

        // Con TheBuryProject.Services.RelojComercial.Sistema.InicioDiaComercial en un servidor UTC serían 1 día y se aplicaría la alerta roja de "1 día" un día antes.
        Assert.Equal("#00AA00", cuota.ColorAlerta);
    }
}
