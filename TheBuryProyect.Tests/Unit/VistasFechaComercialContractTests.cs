using System.Text.RegularExpressions;
using Xunit;

namespace TheBuryProyect.Tests.Unit;

/// <summary>
/// F5 — contrato anti-regresión: ninguna vista calcula «hoy» con el reloj del servidor (UTC en el contenedor).
/// Se usa <c>Reloj</c> (IRelojComercial, inyectado en Views/_ViewImports.cshtml): <c>Reloj.HoyIso()</c>,
/// <c>Reloj.HoyComercial()</c>, <c>Reloj.AhoraComercial</c>, <c>Reloj.UtcAComercial(...)</c>.
/// Excepciones: solo donde la vista debe coincidir con una regla de SERVIDOR que todavía usa el reloj del proceso o UTC;
/// al corregir el servidor se debe corregir también la vista y quitar la excepción.
/// </summary>
public class VistasFechaComercialContractTests
{
    private static readonly Regex RelojServidor = new(@"DateTime(Offset)?\.(Today|Now|UtcNow)\b", RegexOptions.Compiled);

    // archivo (relativo a la raíz) -> motivo
    private static readonly Dictionary<string, string> Excepciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Views/Devolucion/Index.cshtml"] =
            "Badge de vigencia de garantía: debe coincidir con DevolucionService (filtro/contadores por día UTC).",
        ["Views/Ticket/Index_tw.cshtml"] =
            "Enlace 'recientes (7 días)': el servidor filtra tickets por fecha del proceso y las métricas con UtcNow-7d.",
        ["Views/Usuarios/_BloquearUsuarioModal_tw.cshtml"] =
            "min del bloqueo: UsuariosController.Bloquear interpreta el datetime-local como hora local del servidor.",
        ["Views/Venta/Index_tw.cshtml"] =
            "Mes en curso de la pestaña Logística: debe coincidir con VentaController.Index (DateTime.Today del servidor).",
        // Comparan un instante (DateTimeOffset.UtcNow) contra LockoutEnd, que es un instante UTC: correcto.
        ["Views/Shared/_SeguridadUsuariosTab.cshtml"] = "Compara instantes UTC (LockoutEnd vs UtcNow).",
        ["Views/Usuarios/Details_tw.cshtml"] = "Compara instantes UTC (LockoutEnd vs UtcNow).",
    };

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Vistas_NoUsanElRelojDelServidorParaCalcularHoy()
    {
        var raiz = RaizRepo();
        var infractores = new List<string>();

        foreach (var archivo in Directory.EnumerateFiles(Path.Combine(raiz, "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(raiz, archivo).Replace(Path.DirectorySeparatorChar, '/');
            if (Excepciones.ContainsKey(relativo)) continue;

            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
                if (RelojServidor.IsMatch(lineas[i]))
                    infractores.Add($"{relativo}:{i + 1}  {lineas[i].Trim()}");
        }

        Assert.True(infractores.Count == 0,
            "Las vistas deben usar el día comercial (Reloj de IRelojComercial), no DateTime.Today/Now/UtcNow:\n" +
            string.Join("\n", infractores));
    }

    [Fact]
    public void Excepciones_SiguenExistiendoYUsanElRelojDelServidor()
    {
        // Evita excepciones muertas: si la vista se corrigió, hay que quitarla de la lista.
        var raiz = RaizRepo();
        foreach (var (relativo, motivo) in Excepciones)
        {
            var ruta = Path.Combine(raiz, relativo.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(ruta), $"Excepción obsoleta (archivo inexistente): {relativo}");
            Assert.True(RelojServidor.IsMatch(File.ReadAllText(ruta)),
                $"Excepción obsoleta (ya no usa el reloj del servidor): {relativo} — {motivo}");
        }
    }

    [Fact]
    public void ViewImports_InyectaElRelojComercial()
    {
        var texto = File.ReadAllText(Path.Combine(RaizRepo(), "Views", "_ViewImports.cshtml"));
        Assert.Contains("@inject TheBuryProject.Services.IRelojComercial Reloj", texto);
    }
}
