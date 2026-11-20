using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// P-02 / P6: activación productiva de CriterioFiscal V2 desde la fecha real de puesta en marcha (<c>Fiscal:CriterioV2Desde</c>).
/// Sin la clave nada cambia (V1); antes del instante V1 y desde él V2; un valor inválido impide arrancar; el cableado Docker, el
/// <c>.env.example</c> y el preflight la contemplan.
/// </summary>
public class ActivacionCriterioFiscalTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinClave_NoActivaNada(string? valor) => Assert.Null(ActivacionCriterioFiscal.Resolver(valor));

    [Theory]
    [InlineData("2026-12-01T03:00:00Z", "2026-12-01T03:00:00Z")]
    [InlineData("2026-12-01T00:00:00-03:00", "2026-12-01T03:00:00Z")]      // 00:00 de Argentina
    [InlineData(" 2026-12-01T03:00:00.000Z ", "2026-12-01T03:00:00Z")]
    [InlineData("2027-01-15T12:30+00:00", "2027-01-15T12:30:00Z")]
    public void InstanteConZona_SeNormalizaAUtc(string valor, string esperadoUtc)
        => Assert.Equal(DateTimeOffset.Parse(esperadoUtc), ActivacionCriterioFiscal.Resolver(valor));

    [Theory]
    [InlineData("2026-12-01")]                    // sin hora ni zona
    [InlineData("2026-12-01T03:00:00")]           // sin zona: ambiguo entre UTC y hora local del servidor
    [InlineData("mañana")]
    [InlineData("2026-13-45T03:00:00Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("2020-01-01T00:00:00Z")]          // anterior al proyecto: casi seguro un error de carga
    [InlineData("2025-12-01T03:00:00Z")]
    [InlineData("2026-12-01T03:00:00+15:00")]     // offset fuera de rango (máx. ±14 h)
    [InlineData("2026-02-31T03:00:00Z")]          // día inexistente
    [InlineData("2026-12-01T03:00:00z")]          // la zona se escribe con Z mayúscula (igual que el preflight)
    public void ValorInvalido_FallaCerrado(string valor)
        => Assert.Throws<InvalidOperationException>(() => ActivacionCriterioFiscal.Resolver(valor));

    [Fact]
    public void VersionPorFecha_V1SinConfiguracion_YAntesDelInstante_V2DesdeElInstante()
    {
        var desde = DateTimeOffset.Parse("2026-12-01T03:00:00Z");

        Assert.Equal(CriterioFiscalRegistry.VersionInicial, CriterioFiscalRegistry.VersionPorFecha(null, DateTime.UtcNow));
        Assert.Equal(CriterioFiscalRegistry.VersionInicial, CriterioFiscalRegistry.VersionPorFecha(desde, new DateTime(2026, 12, 1, 2, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(CriterioFiscalRegistry.VersionDosRedondeoComercial, CriterioFiscalRegistry.VersionPorFecha(desde, new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(CriterioFiscalRegistry.VersionDosRedondeoComercial, CriterioFiscalRegistry.VersionPorFecha(desde, new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SinConfigurar_LaVersionVigenteSigueSiendoV1()
    {
        // El proceso de pruebas no configura la clave: producción sin la clave no activa V2.
        Assert.Null(CriterioFiscalRegistry.V2Desde);
        Assert.Equal(CriterioFiscalRegistry.VersionInicial, CriterioFiscalRegistry.VersionVigente);
    }

    [Fact]
    public void CableadoDocker_PasaLaClaveALaApp_ConVacioPorDefecto()
    {
        var raiz = FindRepoRoot();
        var compose = File.ReadAllText(Path.Combine(raiz, "docker-compose.yml"));
        Assert.Contains("Fiscal__CriterioV2Desde: ${FISCAL_CRITERIO_V2_DESDE:-}", compose, StringComparison.Ordinal);

        var env = File.ReadAllText(Path.Combine(raiz, ".env.example"));
        Assert.Matches(@"(?m)^FISCAL_CRITERIO_V2_DESDE=\r?$", env);

        var preflight = File.ReadAllText(Path.Combine(raiz, "scripts", "deploy", "preflight.sh"));
        Assert.Contains("FISCAL_CRITERIO_V2_DESDE", preflight, StringComparison.Ordinal);
        Assert.Contains("instante ISO-8601 con zona", preflight, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
