using System.Reflection;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Unit;

public class CondicionDocumentoTests
{
    private static DocumentoContexto Ctx(params (string, object?)[] valores)
    {
        var c = new DocumentoContexto();
        foreach (var (k, v) in valores) c.Set(k, v);
        return c;
    }

    private static bool Evaluar(string json, DocumentoContexto ctx)
    {
        var parse = CondicionDocumentoParser.Parsear(json);
        Assert.True(parse.EsValida, string.Join("; ", parse.Errores));
        return CondicionDocumentoEvaluador.Evaluar(parse.Condicion, ctx);
    }

    [Fact]
    public void SinCondicion_SiempreAplica()
    {
        var parse = CondicionDocumentoParser.Parsear(null);
        Assert.True(parse.EsValida);
        Assert.True(CondicionDocumentoEvaluador.Evaluar(parse.Condicion, Ctx()));
    }

    [Theory]
    [InlineData("igual", "CreditoPersonal", true)]
    [InlineData("igual", "creditopersonal", true)]
    [InlineData("distinto", "Efectivo", true)]
    [InlineData("contiene", "Personal", true)]
    [InlineData("contiene", "Tarjeta", false)]
    public void OperadoresDeTexto(string operador, string valor, bool esperado)
    {
        var json = $$"""{"campo":"venta.tipoPago","operador":"{{operador}}","valor":"{{valor}}"}""";
        // contiene no valida contra la lista de valores permitidos del enum de texto: se prueba con el campo libre
        if (operador == "contiene")
            json = $$"""{"campo":"cliente.nombreCompleto","operador":"contiene","valor":"{{valor}}"}""";

        var ctx = Ctx(("venta.tipoPago", "CreditoPersonal"), ("cliente.nombreCompleto", "Perez CreditoPersonal Juan"));
        Assert.Equal(esperado, Evaluar(json, ctx));
    }

    [Theory]
    [InlineData("mayor", "0", true)]
    [InlineData("mayor", "300", false)]
    [InlineData("mayorIgual", "300", true)]
    [InlineData("menor", "301", true)]
    [InlineData("menorIgual", "299", false)]
    [InlineData("igual", "300", true)]
    [InlineData("distinto", "300", false)]
    public void OperadoresNumericos(string operador, string valor, bool esperado)
    {
        var json = $$"""{"campo":"pago.importe","operador":"{{operador}}","valor":{{valor}}}""";
        Assert.Equal(esperado, Evaluar(json, Ctx(("pago.importe", 300m))));
    }

    [Fact]
    public void EnYNoEn_AceptanListas()
    {
        var ctx = Ctx(("venta.tipoPago", "Efectivo"));
        Assert.True(Evaluar("""{"campo":"venta.tipoPago","operador":"en","valor":["Efectivo","Transferencia"]}""", ctx));
        Assert.False(Evaluar("""{"campo":"venta.tipoPago","operador":"noEn","valor":"Efectivo,Transferencia"}""", ctx));
    }

    [Fact]
    public void ExisteVerdaderoYFalso()
    {
        var ctx = Ctx(("fiador.dni", "123"), ("credito.requiereFiador", true), ("fiador.relacion", null));
        Assert.True(Evaluar("""{"campo":"fiador.dni","operador":"existe"}""", ctx));
        Assert.False(Evaluar("""{"campo":"fiador.relacion","operador":"existe"}""", ctx));
        Assert.True(Evaluar("""{"campo":"credito.requiereFiador","operador":"verdadero"}""", ctx));
        Assert.False(Evaluar("""{"campo":"credito.requiereFiador","operador":"falso"}""", ctx));
    }

    [Fact]
    public void Fechas_SeComparanPorDia()
    {
        var ctx = Ctx(("venta.fecha", new DateTime(2026, 5, 10, 14, 30, 0)));
        Assert.True(Evaluar("""{"campo":"venta.fecha","operador":"igual","valor":"2026-05-10"}""", ctx));
        Assert.True(Evaluar("""{"campo":"venta.fecha","operador":"mayor","valor":"2026-05-09"}""", ctx));
    }

    [Fact]
    public void GruposAndOr_Anidados()
    {
        const string json = """
        {"op":"todas","condiciones":[
          {"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"},
          {"op":"cualquiera","condiciones":[
              {"campo":"credito.cantidadCuotas","operador":"mayor","valor":12},
              {"campo":"credito.requiereFiador","operador":"verdadero"}]}]}
        """;

        Assert.True(Evaluar(json, Ctx(("venta.tipoPago", "CreditoPersonal"), ("credito.cantidadCuotas", 6), ("credito.requiereFiador", true))));
        Assert.True(Evaluar(json, Ctx(("venta.tipoPago", "CreditoPersonal"), ("credito.cantidadCuotas", 24), ("credito.requiereFiador", false))));
        Assert.False(Evaluar(json, Ctx(("venta.tipoPago", "CreditoPersonal"), ("credito.cantidadCuotas", 6), ("credito.requiereFiador", false))));
        Assert.False(Evaluar(json, Ctx(("venta.tipoPago", "Efectivo"), ("credito.cantidadCuotas", 24), ("credito.requiereFiador", true))));
    }

    [Fact]
    public void CampoSinValor_NoCumpleOperadoresPositivos()
    {
        var ctx = Ctx(("pago.importe", null));
        Assert.False(Evaluar("""{"campo":"pago.importe","operador":"mayor","valor":0}""", ctx));
        Assert.True(Evaluar("""{"campo":"pago.importe","operador":"distinto","valor":0}""", ctx));
    }

    [Theory]
    [InlineData("""{"campo":"sistema.eval","operador":"igual","valor":"x"}""", "Campo no permitido")]
    [InlineData("""{"campo":"pago.importe","operador":"contiene","valor":"x"}""", "operador")]
    [InlineData("""{"campo":"pago.importe","operador":"mayor","valor":"abc"}""", "no es un número")]
    [InlineData("""{"campo":"venta.tipoPago","operador":"igual","valor":"Trueque"}""", "no es un valor permitido")]
    [InlineData("""{"campo":"venta.fecha","operador":"igual","valor":"10/05/2026"}""", "fecha")]
    [InlineData("""{"campo":"credito.requiereFiador","operador":"igual","valor":"si"}""", "operador")]
    [InlineData("""{"op":"quizas","condiciones":[]}""", "Operador de grupo inválido")]
    [InlineData("""{"campo":"pago.importe","operador":"mayor"}""", "Falta el valor")]
    [InlineData("""[1,2]""", "objeto")]
    [InlineData("""{not json""", "no es válido")]
    public void CondicionesInvalidas_SeRechazan(string json, string fragmentoEsperado)
    {
        var parse = CondicionDocumentoParser.Parsear(json);
        Assert.False(parse.EsValida);
        Assert.Contains(parse.Errores, e => e.Contains(fragmentoEsperado, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnidamientoExcesivo_SeRechaza()
    {
        var json = """{"campo":"pago.importe","operador":"mayor","valor":0}""";
        for (var i = 0; i < CondicionDocumentoParser.ProfundidadMaxima + 1; i++)
            json = $$"""{"op":"todas","condiciones":[{{json}}]}""";

        var parse = CondicionDocumentoParser.Parsear(json);
        Assert.False(parse.EsValida);
        Assert.Contains(parse.Errores, e => e.Contains("anidarse"));
    }

    [Fact]
    public void NoHayForma_DeInyectarCodigo_ElCampoDebeEstarEnElCatalogo()
    {
        var parse = CondicionDocumentoParser.Parsear("""{"campo":"System.Diagnostics.Process.Start('calc')","operador":"igual","valor":"1"}""");
        Assert.False(parse.EsValida);
    }
}

public class PlantillaRendererTests
{
    private static DocumentoContexto CtxCuotas()
    {
        var c = new DocumentoContexto();
        c.Set("cliente.nombreCompleto", "Perez, Juan");
        c.Set("credito.total", 1500.5m);
        c.Set("venta.fecha", new DateTime(2026, 3, 5));
        c.Set("credito.requiereFiador", false);
        c.Set("fiador.nombreCompleto", null);
        c.Colecciones["cuotas"] = new List<Dictionary<string, object?>>
        {
            DocumentoContextoBuilder.ItemCuota(1, 500m, 400m, 100m, new DateTime(2026, 4, 5), "Pendiente"),
            DocumentoContextoBuilder.ItemCuota(2, 500m, 400m, 100m, new DateTime(2026, 5, 5), "Pendiente")
        };
        c.Colecciones["productos"] = new List<Dictionary<string, object?>>();
        return c;
    }

    [Fact]
    public void ReemplazaVariables_ConFormatoArgentino()
    {
        var r = PlantillaRenderer.Renderizar("{{cliente.nombreCompleto}} debe {{credito.total}} desde {{venta.fecha}}", CtxCuotas());
        Assert.Contains("Perez, Juan", r.Texto);
        Assert.Contains("1.500,50", r.Texto);
        Assert.Contains("05/03/2026", r.Texto);
    }

    [Fact]
    public void Colecciones_RepitenPorItem_SinLineasEnBlanco()
    {
        var r = PlantillaRenderer.Renderizar("Plan:\n{{#cuotas}}\nCuota {{numero}} vence {{vencimiento}}\n{{/cuotas}}\nFin", CtxCuotas());
        Assert.Equal("Plan:\nCuota 1 vence 05/04/2026\nCuota 2 vence 05/05/2026\nFin", r.Texto);
    }

    [Fact]
    public void SeccionesInvertidasYBooleanas()
    {
        var r = PlantillaRenderer.Renderizar(
            "{{#credito.requiereFiador}}CON FIADOR{{/credito.requiereFiador}}{{^credito.requiereFiador}}SIN FIADOR{{/credito.requiereFiador}}|{{^productos}}sin productos{{/productos}}",
            CtxCuotas());
        Assert.Equal("SIN FIADOR|sin productos", r.Texto);
    }

    [Fact]
    public void TokensLegados_SiguenFuncionandoPorAlias()
    {
        var r = PlantillaRenderer.Renderizar("{{COMPRADOR_NOMBRE}} / {{Comprador.NombreCompleto}} / {{PRECIO_TOTAL}}",
            CtxAliasLegado());
        Assert.Equal("Perez, Juan / Perez, Juan / $ 10,00", r.Texto.Replace(' ', ' '));
    }

    private static DocumentoContexto CtxAliasLegado()
    {
        var c = new DocumentoContexto();
        c.Set("cliente.nombreCompleto", "Perez, Juan");
        c.Set("venta.total", 10m);
        return c;
    }

    [Fact]
    public void VariablesVaciasYNoResueltas_SeInforman()
    {
        var r = PlantillaRenderer.Renderizar("A{{fiador.nombreCompleto}}B{{pago.importe}}", CtxCuotas());
        Assert.Equal("AB", r.Texto);
        Assert.Contains("fiador.nombreCompleto", r.Vacias);
        Assert.Contains("pago.importe", r.NoResueltas);
    }

    [Fact]
    public void NoEvaluaCodigoNiExpresiones_SeTrataComoTextoLiteral()
    {
        var r = PlantillaRenderer.Renderizar("<script>alert(1)</script> {{1+1}} {{ \"x\".ToUpper() }} ${7*7}", CtxCuotas());
        Assert.Equal("<script>alert(1)</script> {{1+1}} {{ \"x\".ToUpper() }} ${7*7}", r.Texto);
    }

    [Theory]
    [InlineData("Hola {{variable.inventada}}", "Variable desconocida")]
    [InlineData("{{#cuotas}}sin cerrar", "sin cerrar")]
    [InlineData("{{/cuotas}} huérfana", "sin apertura")]
    [InlineData("{{#cliente.nombreCompleto}}x{{/cliente.nombreCompleto}}", "no es una colección")]
    [InlineData("{{#cuotas}}{{campoInexistente}}{{/cuotas}}", "Variable desconocida")]
    public void Validar_DetectaErroresDeSintaxisYVariables(string plantilla, string fragmento)
    {
        Assert.Contains(PlantillaRenderer.Validar(plantilla), e => e.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validar_AceptaPlantillaCorrecta_ConAliasLegadosYColecciones()
    {
        Assert.Empty(PlantillaRenderer.Validar("{{COMPRADOR_NOMBRE}} {{#cuotas}}{{numero}}-{{importe}}{{/cuotas}} {{documento.numero}}"));
    }
}

public class NumeroALetrasTests
{
    [Theory]
    [InlineData(0, "Cero Pesos")]
    [InlineData(1, "Un Peso")]
    [InlineData(2, "Dos Pesos")]
    [InlineData(21, "Veintiún Pesos")]
    [InlineData(31, "Treinta y Un Pesos")]
    [InlineData(100, "Cien Pesos")]
    [InlineData(101, "Ciento Un Pesos")]
    [InlineData(1000, "Mil Pesos")]
    [InlineData(21000, "Veintiún Mil Pesos")]
    [InlineData(60646, "Sesenta Mil Seiscientos Cuarenta y Seis Pesos")]
    [InlineData(124039, "Ciento Veinticuatro Mil Treinta y Nueve Pesos")]
    [InlineData(1130148, "Un Millón Ciento Treinta Mil Ciento Cuarenta y Ocho Pesos")]
    [InlineData(12345.67, "Doce Mil Trescientos Cuarenta y Cinco Pesos con 67/100")]
    [InlineData(300.5, "Trescientos Pesos con 50/100")]
    [InlineData(1000000, "Un Millón de Pesos")]
    [InlineData(2000000, "Dos Millones de Pesos")]
    [InlineData(2500000, "Dos Millones Quinientos Mil Pesos")]
    public void ConvierteImportes(double importe, string esperado)
        => Assert.Equal(esperado, NumeroALetras.Importe((decimal)importe));
}

public class DocumentoPermisosTests
{
    [Theory]
    [InlineData(typeof(DocumentosConfigController), "PlantillaEdit", "managetemplates")]
    [InlineData(typeof(DocumentosConfigController), "PlantillaActivar", "managetemplates")]
    [InlineData(typeof(DocumentosConfigController), "PlantillaRestaurar", "managetemplates")]
    [InlineData(typeof(DocumentosConfigController), "PlantillaPreview", "managetemplates")]
    [InlineData(typeof(DocumentosConfigController), "ReglaEdit", "managerules")]
    [InlineData(typeof(DocumentosConfigController), "ReglaActivar", "managerules")]
    [InlineData(typeof(DocumentosConfigController), "ReglaEliminar", "managerules")]
    [InlineData(typeof(DocumentosConfigController), "PaqueteEdit", "managerules")]
    [InlineData(typeof(DocumentosConfigController), "TipoEdit", "managetypes")]
    [InlineData(typeof(DocumentoController), "Ver", "view")]
    [InlineData(typeof(DocumentoController), "Reimprimir", "reprint")]
    [InlineData(typeof(DocumentoController), "Firmar", "sign")]
    [InlineData(typeof(DocumentoController), "Cancelar", "cancel")]
    [InlineData(typeof(DocumentoController), "Regenerar", "generate")]
    [InlineData(typeof(DocumentoController), "GenerarPendientes", "generate")]
    public void CadaAccion_ExigeSuPermisoEspecifico_EnTodasSusSobrecargas(Type controller, string accion, string permiso)
    {
        var metodos = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == accion).ToList();
        Assert.NotEmpty(metodos);

        foreach (var m in metodos)
        {
            var attr = m.GetCustomAttributes<PermisoRequeridoAttribute>().SingleOrDefault();
            Assert.NotNull(attr);
            Assert.Equal("documentos", attr!.Modulo);
            Assert.Equal(permiso, attr.Accion);
        }
    }

    [Theory]
    [InlineData(typeof(DocumentosConfigController))]
    [InlineData(typeof(DocumentoController))]
    public void LosControllers_ExigenAutenticacion(Type controller)
        => Assert.NotEmpty(controller.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());

    [Fact]
    public void TodasLasAccionesPost_ExigenAntiforgery()
    {
        foreach (var tipo in new[] { typeof(DocumentosConfigController), typeof(DocumentoController) })
        {
            var posts = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Any());
            foreach (var m in posts)
                Assert.True(m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>().Any(),
                    $"{tipo.Name}.{m.Name} debe validar antiforgery");
        }
    }

    [Fact]
    public void AlIniciarLaApp_NoSeSiembraNingunaConfiguracionDocumental()
    {
        // Solo código: el comentario que explica la decisión menciona el nombre del seeder.
        var inicializador = string.Join("\n", File.ReadAllLines(Path.Combine(FindRepoRoot(), "Data", "DbInitializer.cs"))
            .Where(l => !l.TrimStart().StartsWith("//")));

        // Plantillas, reglas, paquetes, tipos y la migración de contratos legados no se crean solos al arrancar.
        Assert.DoesNotContain("DocumentoSeeder.EnsureAsync", inicializador);
        Assert.DoesNotContain("SembrarConfiguracion", inicializador);
    }

    [Fact]
    public void ElScriptDeLimpiezaDocumental_NoTocaTablasComercialesNiElSistemaLegado()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "documentos", "limpiar-sistema-documental.sql"));
        var sentencias = string.Join("\n", sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--") && !l.TrimStart().StartsWith("/*")));

        foreach (var permitida in new[] { "DocumentosGenerados", "ReglasDocumento", "PaquetesDocumentalesItems", "PaquetesDocumentales",
                     "PlantillasDocumentoVersion", "PlantillasDocumento", "TiposDocumento" })
            Assert.Contains(permitida, sentencias);

        // Los DELETE solo apuntan a tablas documentales (nunca a ventas, créditos, pagos, ni al contrato legado).
        var destinos = System.Text.RegularExpressions.Regex.Matches(sentencias, @"DELETE\s+(?:FROM\s+)?(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        var alias = new[] { "d", "v", "p", "t" };   // alias de DELETE d FROM DocumentosGenerados d ...
        Assert.All(destinos.Where(x => !alias.Contains(x)), x => Assert.Contains(x,
            new[] { "ReglasDocumento", "PaquetesDocumentalesItems", "PaquetesDocumentales" }));
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", sentencias, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THROW 51001", sql);   // aborta si cambia un conteo comercial
    }

    [Fact]
    public void ElSeederDePermisos_DefineElModuloDocumentosConSusAcciones()
    {
        var fuente = File.ReadAllText(Path.Combine(FindRepoRoot(), "Data", "Seeds", "RolesPermisosSeeder.cs"));
        foreach (var accion in new[] { "\"view\"", "\"generate\"", "\"reprint\"", "\"sign\"", "\"cancel\"", "\"managetemplates\"", "\"managerules\"", "\"managetypes\"" })
            Assert.Contains($"\"documentos\"", fuente);
        Assert.Contains("(\"Administrar plantillas\", \"managetemplates\", 6)", fuente);

        // Vendedor y Cajero NO administran plantillas ni reglas.
        var vendedor = fuente[fuente.IndexOf("var vendedorRole", StringComparison.Ordinal)..];
        vendedor = vendedor[..vendedor.IndexOf("var cajeroRole", StringComparison.Ordinal)];
        Assert.DoesNotContain("managetemplates", vendedor);
        Assert.DoesNotContain("managerules", vendedor);
        Assert.Contains("{ \"documentos\", new[] { \"view\", \"generate\", \"reprint\", \"sign\" } }", vendedor);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repo.");
    }
}
