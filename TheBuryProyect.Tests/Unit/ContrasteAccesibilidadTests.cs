using System.Globalization;
using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Regresión de accesibilidad (WCAG 2.1 AA, contraste de texto 4,5:1): axe-core detectó en Venta/Details el botón «Autorizar»
/// (texto blanco sobre el verde de <c>.btn-erp-success</c>, 2,46:1) y las etiquetas <c>text-slate-500</c> (3,5:1). Estas pruebas leen los
/// tokens reales de <c>palette.css</c> y las reglas de <c>shared-components.css</c> y calculan el contraste, de modo que una
/// vuelta atrás de la paleta o un botón nuevo con texto claro sobre un fondo claro fallen aquí y no en producción.
/// No sustituyen la revisión con lector de pantalla ni el análisis en navegador: miden solo pares de colores declarados.
/// </summary>
public class ContrasteAccesibilidadTests
{
    private const double MinimoTexto = 4.5;

    private static readonly Lazy<Dictionary<string, string>> Tokens = new(() => LeerTokens(Leer("wwwroot", "css", "palette.css")));

    private static string Leer(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray()));
    }

    private static string SinComentarios(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>Primera definición de cada token <c>--nombre: valor;</c> (la paleta activa; las alternativas están comentadas).</summary>
    private static Dictionary<string, string> LeerTokens(string css)
    {
        var tokens = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(SinComentarios(css), @"(--[\w-]+)\s*:\s*([^;]+);"))
            tokens.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim());
        return tokens;
    }

    /// <summary>Resuelve un valor: #hex, var(--token) y color-mix(in srgb, X N%, black). Null si no es un color opaco resoluble.</summary>
    private static (double R, double G, double B)? Resolver(string valor, int profundidad = 0)
    {
        valor = valor.Trim();
        if (profundidad > 8)
            return null;

        var hex = Regex.Match(valor, @"^#([0-9a-fA-F]{6})$");
        if (hex.Success)
        {
            var n = int.Parse(hex.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return ((n >> 16) & 255, (n >> 8) & 255, n & 255);
        }

        var v = Regex.Match(valor, @"^var\((--[\w-]+)(?:\s*,\s*(.+))?\)$");
        if (v.Success)
        {
            if (Tokens.Value.TryGetValue(v.Groups[1].Value, out var definido))
                return Resolver(definido, profundidad + 1);
            return v.Groups[2].Success ? Resolver(v.Groups[2].Value, profundidad + 1) : null;
        }

        var mezcla = Regex.Match(valor, @"^color-mix\(in srgb,\s*(.+?)\s+(\d+(?:\.\d+)?)%,\s*black\)$");
        if (mezcla.Success && Resolver(mezcla.Groups[1].Value, profundidad + 1) is { } baseColor)
        {
            var p = double.Parse(mezcla.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0;
            return (baseColor.R * p, baseColor.G * p, baseColor.B * p);
        }

        return null;
    }

    private static double Luminancia((double R, double G, double B) c)
    {
        static double Canal(double v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
    }

    private static double Contraste(string primerPlano, string fondo)
    {
        var a = Resolver(primerPlano) ?? throw new InvalidOperationException($"No se pudo resolver el color {primerPlano}.");
        var b = Resolver(fondo) ?? throw new InvalidOperationException($"No se pudo resolver el color {fondo}.");
        var (claro, oscuro) = Luminancia(a) >= Luminancia(b) ? (Luminancia(a), Luminancia(b)) : (Luminancia(b), Luminancia(a));
        return (claro + 0.05) / (oscuro + 0.05);
    }

    [Fact]
    public void ElResolverDeColoresCalculaElContrasteConocido()
    {
        // Referencias: blanco sobre negro = 21:1; el defecto original (blanco sobre el verde success-600) era 2,46:1.
        Assert.Equal(21.0, Contraste("#ffffff", "#000000"), 1);
        Assert.Equal(2.46, Contraste("#ffffff", "var(--pal-success-600)"), 1);
    }

    [Fact]
    public void ElBotonDeExito_UsaTextoOscuroConContrasteSuficienteEnReposoYHover()
    {
        var css = SinComentarios(Leer("wwwroot", "css", "shared-components.css"));
        var regla = Regex.Match(css, @"\.btn-erp-success\s*\{([^}]*)\}").Groups[1].Value;
        Assert.Contains("color: var(--pal-on-accent)", regla);
        Assert.DoesNotContain("color: var(--pal-white)", regla);

        Assert.True(Contraste("var(--pal-on-accent)", "var(--pal-success-600)") >= MinimoTexto, "reposo");
        Assert.True(Contraste("var(--pal-on-accent)", "var(--pal-success-700)") >= MinimoTexto, "hover");
    }

    [Theory]
    [InlineData("--pal-neutral-950")]
    [InlineData("--pal-neutral-925")]
    [InlineData("--pal-neutral-900")]
    [InlineData("--pal-neutral-875")]
    [InlineData("--pal-neutral-850")]
    [InlineData("--pal-neutral-800")]
    public void ElTextoAtenuadoSlate500_CumpleElContrasteSobreLosFondosOscurosDelSistema(string fondo)
    {
        // text-slate-500 (etiquetas «Forma de pago», «Vendedor», chips) se resuelve en --pal-neutral-500.
        Assert.Equal("var(--pal-neutral-500)", Tokens.Value["--color-slate-500"]);
        Assert.True(Contraste("var(--pal-neutral-500)", $"var({fondo})") >= MinimoTexto, $"slate-500 sobre {fondo}");
    }

    [Theory]
    [InlineData("--pal-neutral-400")]
    [InlineData("--pal-neutral-450")]
    [InlineData("--pal-neutral-350")]
    [InlineData("--pal-neutral-300")]
    public void LosDemasTextosSecundarios_CumplenElContrasteSobreElFondoDeLaPagina(string texto)
    {
        Assert.True(Contraste($"var({texto})", "var(--pal-neutral-925)") >= MinimoTexto, texto);
    }

    [Fact]
    public void TodoBotonYBadgeConFondoOpacoResoluble_TieneContrasteDeTextoSuficiente()
    {
        var css = SinComentarios(Leer("wwwroot", "css", "shared-components.css"));
        var revisados = new List<string>();
        foreach (Match m in Regex.Matches(css, @"(\.(?:btn|badge)-erp-[a-z]+)\s*\{([^}]*)\}"))
        {
            var cuerpo = m.Groups[2].Value;
            var fondo = Regex.Match(cuerpo, @"background-color:\s*([^;]+);");
            var texto = Regex.Match(cuerpo, @"(?<![-\w])color:\s*([^;]+);");
            if (!fondo.Success || !texto.Success || Resolver(fondo.Groups[1].Value) is null || Resolver(texto.Groups[1].Value) is null)
                continue; // fondos translúcidos o transparentes: dependen del fondo de la página y se miden con axe-core en navegador

            revisados.Add(m.Groups[1].Value);
            Assert.True(Contraste(texto.Groups[1].Value, fondo.Groups[1].Value) >= MinimoTexto,
                $"{m.Groups[1].Value}: {texto.Groups[1].Value} sobre {fondo.Groups[1].Value} no alcanza {MinimoTexto}:1.");
        }

        // Se mide al menos el botón primario, el secundario y el de éxito (si el patrón dejara de resolver, la prueba no pasa en vacío).
        Assert.Contains(".btn-erp-primary", revisados);
        Assert.Contains(".btn-erp-secondary", revisados);
        Assert.Contains(".btn-erp-success", revisados);
    }
}
