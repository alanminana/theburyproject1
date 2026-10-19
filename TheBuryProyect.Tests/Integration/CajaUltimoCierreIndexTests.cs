using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Rescate del "ultimo cierre" de Caja/Index: el servicio devuelve en una sola consulta agrupada el
/// cierre vigente mas reciente por caja, y el controller lo expresa en hora comercial.
/// </summary>
public class CajaUltimoCierreServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly ConteoComandos _conteo = new();

    public CajaUltimoCierreServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_conteo)
            .Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotificacionService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class ConteoComandos : DbCommandInterceptor
    {
        public int Lecturas { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Lecturas++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task<Caja> SeedCajaAsync()
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var caja = new Caja { Codigo = codigo, Nombre = "Caja-" + codigo, Activa = true, Estado = EstadoCaja.Cerrada };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();
        return caja;
    }

    private async Task SeedCierreAsync(Caja caja, DateTime fechaCierreUtc, bool cierreEliminado = false, bool aperturaEliminada = false)
    {
        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            FechaApertura = fechaCierreUtc.AddHours(-8),
            MontoInicial = 0m,
            UsuarioApertura = "test",
            Cerrada = true,
            IsDeleted = aperturaEliminada
        };
        _context.AperturasCaja.Add(apertura);
        await _context.SaveChangesAsync();
        _context.CierresCaja.Add(new CierreCaja
        {
            AperturaCajaId = apertura.Id,
            FechaCierre = fechaCierreUtc,
            UsuarioCierre = "test",
            IsDeleted = cierreEliminado
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task VariosCierres_DevuelveElMasReciente()
    {
        var caja = await SeedCajaAsync();
        await SeedCierreAsync(caja, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));
        await SeedCierreAsync(caja, new DateTime(2026, 10, 3, 21, 30, 0, DateTimeKind.Utc));
        await SeedCierreAsync(caja, new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));

        var r = await _service.ObtenerUltimosCierresPorCajaAsync();

        Assert.Equal(new DateTime(2026, 10, 3, 21, 30, 0), r[caja.Id]);
    }

    [Fact]
    public async Task CierreEliminado_SeExcluye()
    {
        var caja = await SeedCajaAsync();
        await SeedCierreAsync(caja, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));
        await SeedCierreAsync(caja, new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc), cierreEliminado: true);

        var r = await _service.ObtenerUltimosCierresPorCajaAsync();

        Assert.Equal(new DateTime(2026, 10, 1, 20, 0, 0), r[caja.Id]);
    }

    [Fact]
    public async Task AperturaEliminada_ExcluyeSusCierres()
    {
        var caja = await SeedCajaAsync();
        await SeedCierreAsync(caja, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));
        await SeedCierreAsync(caja, new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc), aperturaEliminada: true);
        var soloEliminada = await SeedCajaAsync();
        await SeedCierreAsync(soloEliminada, new DateTime(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc), aperturaEliminada: true);

        var r = await _service.ObtenerUltimosCierresPorCajaAsync();

        Assert.Equal(new DateTime(2026, 10, 1, 20, 0, 0), r[caja.Id]);
        Assert.False(r.ContainsKey(soloEliminada.Id));
    }

    [Fact]
    public async Task CajaSinCierres_NoApareceEnElResultado()
    {
        var conCierre = await SeedCajaAsync();
        var sinCierre = await SeedCajaAsync();
        await SeedCierreAsync(conCierre, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));

        var r = await _service.ObtenerUltimosCierresPorCajaAsync();

        Assert.True(r.ContainsKey(conCierre.Id));
        Assert.False(r.ContainsKey(sinCierre.Id));
    }

    [Fact]
    public async Task FiltroDeVisibilidad_NoExponeCajasAjenas()
    {
        var propia = await SeedCajaAsync();
        var ajena = await SeedCajaAsync();
        await SeedCierreAsync(propia, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc));
        await SeedCierreAsync(ajena, new DateTime(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc));

        var r = await _service.ObtenerUltimosCierresPorCajaAsync(new[] { propia.Id });
        var vacio = await _service.ObtenerUltimosCierresPorCajaAsync(Array.Empty<int>());

        Assert.Equal(new[] { propia.Id }, r.Keys.ToArray());
        Assert.Empty(vacio);
    }

    [Fact]
    public async Task ConsultaAgrupada_UnSoloComandoParaVariasCajas()
    {
        for (var i = 0; i < 5; i++)
        {
            var c = await SeedCajaAsync();
            await SeedCierreAsync(c, new DateTime(2026, 10, 1 + i, 20, 0, 0, DateTimeKind.Utc));
            await SeedCierreAsync(c, new DateTime(2026, 10, 10 + i, 20, 0, 0, DateTimeKind.Utc));
        }

        _conteo.Lecturas = 0;
        var r = await _service.ObtenerUltimosCierresPorCajaAsync();

        Assert.Equal(5, r.Count);
        Assert.Equal(1, _conteo.Lecturas);
    }

    [Fact]
    public void AHoraComercial_ConvierteUtcSinDependerDeLaZonaDelServidor_YRespetaKindUnspecified()
    {
        var zona = TimeZoneInfo.CreateCustomTimeZone("ART-test", TimeSpan.FromHours(-3), "ART", "ART");
        // SQL Server devuelve Kind=Unspecified; se interpreta como UTC.
        var desdeSql = new DateTime(2026, 10, 3, 1, 30, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 10, 3, 1, 30, 0, DateTimeKind.Utc);
        var local = new DateTime(2026, 10, 3, 1, 30, 0, DateTimeKind.Local);

        var esperado = new DateTime(2026, 10, 2, 22, 30, 0);
        Assert.Equal(esperado, CajaController.AHoraComercial(desdeSql, zona));
        Assert.Equal(esperado, CajaController.AHoraComercial(utc, zona));
        // Un Kind=Local se reetiqueta como UTC: el resultado no depende de la zona del proceso.
        Assert.Equal(esperado, CajaController.AHoraComercial(local, zona));
    }
}

/// <summary>Render real de Caja/Index con la app en memoria (SQLite) y usuario SuperAdmin.</summary>
[Collection("HttpIntegration")]
public class CajaIndexUltimoCierreRenderTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CajaIndexUltimoCierreRenderTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Index_MuestraUltimoCierreEnHoraComercial_YGuionSinCierres()
    {
        await _factory.SeedTestUserAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var conCierre = "CC-" + sufijo;
        var sinCierre = "SC-" + sufijo;
        var zona = _factory.Services.GetRequiredService<IRelojComercial>().ZonaComercial;
        var cierreUtc = new DateTime(2026, 3, 15, 2, 45, 0, DateTimeKind.Utc);
        var esperado = TimeZoneInfo.ConvertTimeFromUtc(cierreUtc, zona);

        using (var scope = _factory.Services.CreateScope())
        {
            var ctxFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await ctxFactory.CreateDbContextAsync();
            var a = new Caja { Codigo = conCierre, Nombre = "Caja " + conCierre, Activa = true, Estado = EstadoCaja.Cerrada };
            var b = new Caja { Codigo = sinCierre, Nombre = "Caja " + sinCierre, Activa = true, Estado = EstadoCaja.Cerrada };
            ctx.Cajas.AddRange(a, b);
            await ctx.SaveChangesAsync();
            var ap = new AperturaCaja { CajaId = a.Id, FechaApertura = cierreUtc.AddHours(-8), MontoInicial = 0, UsuarioApertura = "t", Cerrada = true };
            ctx.AperturasCaja.Add(ap);
            await ctx.SaveChangesAsync();
            ctx.CierresCaja.Add(new CierreCaja { AperturaCajaId = ap.Id, FechaCierre = cierreUtc, UsuarioCierre = "t" });
            await ctx.SaveChangesAsync();
        }

        var response = await _factory.CreateAuthenticatedClient().GetAsync("/Caja");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        static string Fila(string html, string codigo) => Regex.Match(html,
            "<tr data-caja-row-id=\"\\d+\"[^>]*>(?:(?!</tr>).)*?" + Regex.Escape(codigo) + "(?:(?!</tr>).)*</tr>",
            RegexOptions.Singleline).Value;

        var filaCon = WebUtility.HtmlDecode(Fila(html, conCierre));
        Assert.False(string.IsNullOrEmpty(filaCon), "No se renderizo la fila de la caja con cierre.");
        Assert.Contains(esperado.ToString("dd/MM · HH:mm"), filaCon);
        Assert.DoesNotContain("Turno en curso", filaCon);

        var filaSin = Fila(html, sinCierre);
        Assert.False(string.IsNullOrEmpty(filaSin), "No se renderizo la fila de la caja sin cierres.");
        Assert.DoesNotContain("Turno en curso", filaSin);
        Assert.DoesNotContain("data-ultimo-cierre", filaSin);
    }
}
