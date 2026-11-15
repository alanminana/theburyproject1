using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-01: contrato estático de las pantallas de listas de precios (antiforgery, permiso por acción, labels, sin lógica de
/// negocio en controller/Razor/JS), de la migración D01 y de que el ABM no toca el cálculo de precio de Venta/Cotización.
/// </summary>
public class ListaPrecioUiContractTests
{
    private static string Leer(params string[] partes) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(partes).ToArray())).Replace("\r\n", "\n");

    private static readonly string[] Vistas = { "Index_tw", "Form_tw", "Precios_tw", "FijarPrecio_tw" };

    [Fact]
    public void LaClaseExigePreciosViewYEstaProtegidaContraCsrf()
    {
        var codigo = Leer("Controllers", "ListaPrecioController.cs");

        Assert.Contains("[AutoValidateAntiforgeryToken]\n    [PermisoRequerido(Modulo = \"precios\", Accion = \"view\")]\n    public class ListaPrecioController", codigo);
    }

    [Fact]
    public void CadaAccionDeEscrituraExigeSuPermisoPropio()
    {
        var codigo = Leer("Controllers", "ListaPrecioController.cs");
        var posts = Regex.Split(codigo, @"\[HttpPost\]").Skip(1).ToList();

        Assert.Equal(4, posts.Count); // Crear, Editar, Baja, FijarPrecio
        foreach (var (accion, permiso) in new[] { ("Crear", "create"), ("Editar", "update"), ("Baja", "delete"), ("FijarPrecio", "apply") })
        {
            var patron = new Regex($@"Accion = ""{permiso}""\)\]\s+public async Task<IActionResult> {accion}\(");
            Assert.True(patron.IsMatch(codigo), $"{accion} (POST) debe exigir precios.{permiso}.");
        }
        foreach (var (accion, permiso) in new[] { ("Editar", "update"), ("FijarPrecio", "apply") })
        {
            var patron = new Regex($@"\[HttpGet\]\s+\[PermisoRequerido\(Modulo = ""precios"", Accion = ""{permiso}""\)\]\s+public async Task<IActionResult> {accion}\(");
            Assert.True(patron.IsMatch(codigo), $"{accion} (GET) debe exigir precios.{permiso}.");
        }
    }

    [Fact]
    public void ElControllerCoordinaYAuditaSinLogicaDeNegocio()
    {
        var codigo = Leer("Controllers", "ListaPrecioController.cs");

        Assert.DoesNotContain("AppDbContext", codigo);
        Assert.DoesNotContain("Math.Round", codigo);
        Assert.DoesNotContain("decimal.Round", codigo);
        foreach (var evento in new[] { "crear-lista", "editar-lista", "baja-lista", "fijar-precio" })
            Assert.Contains($"\"{evento}\"", codigo);
    }

    [Fact]
    public void ElAbmNoCambiaElCalculoDePrecioNiAsignaListasAClientes()
    {
        // R-07 y R-03 siguen siendo decisiones del propietario: ni el controller ni las vistas del ABM tocan Venta, Cotización,
        // Cliente ni el costo del producto.
        var codigo = Leer("Controllers", "ListaPrecioController.cs");
        foreach (var prohibido in new[] { "IVentaService", "ICotizacionService", "IClienteService", "PrecioVigenteResolver", "PrecioCompra =" })
            Assert.DoesNotContain(prohibido, codigo);

        foreach (var servicio in new[] { "VentaService.cs", "CotizacionService.cs", "CotizacionConversionService.cs" })
        {
            var fuente = Leer("Services", servicio);
            Assert.DoesNotContain("SetPrecioManualAsync", fuente);
            Assert.DoesNotContain("CreateListaAsync", fuente);
            Assert.DoesNotContain("UpdateListaAsync", fuente);
        }

        // La venta resuelve el precio sin lista explícita (la predeterminada o el precio base), no por cliente.
        Assert.Contains("_precioVigenteResolver.ResolverBatchAsync(productoIds)", Leer("Services", "VentaService.cs"));
        Assert.DoesNotContain("ListaPrecioId", Leer("Models", "Entities", "Cliente.cs"));
    }

    [Fact]
    public void LasVistasUsanFormatoArgentinoYNoFijanElSimboloDeMoneda()
    {
        foreach (var vista in Vistas)
        {
            var html = Leer("Views", "ListaPrecio", vista + ".cshtml");
            Assert.DoesNotContain("$@", html);
            Assert.DoesNotContain("ToString(\"N2\")", html);
        }
    }

    [Fact]
    public void CadaControlDeFormularioTieneLabelAsociado()
    {
        foreach (var vista in new[] { "Form_tw", "Precios_tw", "FijarPrecio_tw" })
        {
            var html = Leer("Views", "ListaPrecio", vista + ".cshtml");
            var ids = Regex.Matches(html, @"<(?:input|select|textarea)\b[^>]*\bid=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(ids);
            foreach (var id in ids)
                Assert.True(html.Contains($"for=\"{id}\""), $"{vista}: el control #{id} no tiene <label for>.");
        }
    }

    [Fact]
    public void LosFormulariosDePostUsanElTagHelperQueIncluyeElTokenAntiforgery()
    {
        foreach (var vista in Vistas)
        {
            var html = Leer("Views", "ListaPrecio", vista + ".cshtml");
            foreach (Match form in Regex.Matches(html, @"<form\b[^>]*>"))
            {
                if (!form.Value.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase))
                    continue;
                Assert.Contains("asp-action=", form.Value);
                Assert.DoesNotContain("asp-antiforgery=\"false\"", form.Value);
            }
        }
    }

    [Fact]
    public void LaBajaSoloSeOfreceConPermisoYNuncaParaLaPredeterminada()
    {
        var html = Leer("Views", "ListaPrecio", "Index_tw.cshtml");

        Assert.Contains("@if (Model.PuedeEliminar && !l.EsPredeterminada)", html);
        Assert.Contains("@if (Model.PuedeEditar)", html);
        Assert.Contains("@if (Model.PuedeCrear)", html);
        Assert.Contains("@if (puedeFijar)", Leer("Views", "ListaPrecio", "Precios_tw.cshtml"));
    }

    [Fact]
    public void ElScriptSoloConfirmaLaBajaYNoContieneReglasDePrecio()
    {
        var js = Leer("wwwroot", "js", "lista-precio.js");

        Assert.Contains("window.confirm", js);
        Assert.DoesNotContain("margen", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", js);
    }

    [Fact]
    public void ElMenuYLasPantallasDeLotesSeEnlazan()
    {
        var layout = Leer("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("User.TienePermiso(\"precios\", \"view\")", layout);
        Assert.Contains("asp-controller=\"ListaPrecio\"", layout);

        Assert.Contains("asp-controller=\"CambiosPrecios\" asp-action=\"Index\"", Leer("Views", "ListaPrecio", "Index_tw.cshtml"));
        Assert.Contains("asp-controller=\"ListaPrecio\" asp-action=\"Index\"", Leer("Views", "CambiosPrecios", "Index.cshtml"));
        Assert.Contains("User.TienePermiso(\"precios\", \"history\")", Leer("Views", "CambiosPrecios", "Historial.cshtml"));
    }

    [Fact]
    public void LaMigracionD01SoloCambiaElIndiceDeLaListaPredeterminada()
    {
        var carpeta = Path.Combine(FindRepoRoot(), "Migrations");
        var archivo = Directory.GetFiles(carpeta, "*_D01_ListaPrecioPredeterminadaUnica.cs").Single();
        var codigo = File.ReadAllText(archivo);

        Assert.Equal(2, Regex.Matches(codigo, @"migrationBuilder\.DropIndex\(").Count); // Up y Down
        Assert.Equal(2, Regex.Matches(codigo, @"migrationBuilder\.CreateIndex\(").Count);
        Assert.Contains("name: \"IX_ListasPrecios_EsPredeterminada\"", codigo);
        Assert.Contains("filter: \"[IsDeleted] = 0 AND [EsPredeterminada] = 1\"", codigo);
        Assert.DoesNotContain("CreateTable", codigo);
        Assert.DoesNotContain("AddColumn", codigo);
        Assert.DoesNotContain("AlterColumn", codigo);
        Assert.DoesNotContain("DropColumn", codigo);
        Assert.DoesNotContain("DeleteData", codigo);
        Assert.DoesNotContain("InsertData", codigo);
        Assert.DoesNotContain("Sql(", codigo);
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
