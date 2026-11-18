using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-03 Tesorería: contratos de la capa web y de alcance. No prueban comportamiento (eso lo hacen los servicios y los HTTP): fijan
/// reglas que no deben erosionarse sin una decisión expresa: permisos propios y antiforgery en cada POST, vistas sin manejadores inline
/// (CSP), nada de endosos ni de cheques de clientes en los pagos a proveedores, y la separación respecto de la Caja de ventas.
/// </summary>
public class TesoreriaUiContractTests
{
    private static string Leer(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray()));
    }

    private static string SinComentarios(string codigo) =>
        string.Join("\n", codigo.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void TodoPostDelControllerDeTesoreria_TieneAntiforgeryYElPermisoDeConfigurar()
    {
        var codigo = Leer("Controllers", "TesoreriaController.cs");
        var bloques = Regex.Split(codigo, @"\[HttpPost\]").Skip(1).ToList();

        Assert.Equal(2, bloques.Count); // Guardar, Eliminar
        foreach (var bloque in bloques)
        {
            var cabecera = bloque[..bloque.IndexOf("public async", StringComparison.Ordinal)];
            Assert.Contains("[ValidateAntiForgeryToken]", cabecera);
            Assert.Contains("[PermisoRequerido(Modulo = \"tesoreria\", Accion = \"configure\")]", cabecera);
        }

        Assert.Contains("[PermisoRequerido(Modulo = \"tesoreria\", Accion = \"view\")]", codigo.Replace("\r\n", "\n"));
    }

    [Fact]
    public void LasVistasDeTesoreriaYAnticiposNoTienenManejadoresInlineNiMontosFijos()
    {
        var vistas = new[]
        {
            new[] { "Views", "Tesoreria", "Index_tw.cshtml" }, new[] { "Views", "Tesoreria", "Form_tw.cshtml" },
            new[] { "Views", "CuentaPorPagar", "Anticipar_tw.cshtml" }, new[] { "Views", "CuentaPorPagar", "Anticipo_tw.cshtml" },
            new[] { "Views", "CuentaPorPagar", "AplicarAnticipo_tw.cshtml" }
        };
        foreach (var v in vistas)
        {
            var html = Leer(v);
            Assert.DoesNotMatch(@"\son(click|change|submit|input)\s*=", html);
            Assert.DoesNotContain("javascript:", html);
            Assert.DoesNotMatch(@"(?<!data-cxp-total)>\s*\$\s*\d", html); // los importes salen del helper es-AR; solo el placeholder del total lo escribe el JS
        }
    }

    [Fact]
    public void LosFormulariosDeTesoreriaYAnticiposTienenLabelAsociadoACadaCampo()
    {
        foreach (var vista in new[]
                 {
                     new[] { "Views", "Tesoreria", "Form_tw.cshtml" }, new[] { "Views", "CuentaPorPagar", "Anticipar_tw.cshtml" }
                 })
        {
            var html = Leer(vista);
            foreach (Match campo in Regex.Matches(html, @"<(?:input|select)\b[^>]*\bid=""(\w+)""[^>]*>"))
            {
                var tipo = Regex.Match(campo.Value, @"type=""(\w+)""").Groups[1].Value;
                if (tipo == "hidden")
                    continue;
                var id = campo.Groups[1].Value;
                Assert.True(html.Contains($"for=\"{id}\"", StringComparison.Ordinal), $"{string.Join('/', vista)}: el campo #{id} no tiene <label for>.");
            }
        }
    }

    [Fact]
    public void LosPagosYAnticiposAProveedor_NoImplementanEndososNiEmitenChequesPropios()
    {
        // Decisión del propietario: los cheques de clientes siguen siendo informativos (sin endosos automáticos) y no se emiten cheques
        // propios hasta resolverlo expresamente. El libro solo REFERENCIA un cheque ya emitido y no cambia su ciclo de vida.
        foreach (var archivo in new[] { "CuentaPorPagarService.cs", "CuentaPorPagarService.Anticipos.cs", "TesoreriaService.cs" })
        {
            var codigo = SinComentarios(Leer("Services", archivo));
            Assert.DoesNotContain("ChequeRecibido", codigo);
            Assert.DoesNotContain("Endos", codigo);
            Assert.DoesNotContain("_context.Cheques.Add", codigo);
            Assert.DoesNotContain("EstadoCheque.Entregado", codigo);
            Assert.DoesNotContain("ChequeService", codigo);
            Assert.DoesNotMatch(@"\.Estado\s*=\s*EstadoCheque\.", codigo); // el pago no cambia el estado del cheque
        }
    }

    [Fact]
    public void LaTesoreria_EstaSeparadaDeLaCajaDeVentas()
    {
        foreach (var archivo in new[]
                 {
                     new[] { "Controllers", "TesoreriaController.cs" }, new[] { "Services", "TesoreriaService.cs" },
                     new[] { "Services", "CuentaPorPagarService.Anticipos.cs" }
                 })
        {
            var codigo = SinComentarios(Leer(archivo));
            Assert.DoesNotContain("ICajaService", codigo);
            Assert.DoesNotContain("AperturaCaja", codigo);
            Assert.DoesNotContain("MovimientoCaja", codigo);
        }

        // Y tiene permisos propios: tesoreria.view / tesoreria.configure, distintos de los de caja.
        var seed = Leer("Data", "Seeds", "RolesPermisosSeeder.cs");
        Assert.Contains("(\"Tesorería\", \"tesoreria\"", seed);
        Assert.Contains("(\"Configurar orígenes de fondos\", \"configure\", 2)", seed);
    }

    [Fact]
    public void LaMigracionDeTesoreria_EsAditivaYNoTocaDatosExistentes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        var archivo = Directory.GetFiles(Path.Combine(dir!.FullName, "Migrations"), "*_D03_TesoreriaOrigenesAnticipos.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var up = File.ReadAllText(archivo);
        up = up[..up.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.DoesNotContain("DeleteData", up);
        Assert.DoesNotContain("InsertData", up);
        Assert.DoesNotContain("UpdateData", up);
        // Única sentencia SQL permitida: conciliar cheques repetidos del esquema anterior, solo ChequeId y Observaciones de los pagos que
        // repiten un cheque (el más antiguo lo conserva). Nunca importes, imputaciones, estados ni borrados.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(up, @"migrationBuilder\.Sql\(").Count);
        var sql = up[up.IndexOf("migrationBuilder.Sql(", StringComparison.Ordinal)..];
        sql = sql[..sql.IndexOf("\");", StringComparison.Ordinal)];
        Assert.Contains("SET p.ChequeId = NULL", sql);
        Assert.Contains("p.Id <> k.IdConserva", sql);
        Assert.DoesNotMatch(@"(?i)DELETE", sql); // (IsDeleted es otra cosa: solo se lee)
        Assert.DoesNotContain("Monto", sql);
        Assert.DoesNotContain("Anulado =", sql.Replace("Anulado = 0", ""));
        Assert.DoesNotContain("DropTable", up);
        Assert.DoesNotContain("DropColumn", up);
        Assert.Contains("OrigenFondosId", up);
    }
}
