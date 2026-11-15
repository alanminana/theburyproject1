using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-03: contrato estático de las pantallas de cuentas por pagar (antiforgery, ARIA, formato argentino,
/// permiso por acción, sin lógica de negocio en controller/Razor/JS).
/// </summary>
public class CuentaPorPagarUiContractTests
{
    private static string Leer(params string[] partes) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(partes).ToArray()));

    private static readonly string[] Vistas =
    {
        "Index_tw", "Detalle_tw", "Crear_tw", "Pagar_tw", "Pago_tw", "ImputarNotaCredito_tw", "EstadoCuenta_tw", "Antiguedad_tw"
    };

    [Fact]
    public void TodoPostDelControllerTieneAntiforgeryYPermisoPropio()
    {
        var codigo = Leer("Controllers", "CuentaPorPagarController.cs");
        var bloques = Regex.Split(codigo, @"\[HttpPost\]").Skip(1).ToList();

        Assert.Equal(6, bloques.Count); // Crear, Anular, Pagar, AnularPago, ImputarNotaCredito, AnularImputacionNotaCredito
        foreach (var bloque in bloques)
        {
            var cabecera = bloque[..bloque.IndexOf("public async", StringComparison.Ordinal)];
            Assert.Contains("[ValidateAntiForgeryToken]", cabecera);
            Assert.Contains("[PermisoRequerido(Modulo = \"cuentasporpagar\"", cabecera);
        }
    }

    [Fact]
    public void LosPermisosSeparanRegistrarComprobantesPagarYAnular()
    {
        var codigo = Leer("Controllers", "CuentaPorPagarController.cs").Replace("\r\n", "\n");

        Assert.Contains("[PermisoRequerido(Modulo = \"cuentasporpagar\", Accion = \"view\")]\n    public class CuentaPorPagarController", codigo);
        foreach (var (accion, permiso) in new[]
                 {
                     ("Crear", "createinvoice"), ("Pagar", "pay"), ("ImputarNotaCredito", "pay"),
                     ("Anular", "cancel"), ("AnularPago", "cancel"), ("AnularImputacionNotaCredito", "cancel")
                 })
        {
            var patron = new Regex($@"Accion = ""{permiso}""\)\]\s+public async Task<IActionResult> {accion}\(");
            Assert.True(patron.IsMatch(codigo), $"{accion} debe exigir cuentasporpagar.{permiso}.");
        }
    }

    [Fact]
    public void ElControllerNoContieneLogicaDeNegocio()
    {
        var codigo = Leer("Controllers", "CuentaPorPagarController.cs");

        Assert.DoesNotContain("AppDbContext", codigo);
        Assert.DoesNotContain("decimal.Round", codigo);
        Assert.DoesNotContain("Math.Round", codigo);
    }

    [Theory]
    [InlineData("Crear_tw")]
    [InlineData("Pagar_tw")]
    [InlineData("ImputarNotaCredito_tw")]
    public void LosFormulariosDeEscrituraSonPostConAntiforgery(string vista)
    {
        var html = Leer("Views", "CuentaPorPagar", vista + ".cshtml");

        Assert.Contains("method=\"post\"", html);
        // El tag helper de form agrega el token cuando hay asp-action; no debe haber formularios POST con action escrita a mano.
        Assert.DoesNotContain("<form action=", html);
        Assert.DoesNotContain("asp-antiforgery=\"false\"", html);
    }

    [Fact]
    public void LaAnulacionPideMotivoObligatorioYUsaFormularioConAntiforgery()
    {
        var html = Leer("Views", "CuentaPorPagar", "_CxpAnular.cshtml");

        Assert.Contains("name=\"motivo\"", html);
        Assert.Contains("required", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("<label", html);
    }

    [Theory]
    [InlineData("Index_tw")]
    [InlineData("Detalle_tw")]
    [InlineData("Pagar_tw")]
    [InlineData("Pago_tw")]
    [InlineData("ImputarNotaCredito_tw")]
    [InlineData("EstadoCuenta_tw")]
    [InlineData("Antiguedad_tw")]
    public void LasTablasTienenRegionConNombreAccesibleYEncabezadosConScope(string vista)
    {
        var html = Leer("Views", "CuentaPorPagar", vista + ".cshtml");

        Assert.Contains("role=\"region\"", html);
        Assert.Contains("aria-label=", html);
        Assert.Contains("tabindex=\"0\"", html);
        Assert.Contains("<th scope=\"col\"", html);
        Assert.DoesNotContain("<th class=", html);
    }

    [Fact]
    public void LosListadosTienenEstadoVacioYLosFiltrosEstanEtiquetados()
    {
        var index = Leer("Views", "CuentaPorPagar", "Index_tw.cshtml");

        Assert.Contains("data-cxp-vacio", index);
        foreach (var id in new[] { "f-proveedor", "f-tipo", "f-estado", "f-busqueda", "f-desde", "f-hasta" })
        {
            Assert.Contains($"for=\"{id}\"", index);
            Assert.Contains($"id=\"{id}\"", index);
        }

        foreach (var vista in new[] { "Pagar_tw", "ImputarNotaCredito_tw", "EstadoCuenta_tw", "Antiguedad_tw" })
            Assert.Contains("data-cxp-vacio", Leer("Views", "CuentaPorPagar", vista + ".cshtml"));
    }

    [Fact]
    public void LosCamposDeLosFormulariosTienenLabelAsociado()
    {
        var crear = Leer("Views", "CuentaPorPagar", "Crear_tw.cshtml");
        foreach (var campo in new[] { "Tipo", "NumeroComprobante", "OrdenCompraId", "FechaEmision", "FechaVencimiento", "ImporteNeto", "ImporteIva", "ImporteTotal", "Observaciones" })
        {
            Assert.Contains($"for=\"{campo}\"", crear);
            Assert.Contains($"id=\"{campo}\"", crear);
        }

        var pagar = Leer("Views", "CuentaPorPagar", "Pagar_tw.cshtml");
        foreach (var campo in new[] { "Fecha", "Medio", "Monto", "Referencia", "ChequeId", "Observaciones" })
        {
            Assert.Contains($"for=\"{campo}\"", pagar);
            Assert.Contains($"id=\"{campo}\"", pagar);
        }
        // Cada importe de imputación tiene nombre accesible propio.
        Assert.Contains("aria-label=\"Importe a imputar al comprobante", pagar);
    }

    [Theory]
    [InlineData("Index_tw")]
    [InlineData("Detalle_tw")]
    [InlineData("Pagar_tw")]
    [InlineData("Pago_tw")]
    [InlineData("ImputarNotaCredito_tw")]
    [InlineData("EstadoCuenta_tw")]
    [InlineData("Antiguedad_tw")]
    public void LosImportesSeFormateanConElHelperArgentinoYNoConSignoDolarHardcodeado(string vista)
    {
        var html = Leer("Views", "CuentaPorPagar", vista + ".cshtml");

        Assert.DoesNotContain("ToString(\"N2\")", html);
        Assert.DoesNotContain("ToString(\"C2\")", html);
        Assert.DoesNotContain(">$@", html);
        Assert.DoesNotContain("$@", html.Replace("$@\"", "").Replace("$\"", ""));
    }

    [Fact]
    public void ElHelperDeUiFormateaEnEsArYNoDependeDeLaCulturaDelProceso()
    {
        var helper = Leer("Helpers", "CuentaPorPagarUiHelper.cs");

        Assert.Contains("CultureInfo.GetCultureInfo(\"es-AR\")", helper);
        Assert.Contains("ToString(\"C2\", Ar)", helper);
    }

    [Fact]
    public void ElScriptSoloAyudaVisualmenteYNoContieneReglasDeNegocio()
    {
        var js = Leer("wwwroot", "js", "cuenta-por-pagar-imputacion.js");

        Assert.Contains("NO decide nada", js);
        Assert.DoesNotContain("fetch(", js);
        Assert.DoesNotContain("XMLHttpRequest", js);
        Assert.DoesNotContain("innerHTML", js);
    }

    [Fact]
    public void ElMenuLateralYLosEnlacesExigenElPermisoCorrespondiente()
    {
        var layout = Leer("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("User.TienePermiso(\"cuentasporpagar\", \"view\")", layout);
        Assert.Contains("asp-controller=\"CuentaPorPagar\"", layout);

        var proveedor = Leer("Views", "Proveedor", "Details_tw.cshtml");
        Assert.Contains("User.TienePermiso(\"cuentasporpagar\", \"view\")", proveedor);

        var orden = Leer("Views", "OrdenCompra", "Details_tw.cshtml");
        Assert.Contains("User.TienePermiso(\"cuentasporpagar\", \"createinvoice\")", orden);
    }

    [Fact]
    public void LaMigracionAddCuentasPorPagarSoloCreaLoNuevo()
    {
        var carpeta = Path.Combine(FindRepoRoot(), "Migrations");
        var archivo = Directory.GetFiles(carpeta, "*_AddCuentasPorPagar.cs").Single();
        var codigo = File.ReadAllText(archivo);

        Assert.Equal(4, Regex.Matches(codigo, @"migrationBuilder\.CreateTable\(").Count);
        foreach (var tabla in new[] { "ComprobantesProveedor", "PagosProveedor", "PagoProveedorImputaciones", "ImputacionesNotaCredito" })
            Assert.Contains($"name: \"{tabla}\"", codigo);
        Assert.DoesNotContain("AddColumn", codigo);
        Assert.DoesNotContain("AlterColumn", codigo);
        Assert.DoesNotContain("DropColumn", codigo);
        Assert.DoesNotContain("DropIndex", codigo);
        Assert.DoesNotContain("DeleteData", codigo);
        Assert.DoesNotContain("InsertData", codigo);
        Assert.DoesNotContain("onDelete: ReferentialAction.Cascade", codigo);
        Assert.Contains("onDelete: ReferentialAction.Restrict", codigo);
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
