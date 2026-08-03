using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML3 — Configuración versionada de punitorios (ConfiguracionPunitorio).
//
// Reloj fake fijado en 2020-01-01: todas las vigencias "normales" de este archivo caen en 2025+
// (posteriores a hoy, no retroactivas) salvo los tests del grupo de retroactividad, que usan
// fechas explícitamente anteriores a 2020-01-01. Esto evita que un test de otro grupo dispare por
// accidente la puerta de retroactividad.
// ---------------------------------------------------------------------------

public sealed class ConfiguracionPunitorioServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj;
    private readonly ConfiguracionPunitorioService _service;

    public ConfiguracionPunitorioServiceTests()
    {
        _dataSource = $"{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"DataSource={_dataSource};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = CrearContexto();
        _context.Database.EnsureCreated();

        _reloj = new TheBuryProject.Tests.Helpers.RelojComercialFake(new DateOnly(2020, 1, 1));
        _service = new ConfiguracionPunitorioService(_context, _reloj);
    }

    private AppDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dataSource};Mode=Memory;Cache=Shared")
            .Options);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static ConfiguracionPunitorioComando Comando(
        decimal porcentaje = 10m,
        int periodoDias = 20,
        int diasGracia = 5,
        bool prorrateoDiario = true,
        bool aplicacionRetroactiva = false,
        DateOnly? vigenteDesde = null,
        bool activa = true,
        string? motivoCambio = "Configuración inicial de punitorios",
        bool autorizadoParaRetroactivo = false) => new()
    {
        Porcentaje = porcentaje,
        PeriodoDias = periodoDias,
        DiasGracia = diasGracia,
        ProrrateoDiario = prorrateoDiario,
        AplicacionRetroactiva = aplicacionRetroactiva,
        VigenteDesde = vigenteDesde ?? new DateOnly(2026, 8, 1),
        Activa = activa,
        MotivoCambio = motivoCambio,
        AutorizadoParaRetroactivo = autorizadoParaRetroactivo
    };

    // -------------------------------------------------------------------------
    // Entidad y validaciones
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearNuevaVersionAsync_ConValoresValidos_Persiste()
    {
        var creada = await _service.CrearNuevaVersionAsync(Comando());

        Assert.NotEqual(0, creada.Id);
        var enBd = await _context.ConfiguracionesPunitorio.AsNoTracking().SingleAsync(c => c.Id == creada.Id);
        Assert.Equal(10m, enBd.Porcentaje);
        Assert.Equal(20, enBd.PeriodoDias);
        Assert.Equal(5, enBd.DiasGracia);
        Assert.True(enBd.Activa);
        Assert.NotNull(enBd.CreatedBy); // auditoría automática (AppDbContext.SaveChangesAsync), no viene del comando
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_PorcentajeCero_EsValido()
    {
        var creada = await _service.CrearNuevaVersionAsync(Comando(porcentaje: 0m));
        Assert.Equal(0m, creada.Porcentaje);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_PorcentajeNegativo_SeRechaza()
    {
        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(porcentaje: -1m)));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.SolicitudInvalida, ex.Motivo);
        Assert.Empty(await _context.ConfiguracionesPunitorio.ToListAsync());
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_PeriodoCero_SeRechaza()
    {
        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(periodoDias: 0)));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.SolicitudInvalida, ex.Motivo);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_DiasGraciaNegativos_SeRechazan()
    {
        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(diasGracia: -1)));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.SolicitudInvalida, ex.Motivo);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_ProrrateoDiarioFalse_SeRechaza()
    {
        // PUN-ML8: el calculador (PUN-ML4) no soporta ProrrateoDiario=false. Antes de esta
        // corrección el servicio lo persistía igual — solo el calculador lo rechazaba después,
        // con la fila ya guardada. Defensa en profundidad server-side, no solo UI.
        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(prorrateoDiario: false)));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.SolicitudInvalida, ex.Motivo);
        Assert.Empty(await _context.ConfiguracionesPunitorio.ToListAsync());
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_Retroactiva_SinMotivo_SeRechaza()
    {
        var comando = Comando(
            vigenteDesde: new DateOnly(2019, 6, 1), // anterior a hoy (fake: 2020-01-01)
            aplicacionRetroactiva: true,
            autorizadoParaRetroactivo: true,
            motivoCambio: null);

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(comando));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_Retroactiva_SinFlagAplicacionRetroactiva_SeRechaza()
    {
        var comando = Comando(
            vigenteDesde: new DateOnly(2019, 6, 1),
            aplicacionRetroactiva: false,
            autorizadoParaRetroactivo: true);

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(comando));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_Retroactiva_SinAutorizacion_SeRechaza()
    {
        var comando = Comando(
            vigenteDesde: new DateOnly(2019, 6, 1),
            aplicacionRetroactiva: true,
            motivoCambio: "Corrección retroactiva",
            autorizadoParaRetroactivo: false);

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(comando));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_Retroactiva_ConMotivoYAutorizacion_SePermite()
    {
        var comando = Comando(
            vigenteDesde: new DateOnly(2019, 6, 1),
            aplicacionRetroactiva: true,
            motivoCambio: "Corrección retroactiva autorizada",
            autorizadoParaRetroactivo: true);

        var creada = await _service.CrearNuevaVersionAsync(comando);

        Assert.True(creada.AplicacionRetroactiva);
        Assert.Equal(new DateOnly(2019, 6, 1), creada.VigenteDesde);
    }

    // -------------------------------------------------------------------------
    // Versionado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearNuevaVersionAsync_NoModificaLaAnterior()
    {
        var v1 = await _service.CrearNuevaVersionAsync(Comando(porcentaje: 10m, vigenteDesde: new DateOnly(2025, 1, 1)));
        var v1Snapshot = await _context.ConfiguracionesPunitorio.AsNoTracking().SingleAsync(c => c.Id == v1.Id);

        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 15m, vigenteDesde: new DateOnly(2026, 8, 1)));

        var v1Recargada = await _context.ConfiguracionesPunitorio.AsNoTracking().SingleAsync(c => c.Id == v1.Id);
        Assert.Equal(v1Snapshot.Porcentaje, v1Recargada.Porcentaje);
        Assert.Equal(v1Snapshot.VigenteDesde, v1Recargada.VigenteDesde);
        Assert.Equal(v1Snapshot.RowVersion, v1Recargada.RowVersion); // nunca se le hizo UPDATE
    }

    [Fact]
    public async Task ListarHistorialAsync_ConservaTodasLasVersiones()
    {
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2025, 1, 1)));
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2025, 6, 1)));
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2026, 8, 1)));

        var historial = await _service.ListarHistorialAsync();

        Assert.Equal(3, historial.Count);
        Assert.Equal(
            new[] { new DateOnly(2026, 8, 1), new DateOnly(2025, 6, 1), new DateOnly(2025, 1, 1) },
            historial.Select(h => h.VigenteDesde));
    }

    [Fact]
    public async Task ObtenerVigenteAsync_DevuelveLaUltimaVigente()
    {
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 5m, vigenteDesde: new DateOnly(2025, 1, 1)));
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 10m, vigenteDesde: new DateOnly(2025, 6, 1)));

        var vigente = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(EstadoConfiguracionPunitorio.Activa, vigente.Estado);
        Assert.Equal(10m, vigente.Configuracion!.Porcentaje);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_FechaHistorica_DevuelveLaVersionCorrespondiente()
    {
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 5m, vigenteDesde: new DateOnly(2025, 1, 1)));
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 10m, vigenteDesde: new DateOnly(2025, 6, 1)));

        var vigenteHistorica = await _service.ObtenerVigenteAsync(new DateOnly(2025, 3, 1));

        Assert.Equal(5m, vigenteHistorica.Configuracion!.Porcentaje);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_VersionFutura_NoSeAplicaAntesDeSuFecha()
    {
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 5m, vigenteDesde: new DateOnly(2025, 1, 1)));
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 99m, vigenteDesde: new DateOnly(2030, 1, 1)));

        var vigente = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(5m, vigente.Configuracion!.Porcentaje); // la versión de 2030 todavía no rige
    }

    [Fact]
    public async Task ObtenerVigenteAsync_SoloExisteUnaVersionFutura_DevuelveAusente()
    {
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2030, 1, 1)));

        var resultado = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(EstadoConfiguracionPunitorio.Ausente, resultado.Estado);
        Assert.Null(resultado.Configuracion);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_SinNingunaVersionCreada_DevuelveAusente()
    {
        var resultado = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(EstadoConfiguracionPunitorio.Ausente, resultado.Estado);
        Assert.Null(resultado.Configuracion);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_VersionInactiva_SeDistingueDeAusencia()
    {
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2025, 1, 1), activa: false));

        var resultado = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(EstadoConfiguracionPunitorio.Inactiva, resultado.Estado);
        Assert.NotNull(resultado.Configuracion); // existe: solo está desactivada, no es "ausente"
    }

    [Fact]
    public async Task ObtenerVigenteAsync_VersionActivaConCero_SeDistingueDeAusenciaYDeInactiva()
    {
        await _service.CrearNuevaVersionAsync(Comando(porcentaje: 0m, vigenteDesde: new DateOnly(2025, 1, 1), activa: true));

        var resultado = await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Equal(EstadoConfiguracionPunitorio.Activa, resultado.Estado);
        Assert.Equal(0m, resultado.Configuracion!.Porcentaje);
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_MismaVigenciaQueUnaExistente_SeRechazaPorRestriccionPersistente()
    {
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2026, 6, 1)));

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2026, 6, 1))));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
        Assert.Single(await _context.ConfiguracionesPunitorio.ToListAsync());
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_VigenciaAnteriorAUnaExistente_SeRechaza()
    {
        await _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2026, 6, 1)));

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => _service.CrearNuevaVersionAsync(Comando(vigenteDesde: new DateOnly(2026, 3, 1))));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
        Assert.Single(await _context.ConfiguracionesPunitorio.ToListAsync());
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_DosAltasConcurrentesConLaMismaVigencia_SoloUnaPersiste()
    {
        using var contextoA = CrearContexto();
        using var contextoB = CrearContexto();
        var servicioA = new ConfiguracionPunitorioService(contextoA, _reloj);
        var servicioB = new ConfiguracionPunitorioService(contextoB, _reloj);

        var vigenteDesde = new DateOnly(2026, 9, 1);
        var intentoA = Task.Run(() => servicioA.CrearNuevaVersionAsync(Comando(vigenteDesde: vigenteDesde)));
        var intentoB = Task.Run(() => servicioB.CrearNuevaVersionAsync(Comando(vigenteDesde: vigenteDesde)));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(exito => exito));

        var filas = await _context.ConfiguracionesPunitorio.AsNoTracking()
            .Where(c => c.VigenteDesde == vigenteDesde)
            .ToListAsync();
        Assert.Single(filas);
    }

    /// <summary>El intento perdedor puede fallar por el chequeo previo o por el índice de la base; ambos son rechazo.</summary>
    private static async Task<bool> EjecutarProtegidoAsync(Task<ConfiguracionPunitorio> intento)
    {
        try
        {
            await intento;
            return true;
        }
        catch (ConfiguracionPunitorioRechazadaException)
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Separación de responsabilidades
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearNuevaVersionAsync_NoModificaConfiguracionMora()
    {
        var mora = new ConfiguracionMora { TasaMoraBase = 5m, DiasGracia = 3 };
        _context.ConfiguracionesMora.Add(mora);
        await _context.SaveChangesAsync();
        var rowVersionOriginal = mora.RowVersion;

        await _service.CrearNuevaVersionAsync(Comando());

        var moraRecargada = await _context.ConfiguracionesMora.AsNoTracking().SingleAsync(m => m.Id == mora.Id);
        Assert.Equal(5m, moraRecargada.TasaMoraBase);
        Assert.Equal(3, moraRecargada.DiasGracia);
        Assert.Equal(rowVersionOriginal, moraRecargada.RowVersion); // sin UPDATE: PUN-ML3 no toca ConfiguracionMora
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_SoloRastreaSuPropiaEntidad()
    {
        await _service.CrearNuevaVersionAsync(Comando());

        // Prueba estructural: si el servicio alguna vez empezara a leer/escribir Cuota, Credito o
        // PagoCuota (acoplamiento prohibido por el enunciado), este assert lo detecta sin necesidad
        // de sembrar esos grafos completos.
        var tiposRastreados = _context.ChangeTracker.Entries()
            .Select(e => e.Entity.GetType())
            .Distinct()
            .ToList();

        Assert.Equal(new[] { typeof(ConfiguracionPunitorio) }, tiposRastreados);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_LlamadoVariasVeces_NoProduceEfectosSecundarios()
    {
        await _service.CrearNuevaVersionAsync(Comando());
        _context.ChangeTracker.Clear();

        await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));
        await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));
        await _service.ObtenerVigenteAsync(new DateOnly(2026, 8, 1));

        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(1, await _context.ConfiguracionesPunitorio.CountAsync());
    }

    [Fact]
    public async Task ListarHistorialAsync_NoProduceEfectosSecundarios()
    {
        await _service.CrearNuevaVersionAsync(Comando());
        _context.ChangeTracker.Clear();

        await _service.ListarHistorialAsync();

        Assert.Empty(_context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CrearNuevaVersionAsync_UsaFechaComercialDelReloj_NoLaFechaDelSistema()
    {
        // Reloj fake muy en el futuro respecto a la fecha real del sistema: si el servicio usara
        // DateTime.Now/UtcNow en vez de IRelojComercial.HoyComercial, esta vigencia (futura para el
        // reloj real) no se detectaría como retroactiva y el alta pasaría sin exigir motivo ni
        // autorización. Como sí usa el reloj inyectado, se rechaza.
        var relojFuturo = new TheBuryProject.Tests.Helpers.RelojComercialFake(new DateOnly(2030, 1, 1));
        var servicio = new ConfiguracionPunitorioService(_context, relojFuturo);

        var comando = Comando(vigenteDesde: new DateOnly(2029, 6, 1)); // pasado para el reloj fake, futuro para el sistema real

        var ex = await Assert.ThrowsAsync<ConfiguracionPunitorioRechazadaException>(
            () => servicio.CrearNuevaVersionAsync(comando));

        Assert.Equal(MotivoRechazoConfiguracionPunitorio.Conflicto, ex.Motivo);
    }
}
