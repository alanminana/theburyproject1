using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>D-05: las vistas no deben volver a usar handlers inline (onclick=, onsubmit=, javascript: ...); usar data-action (wwwroot/js/csp-actions.js).</summary>
public class CspHandlersInlineContractTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz del repo no encontrada");
    }

    private static readonly Regex Inline = new(@"[\s""']on(click|change|submit|input|keydown|keyup|mouseover|mouseout|load|error|focus|blur)\s*=|href\s*=\s*""javascript:", RegexOptions.IgnoreCase);

    [Fact]
    public void Vistas_NoUsanHandlersInline()
    {
        var root = Root();
        var ofensores = new List<string>();
        foreach (var carpeta in new[] { "Views", "Areas" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root, carpeta), "*.cshtml", SearchOption.AllDirectories))
            {
                var lineas = File.ReadAllLines(f);
                for (var i = 0; i < lineas.Length; i++)
                    if (Inline.IsMatch(lineas[i])) ofensores.Add($"{Path.GetRelativePath(root, f)}:{i + 1}");
            }
        Assert.True(ofensores.Count == 0, "Handlers inline (usar data-action): " + string.Join(", ", ofensores.Take(10)));
    }

    [Fact]
    public void ScriptsPropios_NoGeneranHandlersInlineEnHtml()
    {
        var root = Root();
        var js = Directory.EnumerateFiles(Path.Combine(root, "wwwroot", "js"), "*.js")
            .Where(f => !f.EndsWith(".min.js")).Where(f => Regex.IsMatch(File.ReadAllText(f), @"\son(click|change|submit|input)=""")).ToList();
        Assert.True(js.Count == 0, "JS que arma HTML con handlers inline: " + string.Join(", ", js.Select(Path.GetFileName)));
    }

    [Fact]
    public void Layout_IncluyeCspActions()
    {
        Assert.Contains("csp-actions.js", File.ReadAllText(Path.Combine(Root(), "Views", "Shared", "_Layout.cshtml")));
    }

    [Fact]
    public void FuncionesInvocadasPorDataAttributes_EstanEnLaListaPermitidaDeCspActions()
    {
        var root = Root();
        var js = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "csp-actions.js"));
        var lista = Regex.Match(js, @"PERMITIDAS = \[(.*?)\]", RegexOptions.Singleline).Groups[1].Value;
        var permitidas = Regex.Matches(lista, "'([A-Za-z0-9_]+)'").Select(m => m.Groups[1].Value).ToHashSet();
        var usadas = new HashSet<string>();
        var attr = new Regex(@"data-(?:fn|input-fn|change-fn|key-fn|submit-fn)=""([A-Za-z0-9_]+)""");
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.cshtml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(root, "Areas"), "*.cshtml", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(Path.Combine(root, "wwwroot", "js"), "*.js").Where(x => !x.EndsWith("csp-actions.js"))))
            foreach (Match m in attr.Matches(File.ReadAllText(f))) usadas.Add(m.Groups[1].Value);
        Assert.Empty(usadas.Except(permitidas));
    }

    /// <summary>DEF-UNI-01: el contenedor interior (min-h-full) cubre todo el fondo; si no cierra por sí mismo, clic fuera del modal «Gestionar» no lo cierra.</summary>
    [Fact]
    public void Unidades_ModalGestionar_CierraConClicEnElFondoInterior()
    {
        var html = File.ReadAllText(Path.Combine(Root(), "Views", "Producto", "Unidades.cshtml"));
        var inicio = html.IndexOf("id=\"modal-manage\"", StringComparison.Ordinal);
        Assert.True(inicio > 0);
        var tarjeta = html.IndexOf("pu-modal-card", inicio, StringComparison.Ordinal);
        var cabecera = html.Substring(inicio, tarjeta - inicio);
        // overlay (fixed inset-0) y contenedor interior (min-h-full): ambos cierran solo si el destino del clic es el propio elemento.
        Assert.Equal(2, Regex.Matches(cabecera, @"data-action=""call-self""\s+data-fn=""puCloseModal""\s+data-args='\[""modal-manage""\]'").Count);
        Assert.Contains("min-h-full", cabecera);
    }
}
