/// <summary>
/// Contratos de no-presencia del admin legacy ProductoCondicionPago.
/// Protegen contra reintroduccion accidental de modal, asset JS, endpoints y DI retirados en Fase 7.10.
/// </summary>
[Trait("Area", "LegacyPagoPorProducto")]
public class ProductoCondicionesPagoAdminLegacyDespublicadoTests
{
    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(new[] { current!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void LegacyAdmin_NoPresente_EnVistaYAsset()
    {
        var view = File.ReadAllText(RepoFile("Views", "Catalogo", "Index_tw.cshtml"));

        Assert.DoesNotContain("data-condiciones-pago-producto-id", view);
        Assert.DoesNotContain("data-condiciones-pago-producto-nombre", view);
        Assert.DoesNotContain("row-action__label\">Condiciones de pago</span>", view);
        Assert.DoesNotContain("modal-condiciones-pago-producto", view);
        Assert.DoesNotContain("form-condiciones-pago-producto", view);
        Assert.DoesNotContain("modal-condiciones-medio", view);
        Assert.DoesNotContain("producto-condiciones-pago-modal.js", view);

        Assert.False(File.Exists(RepoFile("wwwroot", "js", "producto-condiciones-pago-modal.js")));
    }

    [Fact]
    public void LegacyAdmin_NoPresente_EnControllerYDI()
    {
        var controller = File.ReadAllText(RepoFile("Controllers", "ProductoController.cs"));

        Assert.DoesNotContain("Producto/CondicionesPago/{productoId:int}", controller);
        Assert.DoesNotContain("ObtenerCondicionesPago", controller);
        Assert.DoesNotContain("GuardarCondicionesPago", controller);
        Assert.DoesNotContain("IProductoCondicionPagoService", controller);

        var program = File.ReadAllText(RepoFile("Program.cs"));

        Assert.DoesNotContain("IProductoCondicionPagoService", program);
        Assert.DoesNotContain("ProductoCondicionPagoService", program);
        Assert.DoesNotContain("ICondicionesPagoCarritoResolver", program);
    }
}
