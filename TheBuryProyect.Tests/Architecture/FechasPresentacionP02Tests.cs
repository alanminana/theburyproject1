using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// P-02 / C15: las marcas de tiempo se guardan en UTC y se presentan en la hora comercial argentina, sin depender de la zona del
/// servidor (el contenedor corre en UTC; <c>ToLocalTime()</c> mostraba 03:43 para una cotización hecha a las 00:43).
/// </summary>
public class FechasPresentacionP02Tests
{
    public static IEnumerable<object[]> Casos => new[]
    {
        // utc                         → comercial esperado (UTC−3, sin horario de verano)
        new object[] { "2026-11-20T03:43:00", "2026-11-20T00:43:00" }, // el caso reportado: 00:43 ART = 03:43 UTC
        new object[] { "2026-11-20T03:00:00", "2026-11-20T00:00:00" }, // medianoche exacta argentina
        new object[] { "2026-11-20T02:59:59", "2026-11-19T23:59:59" }, // un segundo antes: día anterior
        new object[] { "2026-11-01T02:59:00", "2026-10-31T23:59:00" }, // cambio de mes hacia atrás
        new object[] { "2026-11-01T03:00:00", "2026-11-01T00:00:00" }, // primer instante de noviembre
        new object[] { "2027-01-01T02:59:00", "2026-12-31T23:59:00" }, // cambio de año
        new object[] { "2027-01-01T03:00:00", "2027-01-01T00:00:00" }, // primer instante del año
        new object[] { "2028-03-01T02:30:00", "2028-02-29T23:30:00" }, // bisiesto
        new object[] { "2026-07-15T12:00:00", "2026-07-15T09:00:00" }, // invierno: la zona no tiene DST
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void AHoraComercial_ConvierteUtcAArgentina(string utc, string esperado)
    {
        var entrada = DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Unspecified); // como lo devuelve EF (datetime2)

        Assert.Equal(DateTime.Parse(esperado), entrada.AHoraComercial());
    }

    [Fact]
    public void AHoraComercial_NoDependeDeLaZonaDelProceso()
    {
        // Con la zona del proceso en UTC o Argentina el resultado es el mismo: se usa la zona comercial explícita.
        var utc = new DateTime(2026, 11, 20, 3, 43, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 20, 0, 43, 0), utc.AHoraComercial());
        Assert.Equal(new DateTime(2026, 11, 20, 0, 43, 0), DateTime.SpecifyKind(utc, DateTimeKind.Unspecified).AHoraComercial());
    }

    [Fact]
    public void Presentacion_NoUsaToLocalTime()
    {
        var raiz = FindRepoRoot();
        var infractores = new List<string>();
        foreach (var carpeta in new[] { "Views", "Services", "Controllers", "Helpers", "ViewModels" })
        {
            var ruta = Path.Combine(raiz, carpeta);
            if (!Directory.Exists(ruta)) continue;
            foreach (var archivo in Directory.EnumerateFiles(ruta, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml")))
            {
                if (archivo.EndsWith("FechaComercialExtensions.cs")) continue; // lo menciona en su documentación
                var texto = File.ReadAllText(archivo);
                if (texto.Contains("ToLocalTime()"))
                    infractores.Add(Path.GetRelativePath(raiz, archivo));
            }
        }

        Assert.True(infractores.Count == 0,
            "ToLocalTime() depende de la zona del servidor; usar AHoraComercial() (instantes UTC) o mostrar la fecha comercial sin convertir. Archivos: "
            + string.Join(", ", infractores));
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
