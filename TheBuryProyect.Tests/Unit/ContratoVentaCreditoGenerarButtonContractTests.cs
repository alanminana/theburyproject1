namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Protección de regresión mínima para el doble POST de "Generar contrato de venta"
/// (Views/Credito/ConfigurarVenta_tw.cshtml, standalone) — la causa real era un
/// &lt;button type="submit" formaction=... formtarget="_blank"&gt; sin ningún guard.
/// No es un E2E (el setup real de una venta de Crédito personal es costoso): protege el
/// contrato mínimo que garantiza "un click = un request" — botón no-submit, un único
/// listener, y el bloqueo (disabled=true) como primera instrucción del handler, antes de
/// cualquier `await` — sin ese orden, un segundo click síncrono antes del primer `await`
/// podría colarse igual.
/// </summary>
public class ContratoVentaCreditoGenerarButtonContractTests
{
    private const string ViewRelativePath = "Views/Credito/ConfigurarVenta_tw.cshtml";
    private const string ScriptRelativePath = "wwwroot/js/configurar-venta-credito.js";

    [Fact]
    public void ConfigurarVentaView_BotonGenerarContrato_NoEsSubmitNiTieneFormaction()
    {
        var view = ReadView();
        var boton = ExtraerBoton(view);

        Assert.Contains("type=\"button\"", boton);
        Assert.DoesNotContain("type=\"submit\"", boton);
        Assert.DoesNotContain("formaction", boton);
        Assert.DoesNotContain("formtarget", boton);
        Assert.DoesNotContain("formnovalidate", boton);
    }

    [Fact]
    public void ConfigurarVentaJs_BotonGenerarContrato_TieneExactamenteUnListenerDeClick()
    {
        var script = ReadScript();

        var ocurrencias = CountOccurrences(script, "btnGenerarContrato.addEventListener(");
        Assert.Equal(1, ocurrencias);
    }

    [Fact]
    public void ConfigurarVentaJs_HandlerGenerarContrato_DeshabilitaElBotonAntesDeCualquierAwait()
    {
        var script = ReadScript();
        var handler = ExtraerHandler(script);

        var indiceDisabledTrue = handler.IndexOf("btnGenerarContrato.disabled = true;", StringComparison.Ordinal);
        Assert.True(indiceDisabledTrue >= 0, "El handler no deshabilita el botón.");

        var indicePrimerAwait = handler.IndexOf("await ", StringComparison.Ordinal);
        Assert.True(indicePrimerAwait >= 0, "El handler no es async / no tiene ningún await.");

        Assert.True(
            indiceDisabledTrue < indicePrimerAwait,
            "btnGenerarContrato.disabled = true debe ejecutarse antes del primer await del handler " +
            "(reentrancy guard síncrono) — un segundo click antes de esa línea no quedaría bloqueado.");

        // Guard de reentrancia real: la primera línea del handler debe salir si ya está
        // deshabilitado (protege incluso contra el disparo del mismo evento dos veces).
        var indiceGuardTemprano = handler.IndexOf("if (btnGenerarContrato.disabled) return;", StringComparison.Ordinal);
        Assert.True(indiceGuardTemprano >= 0 && indiceGuardTemprano < indiceDisabledTrue);
    }

    [Fact]
    public void ConfigurarVentaJs_HandlerGenerarContrato_ReactivaElBotonSoloEnElCatchDeError()
    {
        var script = ReadScript();
        var handler = ExtraerHandler(script);

        // Si se reactivara también fuera del catch (por ejemplo, en un finally), un click
        // rápido durante el fetch en curso podría volver a habilitarse antes de tiempo.
        var reactivaciones = CountOccurrences(handler, "btnGenerarContrato.disabled = false;");
        Assert.Equal(1, reactivaciones);

        var indiceCatch = handler.IndexOf("} catch (error) {", StringComparison.Ordinal);
        var indiceReactivacion = handler.IndexOf("btnGenerarContrato.disabled = false;", StringComparison.Ordinal);
        Assert.True(indiceCatch >= 0 && indiceReactivacion > indiceCatch);
    }

    private static string ExtraerBoton(string view)
    {
        var inicio = view.IndexOf("id=\"btn-generar-contrato\"", StringComparison.Ordinal);
        Assert.True(inicio > 0, "No se encontró el botón btn-generar-contrato.");

        var inicioTag = view.LastIndexOf('<', inicio);
        var finTag = view.IndexOf('>', inicio);
        Assert.True(inicioTag >= 0 && finTag > inicioTag);

        return view.Substring(inicioTag, finTag - inicioTag + 1);
    }

    // Extrae el cuerpo del handler contando llaves desde el primer '{' después de
    // "async function ()" hasta que cierra (nivel 0) — no depende de comentarios ni
    // indentación exacta más allá del listener y la firma del handler.
    private static string ExtraerHandler(string script)
    {
        var inicioListener = script.IndexOf("btnGenerarContrato.addEventListener(", StringComparison.Ordinal);
        Assert.True(inicioListener > 0, "No se encontró el addEventListener de btnGenerarContrato.");

        var inicioFuncion = script.IndexOf("async function ()", inicioListener, StringComparison.Ordinal);
        Assert.True(inicioFuncion > inicioListener, "No se encontró el handler async del listener.");

        var inicioLlave = script.IndexOf('{', inicioFuncion);
        Assert.True(inicioLlave > inicioFuncion, "No se encontró la llave de apertura del handler.");

        var nivel = 0;
        var indiceCierre = -1;
        for (var i = inicioLlave; i < script.Length; i++)
        {
            if (script[i] == '{') nivel++;
            else if (script[i] == '}')
            {
                nivel--;
                if (nivel == 0)
                {
                    indiceCierre = i;
                    break;
                }
            }
        }

        Assert.True(indiceCierre > inicioLlave, "No se encontró el cierre del handler.");

        return script.Substring(inicioFuncion, indiceCierre - inicioFuncion + 1);
    }

    private static string ReadView() => File.ReadAllText(Path.Combine(FindRepoRoot(), ViewRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadScript() => File.ReadAllText(Path.Combine(FindRepoRoot(), ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
