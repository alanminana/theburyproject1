using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// MORA-CONFIG-PRESERVACION — contrato de <see cref="MoraService.UpdateConfiguracionExpandidaAsync"/>
/// con lista de campos enviados (SQLite en memoria, sin HTTP). El flujo HTTP real está en
/// <see cref="MoraConfiguracionExpandidaPreservacionHttpTests"/>.
/// </summary>
public class MoraConfiguracionExpandidaPreservacionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MoraService _service;

    public MoraConfiguracionExpandidaPreservacionServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();
        // La actualización de configuración no usa crédito ni scoring.
        _service = new MoraService(_context, mapper, NullLogger<MoraService>.Instance,
            creditoService: null!, clienteScoringService: null!, RelojComercial.Sistema);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<ConfiguracionMora> SembrarAsync()
    {
        var config = new ConfiguracionMora
        {
            TipoTasaMora = TipoTasaMora.Diaria,
            TasaMoraBase = 4m,
            DiasGracia = 5,
            EscalonamientoActivo = true,
            TasaPrimerMes = 1.5m,
            NotificacionesActivas = true,
            CanalPreferido = CanalNotificacion.Email,
            MaximoCuotasAcuerdo = 12,
            PermitirCondonacionMora = true,
            ImpactarScorePorMora = true,
            PuntosMaximosARestar = 40
        };
        _context.ConfiguracionesMora.Add(config);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return config;
    }

    [Fact]
    public async Task ConCamposEnviados_AplicaSoloEsosYPreservaElResto()
    {
        var config = await SembrarAsync();

        // Simula el binder: lo no enviado queda en default (null/false).
        var vm = new ConfiguracionMoraExpandidaViewModel
        {
            Id = config.Id,
            TasaMoraBase = 6m,
            ImpactarScorePorMora = false,
            PuntosMaximosARestar = null
        };

        // Nombres con distinta capitalización: el binder de MVC es case-insensitive.
        await _service.UpdateConfiguracionExpandidaAsync(vm,
            new[] { "Id", "RowVersion", "tasamorabase", "ImpactarScorePorMora", "PuntosMaximosARestar", "__RequestVerificationToken" });

        _context.ChangeTracker.Clear();
        var actual = await _context.ConfiguracionesMora.AsNoTracking().SingleAsync(c => c.Id == config.Id);

        // Editados
        Assert.Equal(6m, actual.TasaMoraBase);
        Assert.False(actual.ImpactarScorePorMora);
        Assert.Null(actual.PuntosMaximosARestar);
        // Preservados
        Assert.Equal(TipoTasaMora.Diaria, actual.TipoTasaMora);
        Assert.Equal(5, actual.DiasGracia);
        Assert.True(actual.EscalonamientoActivo);
        Assert.Equal(1.5m, actual.TasaPrimerMes);
        Assert.True(actual.NotificacionesActivas);
        Assert.Equal(CanalNotificacion.Email, actual.CanalPreferido);
        Assert.Equal(12, actual.MaximoCuotasAcuerdo);
        Assert.True(actual.PermitirCondonacionMora);
        Assert.NotNull(actual.UpdatedAt);
    }

    [Fact]
    public async Task SinListaDeCampos_AplicaElViewModelCompleto()
    {
        // Contrato previo para llamadas internas con el ViewModel completo.
        var config = await SembrarAsync();

        await _service.UpdateConfiguracionExpandidaAsync(new ConfiguracionMoraExpandidaViewModel
        {
            Id = config.Id,
            TasaMoraBase = 2m
        });

        _context.ChangeTracker.Clear();
        var actual = await _context.ConfiguracionesMora.AsNoTracking().SingleAsync(c => c.Id == config.Id);
        Assert.Equal(2m, actual.TasaMoraBase);
        Assert.Null(actual.TipoTasaMora);
        Assert.False(actual.EscalonamientoActivo);
    }
}
